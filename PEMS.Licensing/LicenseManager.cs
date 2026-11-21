using PEMS.Licensing.Crypto;
using PEMS.Licensing.Evaluation;
using PEMS.Licensing.Fingerprint;
using PEMS.Licensing.Formats;
using PEMS.Licensing.Seal;

namespace PEMS.Licensing;

public enum CheckReason { Startup, Periodic, OnDemand }

public sealed record ApplyOutcome(bool Applied, LicenseFailure Failure, string? Detail, LicenseStatus? Status);

/// <summary>
/// The single licensing engine of the product (guideline: engines once). Startup, periodic job, middleware refresh,
/// sensitive-operation guard and the licence page all go through here. When <c>Enforce</c> is false it still builds
/// requests and verifies uploads (so the flow can be tried) but never changes state on its own and blocks nothing.
/// </summary>
public sealed class LicenseManager
{
    private readonly LicensingSettings _settings;
    private readonly SealService _seal;
    private readonly IInstallKeyStore _keys;
    private readonly Func<ISignalSource> _signals;
    private readonly TrustedKeySet _trusted;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LicenseManager(LicensingSettings settings, SealService seal, IInstallKeyStore keys, Func<ISignalSource> signals, TrustedKeySet trusted)
    {
        _settings = settings;
        _seal = seal;
        _keys = keys;
        _signals = signals;
        _trusted = trusted;
    }

    public LicensingSettings Settings => _settings;

    /// <summary>Full check (design 7.1 V1/V3). Startup and heartbeat raise the counter; a check never throws for licensing reasons.</summary>
    public async Task<LicenseStatus> CheckAsync(CheckReason reason, CancellationToken ct = default)
    {
        if (!_settings.Enforce) return LicenseStatus.NotEnforced;
        await _gate.WaitAsync(ct);
        try { return await CheckCoreAsync(reason, ct); }
        finally { _gate.Release(); }
    }

    /// <summary>The status without enforcement short-circuit: used by the licence page to show real evidence even when Enforce is false.</summary>
    public async Task<LicenseStatus> InspectAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { return await CheckCoreAsync(CheckReason.OnDemand, ct, mutate: false); }
        finally { _gate.Release(); }
    }

    private async Task<LicenseStatus> CheckCoreAsync(CheckReason reason, CancellationToken ct, bool mutate = true)
    {
        var fingerprint = FingerprintCollector.Collect(_signals());
        var check = await _seal.LoadAsync(ct);
        var now = _seal.UtcNow;

        if (check.Record is null || check.Status is SealStatus.Invalid)
        {
            var empty = LicenseEvaluator.Evaluate(
                new EvaluationInput(null, check, null, new ClockEvaluation(now, false), FingerprintCollector.ShortForm(fingerprint.FpHash)),
                EvalSettings());
            return empty;
        }

        var record = check.Record;
        var newestEvent = (await _seal.LastEventAsync(ct))?.Utc;
        var clock = ClockGuard.Evaluate(now, record.LastSeenUtc, newestEvent, _settings.ClockTolerance);

        VerifiedLicence? verified = null;
        if (check.Licence is not null)
        {
            var result = LicenseVerifier.VerifyBinding(check.Licence,
                new VerificationContext(_trusted, record.InstallId, record.InstallPub, fingerprint));
            if (result.Ok) verified = result.Value;
        }

        if (mutate)
        {
            if (check.Status == SealStatus.RestoreSuspected && !record.RestoreSuspected)
                record = await _seal.UpdateAsync(record, r => r with
                {
                    RestoreSuspected = true, RestoreDetectedUtc = now, Counter = r.Counter + 1, LastSeenUtc = clock.EffectiveNow,
                }, check.Licence, "restore_suspected", string.Join(",", check.Notes), ct);
            else if (clock.BackwardJump)
                record = await _seal.UpdateAsync(record, r => r with { LastSeenUtc = clock.EffectiveNow }, check.Licence,
                    "clock_backward", $"system={now:O};floor={record.LastSeenUtc:O}", ct);
            else if (reason == CheckReason.Startup || now - record.LastSeenUtc >= _settings.Heartbeat)
                record = await _seal.RecordStartAsync(record, clock.EffectiveNow, check.Licence, reason == CheckReason.Startup ? "start" : "heartbeat", ct);

            if (verified is { Match.Result: not FingerprintResult.Match } v)
                await TryEventAsync(record, check.Licence, "fingerprint_" + v.Match.Result.ToString().ToLowerInvariant(), string.Join(",", v.Match.ChangedSignals), ct);
        }

        var evaluated = LicenseEvaluator.Evaluate(
            new EvaluationInput(verified?.Payload, check with { Record = record }, verified?.Match, clock, FingerprintCollector.ShortForm(fingerprint.FpHash)),
            EvalSettings());

        if (verified is null && check.Licence is not null)
            evaluated = evaluated with { Reasons = evaluated.Reasons.Append(LicenseReasons.LicenceUnverifiable).ToList() };
        return evaluated;
    }

    /// <summary>Creates the seal on first use and returns the signed request file (design 5.2, 6).</summary>
    public async Task<BuiltRequest> CreateRequestAsync(string requestType, string customerRef, string orderRef, string? reason, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var fingerprint = FingerprintCollector.Collect(_signals());
            var check = await _seal.LoadAsync(ct);
            string? previousLicence = null;
            SealRecord record;
            string? licence = null;

            if (check.Record is null || check.Status == SealStatus.Invalid)
            {
                previousLicence = check.Record?.LicenceId;   // evidence from a moved image: the vendor sees what it replaces
                record = await _seal.CreateAsync(fingerprint, ct);
            }
            else
            {
                record = check.Record;
                licence = check.Licence;
            }

            using var signer = _keys.Load() ?? throw new InvalidOperationException("The install key is missing.");
            var built = RequestBuilder.Build(requestType, record, fingerprint, signer, _seal.UtcNow, customerRef, orderRef,
                previousLicence, reason, _settings.ProductVersion, _settings.BuildDate);

            // Keep the last 8 pending requests; older ones can no longer be answered.
            await _seal.UpdateAsync(record, r => r with
            {
                PendingRequests = r.PendingRequests.Append(built.Pending).TakeLast(8).ToList(),
            }, licence, "request_created", requestType + ":" + built.Pending.Nonce, ct);
            return built;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Verifies and activates an uploaded .pemslic or .pemslic.zip (design 6 upload pipeline, V5).</summary>
    public async Task<ApplyOutcome> ApplyLicenceAsync(byte[] data, CancellationToken ct = default)
    {
        List<string> texts;
        if (LicenceArchive.LooksLikeZip(data))
        {
            var extracted = LicenceArchive.Extract(data);
            if (!extracted.Ok) return new ApplyOutcome(false, extracted.Failure, extracted.Detail, null);
            texts = extracted.Value!;
        }
        else
        {
            if (data.Length > FileFormats.MaxLicenceBytes) return new ApplyOutcome(false, LicenseFailure.TooLarge, null, null);
            texts = new List<string> { System.Text.Encoding.UTF8.GetString(data) };
        }

        await _gate.WaitAsync(ct);
        try
        {
            var check = await _seal.LoadAsync(ct);
            if (check.Record is null || check.Status == SealStatus.Invalid)
                return new ApplyOutcome(false, LicenseFailure.SealInvalid, null, null);

            var record = check.Record;
            var fingerprint = FingerprintCollector.Collect(_signals());
            var ctx = new VerificationContext(_trusted, record.InstallId, record.InstallPub, fingerprint);
            LicensePayload? current = null;
            if (check.Licence is not null && LicenseVerifier.VerifyBinding(check.Licence, ctx) is { Ok: true } cur) current = cur.Value!.Payload;

            var newestEvent = (await _seal.LastEventAsync(ct))?.Utc;
            var clock = ClockGuard.Evaluate(_seal.UtcNow, record.LastSeenUtc, newestEvent, _settings.ClockTolerance);

            LicenseFailure firstFailure = LicenseFailure.None;
            string? firstDetail = null;
            VerifiedLicence? chosen = null;
            string? chosenText = null;
            foreach (var text in texts)
            {
                var r = LicenseVerifier.VerifyForUpload(text, ctx, record.PendingRequests, current, clock.EffectiveNow);
                if (!r.Ok) { if (firstFailure == LicenseFailure.None) { firstFailure = r.Failure; firstDetail = r.Detail; } continue; }
                if (chosen is null || r.Value!.Payload.IssueSeq > chosen.Payload.IssueSeq) { chosen = r.Value; chosenText = text; }
            }
            if (chosen is null) return new ApplyOutcome(false, firstFailure, firstDetail, null);

            var answered = record.PendingRequests.First(p => p.Nonce == chosen.Payload.RequestNonce);
            var clears = answered.RequestType is RequestTypes.Restore or RequestTypes.Transfer or RequestTypes.Activation or RequestTypes.Refresh or RequestTypes.Renewal;
            var next = await _seal.UpdateAsync(record, r => r with
            {
                Counter = r.Counter + 1,
                LastSeenUtc = clock.EffectiveNow,
                LicenceId = chosen.Payload.LicenceId,
                LicenceHash = chosen.Hash,
                FpEnrol = chosen.Payload.Binding.Fingerprint,
                State = "Active",
                RestoreSuspected = clears ? false : r.RestoreSuspected,
                RestoreDetectedUtc = clears ? null : r.RestoreDetectedUtc,
                PendingRequests = r.PendingRequests.Where(p => p.Nonce != answered.Nonce).ToList(),
            }, chosenText, "licence_applied", chosen.Payload.LicenceId + ":" + answered.RequestType, ct);

            var status = LicenseEvaluator.Evaluate(
                new EvaluationInput(chosen.Payload, check with { Record = next, Status = SealStatus.Ok }, chosen.Match, clock, FingerprintCollector.ShortForm(fingerprint.FpHash)),
                EvalSettings());
            return new ApplyOutcome(true, LicenseFailure.None, null, status);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Licence entitlement test for a sensitive operation (design V4): the named module, or the update entitlement for a build date.</summary>
    public async Task<bool> IsModuleGrantedAsync(string module, CancellationToken ct = default)
    {
        if (!_settings.Enforce) return true;
        var check = await _seal.LoadAsync(ct);
        if (check.Record is null || check.Licence is null) return false;
        var fingerprint = FingerprintCollector.Collect(_signals());
        var r = LicenseVerifier.VerifyBinding(check.Licence, new VerificationContext(_trusted, check.Record.InstallId, check.Record.InstallPub, fingerprint));
        return r.Ok && r.Value!.Match.Result != FingerprintResult.Invalid && r.Value.Payload.Grant.Modules.Contains(module, StringComparer.Ordinal);
    }

    /// <summary>Update packages are allowed only for builds released up to <c>updates_until</c> (perpetual licence plus maintenance, C2).</summary>
    public async Task<bool> IsBuildEntitledAsync(DateTimeOffset buildDate, CancellationToken ct = default)
    {
        if (!_settings.Enforce) return true;
        var status = await CheckAsync(CheckReason.OnDemand, ct);
        return status.State is LicenseState.Active or LicenseState.ActiveWarning
            && (status.UpdatesUntil is null || buildDate <= status.UpdatesUntil.Value);
    }

    private async Task TryEventAsync(SealRecord record, string? licence, string type, string data, CancellationToken ct)
    {
        // Drift events are evidence, not state; a failed write must not break a check, and a repeat of the last event is not logged again.
        try
        {
            var last = await _seal.LastEventAsync(ct);
            if (last is not null && last.Type == type && last.DataHash == Base64Url.Encode(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(data)))) return;
            await _seal.UpdateAsync(record, r => r, licence, type, data, ct);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException) { }
    }

    private EvaluationSettings EvalSettings() => new(_settings.WarningDays, _settings.RestoreGrace);
}

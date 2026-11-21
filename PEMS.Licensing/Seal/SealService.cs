using System.Text.Json;
using PEMS.Licensing.Crypto;
using PEMS.Licensing.Formats;

namespace PEMS.Licensing.Seal;

public enum SealStatus
{
    /// <summary>No store holds a seal: first run or a new volume.</summary>
    NoSeal,
    Ok,
    /// <summary>One store lagged by one write (interrupted two-phase write) or was created late; repaired from the greater store.</summary>
    Repaired,
    /// <summary>Counters or chain heads disagree beyond a single write: DB restored from backup, file restored, or a store copied (design 4.4).</summary>
    RestoreSuspected,
    /// <summary>A store fails its signature, or the install key of this machine does not match the seal (image moved to other hardware).</summary>
    Invalid,
}

public sealed record SealCheck(SealStatus Status, SealRecord? Record, string? Licence, IReadOnlyList<string> Notes);

/// <summary>
/// Creates, reads, compares and writes the install seal in every store (design 4). Write order is the order of
/// <c>stores</c> (database first, then file); the check treats a store that lags by exactly one write as an
/// interrupted write and anything larger as a suspected restore.
/// </summary>
public sealed class SealService
{
    /// <summary>A store that is empty while another holds a seal with a counter up to this value is a first-run artefact, not a restore.</summary>
    public const int FirstRunGraceCounter = 3;

    private static readonly JsonSerializerOptions BundleJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly IReadOnlyList<ISealStore> _stores;
    private readonly IReadOnlyList<ILicenseEventSink> _sinks;
    private readonly IInstallKeyStore _keys;
    private readonly TimeProvider _time;

    public SealService(IEnumerable<ISealStore> stores, IEnumerable<ILicenseEventSink> sinks, IInstallKeyStore keys, TimeProvider time)
    {
        _stores = stores.ToList();
        _sinks = sinks.ToList();
        _keys = keys;
        _time = time;
    }

    public DateTimeOffset UtcNow => _time.GetUtcNow();

    public async Task<SealCheck> LoadAsync(CancellationToken ct = default)
    {
        var notes = new List<string>();
        var valid = new List<(ISealStore Store, SealBundle Bundle, SealRecord Record)>();
        var missing = new List<ISealStore>();
        var invalid = false;

        foreach (var store in _stores)
        {
            string? text;
            try { text = await store.ReadAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                notes.Add($"{store.Name}:unavailable");   // e.g. database not ready during first run: not a verdict either way
                continue;
            }
            if (text is null) { missing.Add(store); continue; }

            var (bundle, record) = Read(text);
            if (record is null) { invalid = true; notes.Add($"{store.Name}:invalid"); continue; }
            valid.Add((store, bundle!, record));
        }

        if (invalid) return new SealCheck(SealStatus.Invalid, null, null, notes);
        if (valid.Count == 0) return new SealCheck(SealStatus.NoSeal, null, null, notes);

        using (var local = _keys.Load())
        {
            if (local is null || valid.Any(v => v.Record.InstallPub != local.PublicKey))
            {
                notes.Add("install-key:mismatch");
                return new SealCheck(SealStatus.Invalid, valid.OrderByDescending(v => v.Record.Counter).First().Record, null, notes);
            }
        }

        var best = valid.OrderByDescending(v => v.Record.Counter).First();
        var status = SealStatus.Ok;
        foreach (var v in valid.Where(v => v.Store != best.Store))
        {
            var diff = best.Record.Counter - v.Record.Counter;
            if (diff == 0 && v.Record.AuditHead != best.Record.AuditHead) { status = SealStatus.RestoreSuspected; notes.Add($"{v.Store.Name}:chain-head-differs"); }
            else if (diff >= 2) { status = SealStatus.RestoreSuspected; notes.Add($"{v.Store.Name}:counter-behind-{diff}"); }
            else if (diff == 1 && status == SealStatus.Ok) { status = SealStatus.Repaired; notes.Add($"{v.Store.Name}:lagging"); }
        }
        foreach (var m in missing)
        {
            if (best.Record.Counter <= FirstRunGraceCounter) { if (status == SealStatus.Ok) status = SealStatus.Repaired; notes.Add($"{m.Name}:created-late"); }
            else { status = SealStatus.RestoreSuspected; notes.Add($"{m.Name}:missing"); }
        }

        if (status == SealStatus.Repaired)
            await WriteAllAsync(JsonSerializer.Serialize(best.Bundle, BundleJson), ct);

        return new SealCheck(status, best.Record, best.Bundle.Licence, notes);
    }

    /// <summary>Creates the install key, the first seal and the first event (design 4.1).</summary>
    public async Task<SealRecord> CreateAsync(Formats.FingerprintInfo fingerprint, CancellationToken ct = default)
    {
        using var signer = _keys.Create();
        var now = UtcNow;
        var installId = NewInstallId();
        var entry = await AppendAsync("seal_created", installId, now, ct);
        var record = new SealRecord(1, installId, now, signer.PublicKey, FileFormats.AlgInstall, signer.KeyProtection, 1, now,
            entry.EntryHash, null, null, fingerprint, "ActivationRequired", false, null, null, new List<PendingRequest>());
        await WriteAsync(record, null, signer, ct);
        return record;
    }

    /// <summary>Appends an event, applies <paramref name="change"/>, re-signs and writes every store.</summary>
    public async Task<SealRecord> UpdateAsync(SealRecord current, Func<SealRecord, SealRecord> change, string? licence, string eventType, string eventData, CancellationToken ct = default)
    {
        var now = UtcNow;
        var entry = await AppendAsync(eventType, eventData, now, ct);
        var next = change(current) with { AuditHead = entry.EntryHash };
        using var signer = _keys.Load() ?? throw new InvalidOperationException("The install key is missing.");
        await WriteAsync(next, licence, signer, ct);
        return next;
    }

    /// <summary>One application start or heartbeat: counter + 1 and a new last-seen time (design 4.4, 4.5).</summary>
    public Task<SealRecord> RecordStartAsync(SealRecord current, DateTimeOffset effectiveNow, string? licence, string eventType, CancellationToken ct = default)
        => UpdateAsync(current, r => r with { Counter = r.Counter + 1, LastSeenUtc = effectiveNow }, licence, eventType, $"counter={current.Counter + 1}", ct);

    public async Task<LicenseEventEntry?> LastEventAsync(CancellationToken ct = default)
        => _sinks.Count == 0 ? null : await _sinks[0].LastAsync(ct);

    private async Task<LicenseEventEntry> AppendAsync(string type, string data, DateTimeOffset now, CancellationToken ct)
    {
        var entry = EventChain.Next(await LastEventAsync(ct), now, type, data);
        for (var i = 0; i < _sinks.Count; i++)
        {
            try { await _sinks[i].AppendAsync(entry, ct); }
            catch (Exception) when (i > 0) { /* a mirror (database) may be unavailable; the first sink is authoritative */ }
        }
        return entry;
    }

    private async Task WriteAsync(SealRecord record, string? licence, IInstallSigner signer, CancellationToken ct)
    {
        var payload = LicenseJson.ToBytes(record);
        var envelope = SignedFileCodec.Create(FileFormats.Seal, payload, FileFormats.AlgInstall, "inst-" + record.InstallId, signer.Sign);
        await WriteAllAsync(JsonSerializer.Serialize(new SealBundle(envelope, licence), BundleJson), ct);
    }

    private async Task WriteAllAsync(string bundleJson, CancellationToken ct)
    {
        var written = 0;
        Exception? last = null;
        foreach (var store in _stores)
        {
            try { await store.WriteAsync(bundleJson, ct); written++; }
            catch (Exception ex) when (ex is not OperationCanceledException) { last = ex; }   // e.g. database not ready during first run
        }
        if (written == 0) throw new InvalidOperationException("The install seal could not be written to any store.", last);
    }

    private static (SealBundle? Bundle, SealRecord? Record) Read(string text)
    {
        try
        {
            var bundle = JsonSerializer.Deserialize<SealBundle>(text, BundleJson);
            if (bundle is null) return (null, null);
            var parsed = SignedFileCodec.Parse(bundle.Seal, FileFormats.Seal);
            if (!parsed.Ok) return (null, null);
            var record = LicenseJson.FromBytes<SealRecord>(parsed.Value!.PayloadBytes);
            if (record is null) return (null, null);
            return InstallKeyVerifier.Verify(record.InstallPub, parsed.Value.SignedMessage, parsed.Value.SignatureBytes)
                ? (bundle, record)
                : (null, null);
        }
        catch (JsonException) { return (null, null); }
    }

    /// <summary>128 random bits written as PEMS- plus Crockford base32 groups of five.</summary>
    public static string NewInstallId()
    {
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        var chars = new char[26];
        var bits = 0; var acc = 0; var o = 0;
        foreach (var b in bytes)
        {
            acc = (acc << 8) | b; bits += 8;
            while (bits >= 5 && o < 25) { chars[o++] = alphabet[(acc >> (bits - 5)) & 31]; bits -= 5; }
        }
        while (o < 25) chars[o++] = alphabet[(acc << (5 - bits)) & 31];
        var s = new string(chars, 0, 25);
        return "PEMS-" + string.Join("-", Enumerable.Range(0, 5).Select(i => s.Substring(i * 5, 5)));
    }
}

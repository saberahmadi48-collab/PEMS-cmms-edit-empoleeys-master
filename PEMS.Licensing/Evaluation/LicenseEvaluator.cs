using PEMS.Licensing.Fingerprint;
using PEMS.Licensing.Formats;
using PEMS.Licensing.Seal;

namespace PEMS.Licensing.Evaluation;

public sealed record EvaluationSettings(int WarningDays, TimeSpan RestoreGrace);

public sealed record EvaluationInput(
    LicensePayload? Licence,
    SealCheck Seal,
    FingerprintMatch? Fingerprint,
    ClockEvaluation Clock,
    string? FingerprintShort);

/// <summary>The one place that turns evidence into a state (design 7.2). Pure: no I/O, no clock, so every boundary is unit-testable.</summary>
public static class LicenseEvaluator
{
    public static LicenseStatus Evaluate(EvaluationInput input, EvaluationSettings settings)
    {
        var now = input.Clock.EffectiveNow;
        var seal = input.Seal.Record;
        var licence = input.Licence;
        var reasons = new List<string>();
        var state = LicenseState.Active;

        void Raise(LicenseState s, string reason)
        {
            reasons.Add(reason);
            if (s > state) state = s;
        }

        if (input.Seal.Status == SealStatus.Invalid) Raise(LicenseState.ActivationRequired, LicenseReasons.SealInvalid);
        else if (input.Seal.Status == SealStatus.NoSeal || licence is null) Raise(LicenseState.ActivationRequired, LicenseReasons.NoLicence);

        if (state != LicenseState.ActivationRequired && licence is not null)
        {
            if (input.Seal.Status == SealStatus.RestoreSuspected || seal?.RestoreSuspected == true)
            {
                var detected = seal?.RestoreDetectedUtc;
                if (detected.HasValue && now >= detected.Value + settings.RestoreGrace) Raise(LicenseState.Restricted, LicenseReasons.RestoreGraceExpired);
                else Raise(LicenseState.ActiveWarning, LicenseReasons.RestoreSuspected);
            }

            if (input.Fingerprint is { } fp)
            {
                switch (fp.Result)
                {
                    case FingerprintResult.Invalid: Raise(LicenseState.Restricted, LicenseReasons.FingerprintInvalid); break;
                    case FingerprintResult.Degraded: Raise(LicenseState.ActiveWarning, LicenseReasons.FingerprintDegraded); break;
                    case FingerprintResult.Tolerated: Raise(LicenseState.ActiveWarning, LicenseReasons.FingerprintDrift); break;
                }
            }

            if (LicenseTypes.IsTimeLimited(licence.Type))
            {
                if (now < licence.Validity.NotBefore) Raise(LicenseState.Restricted, LicenseReasons.NotYetValid);
                else if (licence.Validity.NotAfter is { } end)
                {
                    if (now >= end) Raise(LicenseState.Restricted, LicenseReasons.Expired);
                    else if ((end - now).TotalDays <= settings.WarningDays) Raise(LicenseState.ActiveWarning, LicenseReasons.Expiring);
                }
            }

            if (input.Clock.BackwardJump) Raise(LicenseState.ActiveWarning, LicenseReasons.ClockBackward);
        }

        int? daysLeft = null;
        DateTimeOffset? expires = licence is { Type: not LicenseTypes.Lifetime } ? licence.Validity.NotAfter : null;
        if (expires.HasValue) daysLeft = (int)Math.Ceiling((expires.Value - now).TotalDays);

        return new LicenseStatus(state, reasons, licence?.LicenceId, licence?.Type, licence?.Grant.Edition, expires, daysLeft,
            licence?.Maintenance.UpdatesUntil, seal?.InstallId, input.FingerprintShort);
    }
}

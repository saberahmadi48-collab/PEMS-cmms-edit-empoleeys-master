using PEMS.Licensing.Formats;

namespace PEMS.Licensing.Fingerprint;

public enum FingerprintResult { Match, Tolerated, Degraded, Invalid }

public sealed record FingerprintMatch(FingerprintResult Result, IReadOnlyList<string> ChangedSignals, double MatchedWeightRatio);

/// <summary>
/// Tolerance rule of design 3.2. The baseline is the licensed signal list, never the last start, so replacing
/// one component at a time cannot walk a licence to another machine.
/// </summary>
public static class FingerprintComparer
{
    public const double MinWeightRatio = 0.60;
    public const int MaxChangedTolerated = 1;
    public const int MaxChangedDegraded = 2;

    public static FingerprintMatch Compare(FingerprintInfo licensed, FingerprintInfo current)
    {
        var now = current.Signals.ToDictionary(s => s.Id, s => s.H, StringComparer.Ordinal);
        var changed = new List<string>();
        var matched = new List<string>();
        foreach (var s in licensed.Signals)
        {
            if (now.TryGetValue(s.Id, out var h) && h == s.H) matched.Add(s.Id);
            else changed.Add(s.Id);   // missing now counts as changed (design 3.2)
        }

        var total = licensed.Signals.Sum(s => SignalCatalog.Spec(s.Id).Weight);
        var matchedWeight = matched.Sum(id => SignalCatalog.Spec(id).Weight);
        var ratio = total == 0 ? 0 : (double)matchedWeight / total;

        if (changed.Count == 0)
            return new FingerprintMatch(FingerprintResult.Match, changed, ratio);

        // At least one S1 signal must still match, if any S1 signal was available at enrolment
        // (VMs and containers may have none; they then rely on weight and count only).
        var licensedHadS1 = licensed.Signals.Any(s => SignalCatalog.Spec(s.Id).Class == SignalClass.S1);
        var s1Matches = matched.Any(id => SignalCatalog.Spec(id).Class == SignalClass.S1);
        var anchorOk = !licensedHadS1 || s1Matches;

        if (anchorOk && ratio >= MinWeightRatio)
        {
            if (changed.Count <= MaxChangedTolerated) return new FingerprintMatch(FingerprintResult.Tolerated, changed, ratio);
            if (changed.Count <= MaxChangedDegraded) return new FingerprintMatch(FingerprintResult.Degraded, changed, ratio);
        }
        return new FingerprintMatch(FingerprintResult.Invalid, changed, ratio);
    }
}

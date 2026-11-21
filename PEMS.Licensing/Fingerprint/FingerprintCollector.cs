using PEMS.Licensing.Formats;

namespace PEMS.Licensing.Fingerprint;

public sealed record RawSignal(string Id, string? Value);

/// <summary>Platform-specific reading of raw signals. Implementations never log or expose raw values.</summary>
public interface ISignalSource
{
    string Platform { get; }
    string Virtualization { get; }
    IReadOnlyList<RawSignal> Collect();
}

public static class FingerprintCollector
{
    public static FingerprintInfo Collect(ISignalSource source)
    {
        var signals = new List<SignalEntry>();
        foreach (var raw in source.Collect())
        {
            var normalized = SignalHasher.Normalize(raw.Value);
            if (normalized is null) continue;
            signals.Add(new SignalEntry(raw.Id, SignalHasher.Hash(raw.Id, normalized)));
        }
        signals = signals.OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
        return new FingerprintInfo(SignalCatalog.Scheme, source.Platform, source.Virtualization,
            SignalHasher.FingerprintHash(signals), signals);
    }

    /// <summary>The short form shown on the licence page so a customer can read it to support.</summary>
    public static string ShortForm(string fpHash) => fpHash.Length <= 8 ? fpHash : fpHash[..8];
}

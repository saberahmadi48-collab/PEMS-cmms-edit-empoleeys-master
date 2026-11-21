namespace PEMS.Licensing.Formats;

/// <summary>RFC 4648 base64url without padding, the encoding of every binary value in licensing files.</summary>
public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Convert.FromBase64String(s);
    }

    public static bool TryDecode(string? text, out byte[] data)
    {
        data = Array.Empty<byte>();
        if (string.IsNullOrEmpty(text)) return false;
        try { data = Decode(text); return true; }
        catch (FormatException) { return false; }
    }
}

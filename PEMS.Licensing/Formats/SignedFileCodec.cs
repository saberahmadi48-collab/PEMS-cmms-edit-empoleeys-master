using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PEMS.Licensing.Formats;

public sealed record SignatureBlock(string Alg, string KeyId, string Value);

/// <summary>On-disk envelope of every signed licensing file: payload bytes are base64url, signature covers prefix + payload bytes.</summary>
public sealed record SignedFile(string Format, int FormatVersion, string Payload, SignatureBlock Signature);

public static class FileFormats
{
    public const string Request = "pemsreq";
    public const string Licence = "pemslic";
    public const string Seal = "pemsseal";

    public const int CurrentVersion = 1;

    public const string AlgInstall = "ES256";
    public const string AlgVendor = "EdDSA";

    public const int MaxRequestBytes = 32 * 1024;
    public const int MaxLicenceBytes = 32 * 1024;
    public const int MaxSealBytes = 256 * 1024;

    /// <summary>Domain separation: a signature made for one file type can never validate as another (design 5.1).</summary>
    public static string Prefix(string format) => format switch
    {
        Request => "PEMS-REQ-V1\n",
        Licence => "PEMS-LIC-V1\n",
        Seal => "PEMS-SEAL-V1\n",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown licensing file format."),
    };

    public static int MaxBytes(string format) => format switch
    {
        Request => MaxRequestBytes,
        Licence => MaxLicenceBytes,
        _ => MaxSealBytes,
    };
}

/// <summary>A file that passed the structural checks but whose signature is not yet verified.</summary>
public sealed record ParsedFile(SignedFile Envelope, byte[] PayloadBytes, byte[] SignedMessage, byte[] SignatureBytes);

public static class SignedFileCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public static byte[] SignedMessage(string format, byte[] payloadBytes)
    {
        var prefix = Encoding.UTF8.GetBytes(FileFormats.Prefix(format));
        var message = new byte[prefix.Length + payloadBytes.Length];
        prefix.CopyTo(message, 0);
        payloadBytes.CopyTo(message, prefix.Length);
        return message;
    }

    /// <summary>Builds the file text. <paramref name="sign"/> receives the message to sign (prefix + payload bytes).</summary>
    public static string Create(string format, byte[] payloadBytes, string alg, string keyId, Func<byte[], byte[]> sign)
    {
        var signature = sign(SignedMessage(format, payloadBytes));
        var file = new SignedFile(format, FileFormats.CurrentVersion, Base64Url.Encode(payloadBytes),
            new SignatureBlock(alg, keyId, Base64Url.Encode(signature)));
        return JsonSerializer.Serialize(file, Options);
    }

    /// <summary>Structure checks only (size, format, version, encoding). The signature is verified by the caller with the right key.</summary>
    public static LicenseResult<ParsedFile> Parse(string? text, string expectedFormat)
    {
        if (string.IsNullOrWhiteSpace(text))
            return LicenseResult<ParsedFile>.Fail(LicenseFailure.BadEncoding, "empty file");
        if (Encoding.UTF8.GetByteCount(text) > FileFormats.MaxBytes(expectedFormat))
            return LicenseResult<ParsedFile>.Fail(LicenseFailure.TooLarge);

        SignedFile? file;
        try { file = JsonSerializer.Deserialize<SignedFile>(text, Options); }
        catch (JsonException) { return LicenseResult<ParsedFile>.Fail(LicenseFailure.BadEncoding, "not json"); }
        if (file?.Signature is null || file.Payload is null)
            return LicenseResult<ParsedFile>.Fail(LicenseFailure.BadEncoding, "missing fields");

        if (!string.Equals(file.Format, expectedFormat, StringComparison.Ordinal))
            return LicenseResult<ParsedFile>.Fail(LicenseFailure.UnknownFormat, file.Format);
        if (file.FormatVersion != FileFormats.CurrentVersion)
            return LicenseResult<ParsedFile>.Fail(LicenseFailure.UnknownVersion, file.FormatVersion.ToString());
        if (!Base64Url.TryDecode(file.Payload, out var payload))
            return LicenseResult<ParsedFile>.Fail(LicenseFailure.BadEncoding, "payload");
        if (!Base64Url.TryDecode(file.Signature.Value, out var signature))
            return LicenseResult<ParsedFile>.Fail(LicenseFailure.BadEncoding, "signature");

        return LicenseResult<ParsedFile>.Success(new ParsedFile(file, payload, SignedMessage(expectedFormat, payload), signature));
    }
}

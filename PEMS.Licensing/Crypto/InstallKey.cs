using System.Security.Cryptography;
using PEMS.Licensing.Formats;

namespace PEMS.Licensing.Crypto;

/// <summary>The install key pair (ECDSA P-256, ES256). The private half is held by the platform where it can be non-exportable.</summary>
public interface IInstallSigner : IDisposable
{
    /// <summary>SubjectPublicKeyInfo, base64url.</summary>
    string PublicKey { get; }
    /// <summary><c>tpm</c>, <c>cng-machine</c> or <c>software</c> (design 4.3): what really protects the key.</summary>
    string KeyProtection { get; }
    byte[] Sign(byte[] message);
}

public interface IInstallKeyStore
{
    IInstallSigner? Load();
    IInstallSigner Create();
}

public static class InstallKeyVerifier
{
    public static bool Verify(string publicKeySpkiBase64Url, byte[] message, byte[] signature)
    {
        if (!Base64Url.TryDecode(publicKeySpkiBase64Url, out var spki)) return false;
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(spki, out _);
            return ecdsa.VerifyData(message, signature, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException) { return false; }
        catch (ArgumentException) { return false; }
    }

    public static string PublicKeyHash(string publicKeySpkiBase64Url)
        => Base64Url.Encode(SHA256.HashData(Base64Url.Decode(publicKeySpkiBase64Url)));
}

/// <summary>Software key kept in a file of the seal directory (mode 0600 on Unix). Casual-copy protection only; used on Linux and Docker without a TPM.</summary>
public sealed class FileInstallKeyStore : IInstallKeyStore
{
    private readonly string _path;

    public FileInstallKeyStore(string sealDirectory) => _path = Path.Combine(sealDirectory, "install.key");

    public IInstallSigner? Load()
    {
        if (!File.Exists(_path)) return null;
        var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(File.ReadAllBytes(_path), out _);
        return new EcdsaSigner(ecdsa, "software");
    }

    public IInstallSigner Create()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllBytes(_path, ecdsa.ExportPkcs8PrivateKey());
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return new EcdsaSigner(ecdsa, "software");
    }
}

/// <summary>Windows machine-scope CNG key with the non-exportable flag. It stops ordinary export tools; it does not bind to hardware without a TPM (design 4.3).</summary>
public sealed class CngInstallKeyStore : IInstallKeyStore
{
    private const string KeyName = "PEMS-Install-Key";

    public IInstallSigner? Load()
    {
        if (!OperatingSystem.IsWindows() || !CngKey.Exists(KeyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.MachineKey))
            return null;
        return new EcdsaSigner(new ECDsaCng(CngKey.Open(KeyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.MachineKey)), "cng-machine");
    }

    public IInstallSigner Create()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var parameters = new CngKeyCreationParameters
        {
            Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
            KeyCreationOptions = CngKeyCreationOptions.MachineKey | CngKeyCreationOptions.OverwriteExistingKey,
            ExportPolicy = CngExportPolicies.None,
        };
        return new EcdsaSigner(new ECDsaCng(CngKey.Create(CngAlgorithm.ECDsaP256, KeyName, parameters)), "cng-machine");
    }
}

internal sealed class EcdsaSigner : IInstallSigner
{
    private readonly ECDsa _ecdsa;

    public EcdsaSigner(ECDsa ecdsa, string keyProtection)
    {
        _ecdsa = ecdsa;
        KeyProtection = keyProtection;
        PublicKey = Base64Url.Encode(ecdsa.ExportSubjectPublicKeyInfo());
    }

    public string PublicKey { get; }
    public string KeyProtection { get; }
    public byte[] Sign(byte[] message) => _ecdsa.SignData(message, HashAlgorithmName.SHA256);
    public void Dispose() => _ecdsa.Dispose();
}

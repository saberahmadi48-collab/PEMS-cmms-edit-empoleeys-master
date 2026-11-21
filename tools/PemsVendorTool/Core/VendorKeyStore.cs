using System.Security.Cryptography;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using PEMS.Licensing.Crypto;
using PEMS.Licensing.Formats;

namespace PemsVendorTool.Core;

public sealed record VendorKeyInfo(string KeyId, string PublicKey, DateTimeOffset CreatedUtc);

/// <summary>
/// Offline vendor signing keys (design 9.1, rule 10). The private key is stored only encrypted with a passphrase
/// (PBKDF2-HMAC-SHA256, AES-256-GCM) on the owner computer; it is decrypted into memory per signing session and never exported.
/// The key procedure itself (custody, backup, rotation, compromise plan) is plan row #107.
/// </summary>
public sealed class VendorKeyStore
{
    public const int Pbkdf2Iterations = 600_000;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true };

    private readonly string _directory;

    public VendorKeyStore(string directory) => _directory = directory;

    public IReadOnlyList<VendorKeyInfo> List()
        => !Directory.Exists(_directory)
            ? Array.Empty<VendorKeyInfo>()
            : Directory.GetFiles(_directory, "vendor-key-*.json")
                .Select(f => JsonSerializer.Deserialize<KeyFile>(File.ReadAllText(f), Json)!)
                .Select(k => new VendorKeyInfo(k.KeyId, k.PublicKey, k.CreatedUtc))
                .OrderBy(k => k.KeyId, StringComparer.Ordinal).ToList();

    public VendorKeyInfo Generate(string keyId, string passphrase, DateTimeOffset now)
    {
        if (passphrase.Length < 12) throw new ArgumentException("The passphrase must be at least 12 characters.", nameof(passphrase));
        if (keyId.Any(c => !(char.IsLetterOrDigit(c) || c == '-'))) throw new ArgumentException("Key id may contain letters, digits and '-'.", nameof(keyId));
        var path = PathOf(keyId);
        if (File.Exists(path)) throw new InvalidOperationException("A key with this id already exists.");

        var priv = new Ed25519PrivateKeyParameters(new SecureRandom());
        var privateKey = priv.GetEncoded();
        var publicKey = Base64Url.Encode(priv.GeneratePublicKey().GetEncoded());

        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[privateKey.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(Derive(passphrase, salt), 16))
            aes.Encrypt(nonce, privateKey, cipher, tag, System.Text.Encoding.UTF8.GetBytes(keyId));
        CryptographicOperations.ZeroMemory(privateKey);

        Directory.CreateDirectory(_directory);
        var file = new KeyFile(keyId, publicKey, now, Pbkdf2Iterations, Base64Url.Encode(salt), Base64Url.Encode(nonce), Base64Url.Encode(cipher), Base64Url.Encode(tag));
        File.WriteAllText(path, JsonSerializer.Serialize(file, Json));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return new VendorKeyInfo(keyId, publicKey, now);
    }

    /// <summary>Decrypts the private key for one signing session. Throws <see cref="CryptographicException"/> on a wrong passphrase.</summary>
    public VendorSigner Unlock(string keyId, string passphrase)
    {
        var path = PathOf(keyId);
        if (!File.Exists(path)) throw new FileNotFoundException("Unknown key id.", keyId);
        var k = JsonSerializer.Deserialize<KeyFile>(File.ReadAllText(path), Json)!;
        var cipher = Base64Url.Decode(k.Cipher);
        var plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(Derive(passphrase, Base64Url.Decode(k.Salt), k.Iterations), 16);
            aes.Decrypt(Base64Url.Decode(k.Nonce), cipher, Base64Url.Decode(k.Tag), plain, System.Text.Encoding.UTF8.GetBytes(keyId));
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new CryptographicException("Wrong passphrase or damaged key file.");
        }
        return new VendorSigner(keyId, k.PublicKey, plain);
    }

    /// <summary>The trusted-keys.json snippet to commit (public keys only) so the product can verify (plan row #107).</summary>
    public string ExportTrustedKeys()
        => JsonSerializer.Serialize(new { keys = List().Select(k => new { key_id = k.KeyId, public_key = k.PublicKey }) }, Json);

    public TrustedKeySet ToTrustedKeySet() => new(List().Select(k => new TrustedKey(k.KeyId, k.PublicKey)));

    private string PathOf(string keyId) => Path.Combine(_directory, $"vendor-key-{keyId}.json");

    private static byte[] Derive(string passphrase, byte[] salt, int iterations = Pbkdf2Iterations)
        => Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, iterations, HashAlgorithmName.SHA256, 32);

    private sealed record KeyFile(string KeyId, string PublicKey, DateTimeOffset CreatedUtc, int Iterations, string Salt, string Nonce, string Cipher, string Tag);
}

/// <summary>An unlocked signing key held in memory; disposing wipes the private bytes.</summary>
public sealed class VendorSigner : IDisposable
{
    private readonly byte[] _privateKey;

    public VendorSigner(string keyId, string publicKey, byte[] privateKey)
    {
        KeyId = keyId;
        PublicKey = publicKey;
        _privateKey = privateKey;
    }

    public string KeyId { get; }
    public string PublicKey { get; }

    public byte[] Sign(byte[] message)
    {
        var signer = new Ed25519Signer();
        signer.Init(true, new Ed25519PrivateKeyParameters(_privateKey, 0));
        signer.BlockUpdate(message, 0, message.Length);
        return signer.GenerateSignature();
    }

    public void Dispose() => CryptographicOperations.ZeroMemory(_privateKey);
}

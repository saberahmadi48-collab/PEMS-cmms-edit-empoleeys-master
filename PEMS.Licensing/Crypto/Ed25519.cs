using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace PEMS.Licensing.Crypto;

/// <summary>
/// Ed25519 (PureEdDSA) verification over raw 32-byte keys. Signing and key generation are deliberately NOT here:
/// the customer product contains no private-key code path (design section 2); they live in the vendor tool.
/// </summary>
public static class Ed25519
{
    public const int KeyLength = 32;
    public const int SignatureLength = 64;

    public static bool Verify(byte[] publicKey, byte[] message, byte[] signature)
    {
        if (publicKey.Length != KeyLength || signature.Length != SignatureLength) return false;
        try
        {
            var signer = new Ed25519Signer();
            signer.Init(false, new Ed25519PublicKeyParameters(publicKey, 0));
            signer.BlockUpdate(message, 0, message.Length);
            return signer.VerifySignature(signature);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }
}

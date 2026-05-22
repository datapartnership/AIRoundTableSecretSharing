using System.Security.Cryptography;

namespace AIRoundTableSecretSharingCommon.Core;

/// <summary>
/// ML-KEM-768 (FIPS 203) key encapsulation mechanism.
/// Replaces ECDH with a post-quantum secure primitive.
///
/// Convention: the alphabetically LARGER partner encapsulates for the smaller one.
///   Larger calls Encapsulate(smallerPartner.PublicKey) → (ciphertext, sharedSecret)
///   Smaller calls Decapsulate(ciphertext) → sharedSecret
/// Both ends derive the same 32-byte shared secret; the aggregator never sees it.
/// </summary>
public class MlKemKeyExchange : IDisposable
{
    private readonly MLKem _kem;

    public MlKemKeyExchange()
    {
        _kem = MLKem.GenerateKey(MLKemAlgorithm.MLKem768);
    }

    public byte[] GetPublicKey() => _kem.ExportEncapsulationKey();

    public string GetPublicKeyBase64() => Convert.ToBase64String(GetPublicKey());

    /// <summary>
    /// Called by the alphabetically LARGER partner.
    /// Generates a fresh (ciphertext, sharedSecret) pair using the smaller partner's public key.
    /// POST the ciphertext to the aggregator; keep sharedSecret locally.
    /// </summary>
    public (byte[] ciphertext, byte[] sharedSecret) Encapsulate(byte[] partnerPublicKey)
    {
        using var partnerKem = MLKem.ImportEncapsulationKey(MLKemAlgorithm.MLKem768, partnerPublicKey);
        partnerKem.Encapsulate(out byte[] ciphertext, out byte[] sharedSecret);
        return (ciphertext, sharedSecret);
    }

    /// <summary>
    /// Called by the alphabetically SMALLER partner after fetching the ciphertext from the aggregator.
    /// Recovers the 32-byte shared secret established by the larger partner.
    /// </summary>
    public byte[] Decapsulate(byte[] ciphertext) => _kem.Decapsulate(ciphertext);

    public void Dispose() => _kem?.Dispose();
}

using System.Security.Cryptography;

namespace AIRoundTableSecretSharingCommon.Core;

/// <summary>
/// Elliptic Curve Diffie-Hellman key exchange implementation.
/// Uses NIST P-256 curve for key agreement.
/// </summary>
public class DiffieHellmanKeyExchange
{
    private readonly ECDiffieHellman _ecdh;
    private readonly byte[] _publicKey;
    
    public DiffieHellmanKeyExchange()
    {
        // Create ECDH with NIST P-256 curve
        _ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        _publicKey = _ecdh.PublicKey.ExportSubjectPublicKeyInfo();
    }
    
    /// <summary>
    /// Get the public key to share with other partners (via the aggregator).
    /// This is safe to share publicly.
    /// </summary>
    public byte[] GetPublicKey() => _publicKey;
    
    /// <summary>
    /// Get the public key as a Base64 string for easy transmission.
    /// </summary>
    public string GetPublicKeyBase64() => Convert.ToBase64String(_publicKey);
    
    /// <summary>
    /// Compute a shared secret with another partner using their public key.
    /// The result is the same whether A computes with B's key or B computes with A's key.
    /// </summary>
    public byte[] ComputeSharedSecret(byte[] otherPublicKey)
    {
        using var otherEcdh = ECDiffieHellman.Create();
        otherEcdh.ImportSubjectPublicKeyInfo(otherPublicKey, out _);
        
        // Derive a 256-bit shared secret
        return _ecdh.DeriveKeyMaterial(otherEcdh.PublicKey);
    }
    
    /// <summary>
    /// Compute a shared secret from a Base64-encoded public key.
    /// </summary>
    public byte[] ComputeSharedSecret(string otherPublicKeyBase64)
    {
        var otherPublicKey = Convert.FromBase64String(otherPublicKeyBase64);
        return ComputeSharedSecret(otherPublicKey);
    }
    
    public void Dispose()
    {
        _ecdh?.Dispose();
    }
}

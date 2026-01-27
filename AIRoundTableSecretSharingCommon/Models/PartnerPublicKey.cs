namespace AIRoundTableSecretSharingCommon.Models;

/// <summary>
/// Represents a partner's public key for Diffie-Hellman key exchange.
/// The private key is NEVER transmitted - it stays with the partner.
/// </summary>
public class PartnerPublicKey
{
    /// <summary>
    /// The partner's unique identifier.
    /// </summary>
    public string ProducerId { get; set; } = string.Empty;
    
    /// <summary>
    /// The partner's ECDH public key encoded as Base64.
    /// This is safe to share - it cannot be used to derive the private key.
    /// </summary>
    public string PublicKeyBase64 { get; set; } = string.Empty;
    
    /// <summary>
    /// When this key was registered.
    /// </summary>
    public DateTime RegisteredAt { get; set; }
}

/// <summary>
/// Request to register a partner's public key.
/// </summary>
public class RegisterKeyRequest
{
    public string ProducerId { get; set; } = string.Empty;
    public string PublicKeyBase64 { get; set; } = string.Empty;
}

/// <summary>
/// Response containing all partners' public keys for key exchange.
/// </summary>
public class KeyExchangeResponse
{
    public List<PartnerPublicKey> PartnerKeys { get; set; } = new();
    public int TotalPartners { get; set; }
}

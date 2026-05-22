namespace AIRoundTableSecretSharingCommon.Models;

/// <summary>
/// Represents a partner's ML-KEM encapsulation (public) key.
/// The decapsulation (private) key is NEVER transmitted — it stays with the partner.
/// </summary>
public class PartnerPublicKey
{
    public string ProducerId { get; set; } = string.Empty;

    /// <summary>
    /// The partner's ML-KEM-768 encapsulation key encoded as Base64 (1184 bytes).
    /// Safe to share publicly.
    /// </summary>
    public string PublicKeyBase64 { get; set; } = string.Empty;

    public DateTime RegisteredAt { get; set; }
}

/// <summary>
/// Request to register a partner's ML-KEM encapsulation key.
/// </summary>
public class RegisterKeyRequest
{
    public string ProducerId { get; set; } = string.Empty;
    public string PublicKeyBase64 { get; set; } = string.Empty;
}

/// <summary>
/// Response containing all partners' ML-KEM encapsulation keys.
/// </summary>
public class KeyExchangeResponse
{
    public List<PartnerPublicKey> PartnerKeys { get; set; } = new();
    public int TotalPartners { get; set; }
}

/// <summary>
/// A KEM ciphertext sent from one partner to another via the aggregator.
/// The sender encapsulated using the recipient's public key.
/// Only the recipient (who holds the decapsulation key) can recover the shared secret.
/// </summary>
public class PartnerCiphertext
{
    public string SenderId { get; set; } = string.Empty;
    public string RecipientId { get; set; } = string.Empty;

    /// <summary>
    /// ML-KEM-768 ciphertext encoded as Base64 (1088 bytes).
    /// </summary>
    public string CiphertextBase64 { get; set; } = string.Empty;

    public DateTime StoredAt { get; set; }
}

/// <summary>
/// Request to store a KEM ciphertext in the aggregator.
/// </summary>
public class StoreCiphertextRequest
{
    public string SenderId { get; set; } = string.Empty;
    public string RecipientId { get; set; } = string.Empty;
    public string CiphertextBase64 { get; set; } = string.Empty;
}

/// <summary>
/// Response containing all ciphertexts addressed to a given partner.
/// </summary>
public class CiphertextResponse
{
    public List<PartnerCiphertext> Ciphertexts { get; set; } = new();
}

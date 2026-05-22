using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingAPI.Services;

/// <summary>
/// Stores ML-KEM ciphertexts posted by encapsulating partners.
///
/// SECURITY NOTE: The aggregator only stores opaque ciphertexts.
/// Only the intended recipient (who holds the decapsulation key) can recover the shared secret.
/// </summary>
public class CiphertextStore
{
    // Key: (senderId, recipientId)
    private readonly Dictionary<(string, string), PartnerCiphertext> _ciphertexts = new();
    private readonly object _lock = new();

    public void Store(PartnerCiphertext ciphertext)
    {
        lock (_lock)
        {
            _ciphertexts[(ciphertext.SenderId, ciphertext.RecipientId)] = ciphertext;
        }
    }

    public List<PartnerCiphertext> GetForRecipient(string recipientId)
    {
        lock (_lock)
        {
            return _ciphertexts.Values
                .Where(c => c.RecipientId == recipientId)
                .ToList();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _ciphertexts.Clear();
        }
    }
}

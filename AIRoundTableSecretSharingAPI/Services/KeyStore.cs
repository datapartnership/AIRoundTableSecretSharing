using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingAPI.Services;

/// <summary>
/// Stores partner public keys for Diffie-Hellman key exchange.
/// 
/// SECURITY NOTE: This only stores PUBLIC keys, which are safe to share.
/// Private keys never leave the partners' systems.
/// </summary>
public class KeyStore
{
    private readonly Dictionary<string, PartnerPublicKey> _keys = new();
    private readonly object _lock = new();
    
    public void RegisterKey(PartnerPublicKey key)
    {
        lock (_lock)
        {
            _keys[key.ProducerId] = key;
        }
    }
    
    public PartnerPublicKey? GetKey(string producerId)
    {
        lock (_lock)
        {
            return _keys.TryGetValue(producerId, out var key) ? key : null;
        }
    }
    
    public List<PartnerPublicKey> GetAllKeys()
    {
        lock (_lock)
        {
            return _keys.Values.ToList();
        }
    }
    
    public void Clear()
    {
        lock (_lock)
        {
            _keys.Clear();
        }
    }
}

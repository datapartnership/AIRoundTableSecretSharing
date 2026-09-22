using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingAPI.Repositories;

public interface IKeyRepository
{
    Task RegisterKeyAsync(PartnerPublicKey key);
    /// <summary>
    /// Removes every key the producer registered in the epoch (on any device) and stores the new one.
    /// </summary>
    Task ReplaceProducerKeyAsync(PartnerPublicKey key);
    Task<PartnerPublicKey?> GetKeyAsync(int epochId, string producerId, string deviceId);
    Task<List<PartnerPublicKey>> GetAllKeysAsync(int epochId);
    Task ClearAsync(int? epochId = null);
}

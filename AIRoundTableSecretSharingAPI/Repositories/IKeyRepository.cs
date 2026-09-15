using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingAPI.Repositories;

public interface IKeyRepository
{
    Task RegisterKeyAsync(PartnerPublicKey key);
    Task<PartnerPublicKey?> GetKeyAsync(int epochId, string producerId, string deviceId);
    Task<List<PartnerPublicKey>> GetAllKeysAsync(int epochId);
    Task ClearAsync(int? epochId = null);
}

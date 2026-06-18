using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingAPI.Repositories;

public interface IKeyRepository
{
    Task RegisterKeyAsync(PartnerPublicKey key);
    Task<PartnerPublicKey?> GetKeyAsync(string producerId);
    Task<List<PartnerPublicKey>> GetAllKeysAsync();
    Task ClearAsync();
}

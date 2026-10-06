using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingAPI.Repositories;

public interface IQuorumRepository
{
    Task<List<QuorumResponse>> GetByEpochAsync(int epochId);
    Task<List<QuorumResponse>> GetByProducerAsync(int epochId, string producerId);
    Task AddAsync(IReadOnlyList<QuorumResponse> responses);
    Task ClearAllAsync();
}

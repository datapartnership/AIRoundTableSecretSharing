using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingAPI.Repositories;

public interface IProducerRepository
{
    Task<List<ProducerInfo>> GetActiveProducersAsync(DateTime effectiveDate);
    Task<ProducerEpoch?> GetEpochForDateAsync(DateTime date);
    Task<ProducerEpoch?> GetEpochByIdAsync(int epochId);
    Task<List<ProducerEpoch>> GetAllEpochsAsync();
    Task AddProducerAsync(ProducerInfo producer);
    Task UpsertProducerAsync(ProducerInfo producer);
    Task<List<ProducerInfo>> GetProducersByIdsAsync(IReadOnlyCollection<string> ids);
    Task CreateEpochAsync(ProducerEpoch epoch);
    Task AddEpochAsync(ProducerEpoch epoch);
    Task CloseEpochAsync(int epochId);

    Task ClearAllAsync();
}

using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingAPI.Repositories;

public interface IProducerRepository
{
    Task<List<ProducerInfo>> GetActiveProducersAsync(DateTime effectiveDate);
    Task<ProducerEpoch?> GetEpochForDateAsync(DateTime date);
    Task AddProducerAsync(ProducerInfo producer);
    Task CreateEpochAsync(ProducerEpoch epoch);
    Task AddEpochAsync(ProducerEpoch epoch);

    Task ClearAllAsync();
}

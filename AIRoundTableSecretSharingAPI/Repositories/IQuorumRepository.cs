using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingAPI.Repositories;

public interface IQuorumRepository
{
    Task<QuorumPeriod?> GetPeriodAsync(int periodId);
    Task<List<QuorumPeriod>> GetAllPeriodsAsync();
    /// <summary>The newest period that is neither cancelled nor marked complete, of any usage type.</summary>
    Task<QuorumPeriod?> GetActivePeriodAsync();
    Task AddPeriodAsync(QuorumPeriod period);
    Task MarkCompleteAsync(int periodId);
    Task CancelPeriodAsync(int periodId, string? reason);

    Task<List<QuorumAnswer>> GetAnswersAsync(int periodId);
    Task AddAnswersAsync(IReadOnlyList<QuorumAnswer> answers);
    Task ClearAllAsync();
}

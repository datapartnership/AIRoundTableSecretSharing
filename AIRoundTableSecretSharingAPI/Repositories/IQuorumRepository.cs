using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingAPI.Repositories;

public interface IQuorumRepository
{
    Task<QuorumPeriod?> GetPeriodAsync(int periodId);
    Task<List<QuorumPeriod>> GetAllPeriodsAsync();
    /// <summary>The newest non-cancelled period of the usage type that covers the date.</summary>
    Task<QuorumPeriod?> GetPeriodForDateAsync(string usageType, DateTime date);
    Task AddPeriodAsync(QuorumPeriod period);
    Task MarkCompleteAsync(int periodId);
    Task CancelPeriodAsync(int periodId, string? reason);

    Task<List<QuorumAnswer>> GetAnswersAsync(int periodId);
    Task AddAnswersAsync(IReadOnlyList<QuorumAnswer> answers);
    Task ClearAllAsync();
}

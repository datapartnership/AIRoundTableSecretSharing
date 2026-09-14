using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingAPI.Repositories;

public interface ISubmissionRepository
{
    /// <summary>
    /// Stores a submission. Returns false if an identical submission already exists.
    /// </summary>
    Task<bool> AddSubmissionAsync(MetricSubmission submission);

    /// <summary>
    /// Stores new submissions in a transaction. Caller must already exclude duplicates.
    /// </summary>
    Task AddSubmissionsAsync(IReadOnlyList<MetricSubmission> submissions);

    Task<List<MetricSubmission>> GetSubmissionsAsync(
        string country, string month, string indicator, string segment, int epochId);

    Task<List<MetricSubmission>> GetSubmissionsByProducerAsync(string producerId, int epochId);

    Task<List<MetricSubmission>> GetSubmissionsByEpochAsync(int epochId);

    Task ClearAllAsync();
}

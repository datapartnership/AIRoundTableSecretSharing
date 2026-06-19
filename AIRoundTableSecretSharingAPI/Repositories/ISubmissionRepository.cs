using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingAPI.Repositories;

public interface ISubmissionRepository
{
    /// <summary>
    /// Stores a submission. Returns false if an identical submission already exists.
    /// </summary>
    Task<bool> AddSubmissionAsync(MetricSubmission submission);

    Task<List<MetricSubmission>> GetSubmissionsAsync(string country, string month, int epochId);

    Task<List<MetricSubmission>> GetSubmissionsByProducerAsync(string producerId, int epochId);

    Task ClearAllAsync();
}

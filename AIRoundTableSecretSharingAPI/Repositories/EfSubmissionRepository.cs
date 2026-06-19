using AIRoundTableSecretSharingAPI.Data;
using AIRoundTableSecretSharingCommon.Models;
using Microsoft.EntityFrameworkCore;

namespace AIRoundTableSecretSharingAPI.Repositories;

public class EfSubmissionRepository : ISubmissionRepository
{
    private readonly AppDbContext _db;

    public EfSubmissionRepository(AppDbContext db) => _db = db;

    public async Task<bool> AddSubmissionAsync(MetricSubmission submission)
    {
        var exists = await _db.Submissions.AnyAsync(s =>
            s.ProducerId == submission.ProducerId &&
            s.Country == submission.Country &&
            s.Month == submission.Month &&
            s.EpochId == submission.EpochId);

        if (exists)
            return false;

        _db.Submissions.Add(submission);
        await _db.SaveChangesAsync();
        return true;
    }

    public Task<List<MetricSubmission>> GetSubmissionsAsync(string country, string month, int epochId) =>
        _db.Submissions
            .Where(s => s.Country == country && s.Month == month && s.EpochId == epochId)
            .ToListAsync();

    public Task<List<MetricSubmission>> GetSubmissionsByProducerAsync(string producerId, int epochId) =>
        _db.Submissions
            .Where(s => s.ProducerId == producerId && s.EpochId == epochId)
            .ToListAsync();

    public async Task ClearAllAsync()
    {
        _db.Submissions.RemoveRange(_db.Submissions);
        await _db.SaveChangesAsync();
    }
}

using AIRoundTableSecretSharingAPI.Data;
using AIRoundTableSecretSharingCommon.Models;
using Microsoft.EntityFrameworkCore;

namespace AIRoundTableSecretSharingAPI.Repositories;

public class EfQuorumRepository : IQuorumRepository
{
    private readonly AppDbContext _db;

    public EfQuorumRepository(AppDbContext db) => _db = db;

    public Task<List<QuorumResponse>> GetByEpochAsync(int epochId) =>
        _db.QuorumResponses.Where(r => r.EpochId == epochId).ToListAsync();

    public Task<List<QuorumResponse>> GetByProducerAsync(int epochId, string producerId) =>
        _db.QuorumResponses.Where(r => r.EpochId == epochId && r.ProducerId == producerId).ToListAsync();

    public async Task AddAsync(IReadOnlyList<QuorumResponse> responses)
    {
        if (responses.Count == 0) return;
        await using var tx = await _db.Database.BeginTransactionAsync();
        _db.QuorumResponses.AddRange(responses);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task ClearAllAsync()
    {
        _db.QuorumResponses.RemoveRange(_db.QuorumResponses);
        await _db.SaveChangesAsync();
    }
}

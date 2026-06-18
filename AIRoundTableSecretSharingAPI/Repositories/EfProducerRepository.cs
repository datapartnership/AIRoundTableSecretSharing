using AIRoundTableSecretSharingAPI.Data;
using AIRoundTableSecretSharingCommon.Models;
using Microsoft.EntityFrameworkCore;

namespace AIRoundTableSecretSharingAPI.Repositories;

public class EfProducerRepository : IProducerRepository
{
    private readonly AppDbContext _db;

    public EfProducerRepository(AppDbContext db) => _db = db;

    public Task<List<ProducerInfo>> GetActiveProducersAsync(DateTime effectiveDate) =>
        _db.Producers
            .Where(p => p.JoinedDate <= effectiveDate && p.IsActive)
            .OrderBy(p => p.ProducerId)
            .ToListAsync();

    public Task<ProducerEpoch?> GetEpochForDateAsync(DateTime date) =>
        _db.Epochs
            .Where(e => e.StartDate <= date && (e.EndDate == null || e.EndDate > date))
            .OrderByDescending(e => e.StartDate)
            .FirstOrDefaultAsync();

    public async Task AddProducerAsync(ProducerInfo producer)
    {
        _db.Producers.Add(producer);
        await _db.SaveChangesAsync();
    }

    public async Task CreateEpochAsync(ProducerEpoch epoch)
    {
        var currentEpoch = await _db.Epochs.FirstOrDefaultAsync(e => e.EndDate == null);
        if (currentEpoch != null)
            currentEpoch.EndDate = epoch.StartDate;

        _db.Epochs.Add(epoch);
        await _db.SaveChangesAsync();
    }
}

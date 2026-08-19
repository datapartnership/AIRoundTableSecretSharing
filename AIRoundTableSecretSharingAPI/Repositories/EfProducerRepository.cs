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

    public Task<ProducerEpoch?> GetEpochByIdAsync(int epochId) =>
        _db.Epochs.FirstOrDefaultAsync(e => e.EpochId == epochId);

    public Task<List<ProducerEpoch>> GetAllEpochsAsync() =>
        _db.Epochs
            .OrderByDescending(e => e.StartDate)
            .ThenByDescending(e => e.EpochId)
            .ToListAsync();

    public Task<List<ProducerInfo>> GetProducersByIdsAsync(IReadOnlyCollection<string> ids) =>
        _db.Producers
            .Where(p => ids.Contains(p.ProducerId))
            .ToListAsync();

    public async Task AddProducerAsync(ProducerInfo producer)
    {
        _db.Producers.Add(producer);
        await _db.SaveChangesAsync();
    }

    public async Task UpsertProducerAsync(ProducerInfo producer)
    {
        var existing = await _db.Producers.FindAsync(producer.ProducerId);
        if (existing is null)
        {
            _db.Producers.Add(producer);
        }
        else
        {
            existing.DisplayName = producer.DisplayName;
            existing.IsActive = true;
        }
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

    public async Task ClearAllAsync()
    {
        _db.Epochs.RemoveRange(_db.Epochs);
        _db.Producers.RemoveRange(_db.Producers);
        await _db.SaveChangesAsync();
    }

    public async Task AddEpochAsync(ProducerEpoch epoch)
    {
        _db.Epochs.Add(epoch);
        await _db.SaveChangesAsync();
    }

    public async Task CloseEpochAsync(int epochId)
    {
        var epoch = await _db.Epochs.FindAsync(epochId);
        if (epoch == null || epoch.IsClosed)
            return;

        epoch.IsClosed = true;
        await _db.SaveChangesAsync();
    }
}

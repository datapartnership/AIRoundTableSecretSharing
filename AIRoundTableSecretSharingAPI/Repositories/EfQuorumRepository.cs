using AIRoundTableSecretSharingAPI.Data;
using AIRoundTableSecretSharingCommon.Models;
using Microsoft.EntityFrameworkCore;

namespace AIRoundTableSecretSharingAPI.Repositories;

public class EfQuorumRepository : IQuorumRepository
{
    private readonly AppDbContext _db;

    public EfQuorumRepository(AppDbContext db) => _db = db;

    public Task<QuorumPeriod?> GetPeriodAsync(int periodId) =>
        _db.QuorumPeriods.FirstOrDefaultAsync(p => p.PeriodId == periodId);

    public Task<List<QuorumPeriod>> GetAllPeriodsAsync() =>
        _db.QuorumPeriods.OrderByDescending(p => p.StartDate).ThenByDescending(p => p.PeriodId).ToListAsync();

    public Task<QuorumPeriod?> GetActivePeriodAsync() =>
        _db.QuorumPeriods
            .Where(p => p.CancelledAt == null && !p.QuorumComplete)
            .OrderByDescending(p => p.PeriodId)
            .FirstOrDefaultAsync();

    public async Task AddPeriodAsync(QuorumPeriod period)
    {
        _db.QuorumPeriods.Add(period);
        await _db.SaveChangesAsync();
    }

    public async Task MarkCompleteAsync(int periodId)
    {
        var period = await _db.QuorumPeriods.FindAsync(periodId);
        if (period == null || period.QuorumComplete)
            return;

        period.QuorumComplete = true;
        await _db.SaveChangesAsync();
    }

    public async Task CancelPeriodAsync(int periodId, string? reason)
    {
        var period = await _db.QuorumPeriods.FindAsync(periodId);
        if (period == null)
            return;

        period.CancelledAt ??= DateTime.UtcNow;
        if (reason != null)
            period.CancelReason = reason;
        await _db.SaveChangesAsync();
    }

    public Task<List<QuorumAnswer>> GetAnswersAsync(int periodId) =>
        _db.QuorumAnswers.Where(r => r.PeriodId == periodId).ToListAsync();

    public async Task AddAnswersAsync(IReadOnlyList<QuorumAnswer> answers)
    {
        if (answers.Count == 0) return;
        await using var tx = await _db.Database.BeginTransactionAsync();
        _db.QuorumAnswers.AddRange(answers);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task ClearAllAsync()
    {
        _db.QuorumAnswers.RemoveRange(_db.QuorumAnswers);
        _db.QuorumPeriods.RemoveRange(_db.QuorumPeriods);
        await _db.SaveChangesAsync();
    }
}

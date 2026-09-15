using AIRoundTableSecretSharingAPI.Data;
using AIRoundTableSecretSharingCommon.Models;
using Microsoft.EntityFrameworkCore;

namespace AIRoundTableSecretSharingAPI.Repositories;

public class EfKeyRepository : IKeyRepository
{
    private readonly AppDbContext _db;

    public EfKeyRepository(AppDbContext db) => _db = db;

    public async Task RegisterKeyAsync(PartnerPublicKey key)
    {
        var existing = await _db.PublicKeys.FindAsync(key.EpochId, key.ProducerId, key.DeviceId);
        if (existing != null)
        {
            existing.PublicKeyBase64 = key.PublicKeyBase64;
            existing.RegisteredAt = key.RegisteredAt;
        }
        else
        {
            _db.PublicKeys.Add(key);
        }
        await _db.SaveChangesAsync();
    }

    public Task<PartnerPublicKey?> GetKeyAsync(int epochId, string producerId, string deviceId) =>
        _db.PublicKeys.FindAsync(epochId, producerId, deviceId).AsTask();

    public Task<List<PartnerPublicKey>> GetAllKeysAsync(int epochId) =>
        _db.PublicKeys.Where(k => k.EpochId == epochId).ToListAsync();

    public async Task ClearAsync(int? epochId = null)
    {
        var keys = epochId.HasValue
            ? _db.PublicKeys.Where(k => k.EpochId == epochId.Value)
            : _db.PublicKeys;
        _db.PublicKeys.RemoveRange(keys);
        await _db.SaveChangesAsync();
    }
}

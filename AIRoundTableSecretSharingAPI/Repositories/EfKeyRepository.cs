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
        var existing = await _db.PublicKeys.FindAsync(key.ProducerId);
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

    public Task<PartnerPublicKey?> GetKeyAsync(string producerId) =>
        _db.PublicKeys.FindAsync(producerId).AsTask();

    public Task<List<PartnerPublicKey>> GetAllKeysAsync() =>
        _db.PublicKeys.ToListAsync();

    public async Task ClearAsync()
    {
        _db.PublicKeys.RemoveRange(_db.PublicKeys);
        await _db.SaveChangesAsync();
    }
}

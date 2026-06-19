using AIRoundTableSecretSharingAPI.Data;
using AIRoundTableSecretSharingCommon.Models;
using Microsoft.EntityFrameworkCore;

namespace AIRoundTableSecretSharingAPI.Repositories;

public class EfCiphertextRepository : ICiphertextRepository
{
    private readonly AppDbContext _db;

    public EfCiphertextRepository(AppDbContext db) => _db = db;

    public async Task StoreAsync(PartnerCiphertext ciphertext)
    {
        var existing = await _db.Ciphertexts
            .FirstOrDefaultAsync(c => c.SenderId == ciphertext.SenderId && c.RecipientId == ciphertext.RecipientId);

        if (existing != null)
        {
            existing.CiphertextBase64 = ciphertext.CiphertextBase64;
            existing.StoredAt = ciphertext.StoredAt;
        }
        else
        {
            _db.Ciphertexts.Add(ciphertext);
        }

        await _db.SaveChangesAsync();
    }

    public Task<List<PartnerCiphertext>> GetForRecipientAsync(string recipientId) =>
        _db.Ciphertexts
            .Where(c => c.RecipientId == recipientId)
            .ToListAsync();

    public Task<int> CountForPartnersAsync(List<string> partnerIds) =>
        _db.Ciphertexts
            .Where(c => partnerIds.Contains(c.SenderId) && partnerIds.Contains(c.RecipientId))
            .CountAsync();

    public Task<List<string>> GetSenderIdsForPartnersAsync(List<string> partnerIds) =>
        _db.Ciphertexts
            .Where(c => partnerIds.Contains(c.SenderId) && partnerIds.Contains(c.RecipientId))
            .Select(c => c.SenderId)
            .Distinct()
            .ToListAsync();

    public async Task ClearAsync()
    {
        _db.Ciphertexts.RemoveRange(_db.Ciphertexts);
        await _db.SaveChangesAsync();
    }
}

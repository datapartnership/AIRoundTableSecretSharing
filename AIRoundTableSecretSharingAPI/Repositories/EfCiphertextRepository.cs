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
            .FirstOrDefaultAsync(c => c.EpochId == ciphertext.EpochId
                && c.SenderId == ciphertext.SenderId && c.SenderDeviceId == ciphertext.SenderDeviceId
                && c.RecipientId == ciphertext.RecipientId && c.RecipientDeviceId == ciphertext.RecipientDeviceId);

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

    public Task<PartnerCiphertext?> GetAsync(int epochId, string senderId, string senderDeviceId, string recipientId, string recipientDeviceId) =>
        _db.Ciphertexts.FirstOrDefaultAsync(c => c.EpochId == epochId
            && c.SenderId == senderId && c.SenderDeviceId == senderDeviceId
            && c.RecipientId == recipientId && c.RecipientDeviceId == recipientDeviceId);

    public Task<List<PartnerCiphertext>> GetForRecipientAsync(int epochId, string recipientId, string recipientDeviceId) =>
        _db.Ciphertexts
            .Where(c => c.EpochId == epochId && c.RecipientId == recipientId && c.RecipientDeviceId == recipientDeviceId)
            .ToListAsync();

    public Task<List<PartnerCiphertext>> GetForSenderAsync(int epochId, string senderId, string senderDeviceId) =>
        _db.Ciphertexts
            .Where(c => c.EpochId == epochId && c.SenderId == senderId && c.SenderDeviceId == senderDeviceId)
            .ToListAsync();

    public Task<int> CountForPartnersAsync(int epochId, List<string> partnerIds) =>
        _db.Ciphertexts
            .Where(c => c.EpochId == epochId && partnerIds.Contains(c.SenderId) && partnerIds.Contains(c.RecipientId))
            .CountAsync();

    public Task<List<string>> GetSenderIdsForPartnersAsync(int epochId, List<string> partnerIds) =>
        _db.Ciphertexts
            .Where(c => c.EpochId == epochId && partnerIds.Contains(c.SenderId) && partnerIds.Contains(c.RecipientId))
            .Select(c => c.SenderId)
            .Distinct()
            .ToListAsync();

    public async Task ClearAsync(int? epochId = null)
    {
        var ciphertexts = epochId.HasValue
            ? _db.Ciphertexts.Where(c => c.EpochId == epochId.Value)
            : _db.Ciphertexts;
        _db.Ciphertexts.RemoveRange(ciphertexts);
        await _db.SaveChangesAsync();
    }
}

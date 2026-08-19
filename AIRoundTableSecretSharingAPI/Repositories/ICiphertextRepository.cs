using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingAPI.Repositories;

public interface ICiphertextRepository
{
    Task StoreAsync(PartnerCiphertext ciphertext);
    Task<PartnerCiphertext?> GetAsync(string senderId, string recipientId);
    Task<List<PartnerCiphertext>> GetForRecipientAsync(string recipientId);
    Task<List<PartnerCiphertext>> GetForSenderAsync(string senderId);
    Task<int> CountForPartnersAsync(List<string> partnerIds);
    Task<List<string>> GetSenderIdsForPartnersAsync(List<string> partnerIds);

    Task ClearAsync();
}

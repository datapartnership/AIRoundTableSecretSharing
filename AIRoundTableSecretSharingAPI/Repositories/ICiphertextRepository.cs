using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingAPI.Repositories;

public interface ICiphertextRepository
{
    Task StoreAsync(PartnerCiphertext ciphertext);
    Task<List<PartnerCiphertext>> GetForRecipientAsync(string recipientId);
    Task<int> CountForPartnersAsync(List<string> partnerIds);
    Task<List<string>> GetSenderIdsForPartnersAsync(List<string> partnerIds);

    Task ClearAsync();
}

using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingAPI.Repositories;

public interface ICiphertextRepository
{
    Task StoreAsync(PartnerCiphertext ciphertext);
    Task<PartnerCiphertext?> GetAsync(int epochId, string senderId, string senderDeviceId, string recipientId, string recipientDeviceId);
    Task<List<PartnerCiphertext>> GetForRecipientAsync(int epochId, string recipientId, string recipientDeviceId);
    Task<List<PartnerCiphertext>> GetForSenderAsync(int epochId, string senderId, string senderDeviceId);
    Task<int> CountForPartnersAsync(int epochId, List<string> partnerIds);
    Task<List<string>> GetSenderIdsForPartnersAsync(int epochId, List<string> partnerIds);
    Task<bool> AnyInvolvingAsync(int epochId, string producerId);

    Task ClearAsync(int? epochId = null);
}

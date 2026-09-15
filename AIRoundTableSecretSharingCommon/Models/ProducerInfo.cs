namespace AIRoundTableSecretSharingCommon.Models;

public class ProducerInfo
{
    public string ProducerId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime JoinedDate { get; set; }
    public bool IsActive { get; set; }    
}

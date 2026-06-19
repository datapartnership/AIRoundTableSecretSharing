namespace AIRoundTableSecretSharingCommon.Models;

public class MetricSubmission
{
    public string ProducerId { get; set; }
    public string Country { get; set; }
    public string Month { get; set; }
    
    /// <summary>
    /// Masked Monthly Active Users (MAU) value
    /// </summary>
    public long Value { get; set; }
    
    public int EpochId { get; set; }
    public string Signature { get; set; }
    public DateTime SubmittedAt { get; set; }
}

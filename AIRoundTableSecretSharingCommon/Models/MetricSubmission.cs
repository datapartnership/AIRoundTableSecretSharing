namespace AIRoundTableSecretSharingCommon.Models;

public class MetricSubmission
{
    public string ProducerId { get; set; }
    public string Country { get; set; }
    public DateTime Month { get; set; }
    
    /// <summary>
    /// Masked Monthly Active Users (MAU) value
    /// </summary>
    public long Value { get; set; }
    
    /// <summary>
    /// Masked Weighted MAU value (MAU × partner-specific coefficient)
    /// The coefficient is secret to each partner.
    /// </summary>
    public long? WeightedValue { get; set; }
    
    public int EpochId { get; set; }
    public string Signature { get; set; }
    public DateTime SubmittedAt { get; set; }
}

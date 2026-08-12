namespace AIRoundTableSecretSharingCommon.Models;

public class MetricSubmission
{
    public string? ProducerId { get; set; }   // set server-side from JWT
    public string Country { get; set; }
    public string Month { get; set; }

    /// <summary>
    /// Masked Monthly Active Users (MAU) value
    /// </summary>
    public long Value { get; set; }

    public int EpochId { get; set; }
    public string? Signature { get; set; }    // set server-side if applicable
    public DateTime SubmittedAt { get; set; }
}

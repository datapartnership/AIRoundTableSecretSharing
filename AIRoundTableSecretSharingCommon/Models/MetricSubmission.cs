namespace AIRoundTableSecretSharingCommon.Models;

public class MetricSubmission
{
    public string? ProducerId { get; set; }   // set server-side from JWT
    public string Country { get; set; } = string.Empty;
    public string Month { get; set; } = string.Empty;
    public string Indicator { get; set; } = string.Empty;
    public string Segment { get; set; } = string.Empty;

    /// <summary>
    /// Masked metric value (noise already applied client-side).
    /// </summary>
    public long Value { get; set; }

    public int EpochId { get; set; }
    public string? Signature { get; set; }    // set server-side if applicable
    public DateTime SubmittedAt { get; set; }
}

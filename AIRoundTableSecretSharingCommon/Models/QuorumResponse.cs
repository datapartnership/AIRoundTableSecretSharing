namespace AIRoundTableSecretSharingCommon.Models;

/// <summary>A partner's plain (unmasked) answer to the Quorum Check for one metric cell.</summary>
public class QuorumResponse
{
    public int EpochId { get; set; }
    public string ProducerId { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Month { get; set; } = string.Empty;
    public string Indicator { get; set; } = string.Empty;
    public string Segment { get; set; } = string.Empty;
    public bool Participates { get; set; }
    public DateTime SubmittedAt { get; set; }
}

public class QuorumSubmission
{
    public int EpochId { get; set; }
    public string Country { get; set; } = string.Empty;
    public string Month { get; set; } = string.Empty;
    public string Indicator { get; set; } = string.Empty;
    public string Segment { get; set; } = string.Empty;
    /// <summary>1 = participate, 0 = do not participate.</summary>
    public int Value { get; set; }
}

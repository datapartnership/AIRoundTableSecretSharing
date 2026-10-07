namespace AIRoundTableSecretSharingCommon.Models;

/// <summary>
/// A partner's masked answer to the Quorum Check for one metric cell. Pairwise noise cancels in the sum, so the
/// server only learns the totals: how many partners participate, and a composition tag sum.
/// </summary>
public class QuorumResponse
{
    public int EpochId { get; set; }
    public string ProducerId { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Month { get; set; } = string.Empty;
    public string Indicator { get; set; } = string.Empty;
    public string Segment { get; set; } = string.Empty;

    /// <summary>Masked 0/1 participation flag.</summary>
    public long MaskedCount { get; set; }

    /// <summary>
    /// Masked (flag × partner tag), two independent 64-bit lanes. Each partner's tag is a secret random number,
    /// so the unmasked sum fingerprints the participant set without revealing it.
    /// </summary>
    public long MaskedTagA { get; set; }
    public long MaskedTagB { get; set; }
    public DateTime SubmittedAt { get; set; }
}

public class QuorumSubmission
{
    public int EpochId { get; set; }
    public string Country { get; set; } = string.Empty;
    public string Month { get; set; } = string.Empty;
    public string Indicator { get; set; } = string.Empty;
    public string Segment { get; set; } = string.Empty;
    public long MaskedCount { get; set; }
    public long MaskedTagA { get; set; }
    public long MaskedTagB { get; set; }
}

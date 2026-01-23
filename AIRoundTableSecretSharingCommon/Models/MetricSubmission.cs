namespace AIRoundTableSecretSharingCommon.Models;

public class MetricSubmission
{
    public string ProducerId { get; set; }
    public string Country { get; set; }
    public DateTime Month { get; set; }
    public long Value { get; set; }
    public int EpochId { get; set; }
    public string Signature { get; set; }
    public DateTime SubmittedAt { get; set; }
}

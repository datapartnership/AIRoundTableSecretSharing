namespace AIRoundTableSecretSharingCommon.Models;

public class SubmissionResult
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public string ProducerId { get; set; }
    
    // MAU values
    public long OriginalValue { get; set; }
    public long MaskedValue { get; set; }
    public long NoiseApplied { get; set; }
    public Dictionary<string, long> NoiseBreakdown { get; set; }
}

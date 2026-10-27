namespace AIRoundTableSecretSharingCommon.Models;

public class SubmissionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ProducerId { get; set; } = string.Empty;
    
    // MAU values
    public Int128 OriginalValue { get; set; }
    public Int128 MaskedValue { get; set; }
    public Int128 NoiseApplied { get; set; }
    public Dictionary<string, Int128> NoiseBreakdown { get; set; } = new();
}

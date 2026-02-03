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
    
    // Weighted MAU values (MAU × coefficient)
    public long? OriginalWeightedValue { get; set; }
    public long? MaskedWeightedValue { get; set; }
    public long? WeightedNoiseApplied { get; set; }
    public Dictionary<string, long> WeightedNoiseBreakdown { get; set; }
    
    /// <summary>
    /// The coefficient used by this partner (kept secret from aggregator)
    /// </summary>
    public double? Coefficient { get; set; }
}

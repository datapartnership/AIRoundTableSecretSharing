namespace AIRoundTableSecretSharingCommon.Models;

public class AggregationResult
{
    public string Status { get; set; }
    public string Country { get; set; }
    public DateTime Month { get; set; }
    
    /// <summary>
    /// Total MAU across all partners (noise-cancelled aggregate)
    /// </summary>
    public long? Total { get; set; }
    
    /// <summary>
    /// Total Weighted MAU across all partners (noise-cancelled aggregate)
    /// This is the sum of each partner's (MAU × coefficient)
    /// </summary>
    public long? WeightedTotal { get; set; }
    
    /// <summary>
    /// Weighted average ratio: TotalWeightedMAU / TotalMAU
    /// This represents the aggregate weighted coefficient across all partners.
    /// </summary>
    public double? WeightedRatio { get; set; }
    
    public int SubmissionCount { get; set; }
    public int ExpectedSubmissions { get; set; }
    public List<string> MissingProducers { get; set; }
}

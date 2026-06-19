namespace AIRoundTableSecretSharingCommon.Models;

public class AggregationResult
{
    public string Status { get; set; }
    public string Country { get; set; }
    public string Month { get; set; }
    
    /// <summary>
    /// Total MAU across all partners (noise-cancelled aggregate)
    /// </summary>
    public long? Total { get; set; }
    
    public int SubmissionCount { get; set; }
    public int ExpectedSubmissions { get; set; }
    public List<string> MissingProducers { get; set; }
}

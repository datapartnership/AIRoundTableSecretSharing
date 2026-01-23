namespace AIRoundTableSecretSharingCommon.Models;

public class AggregationResult
{
    public string Status { get; set; }
    public string Country { get; set; }
    public DateTime Month { get; set; }
    public long? Total { get; set; }
    public int SubmissionCount { get; set; }
    public int ExpectedSubmissions { get; set; }
    public List<string> MissingProducers { get; set; }
}

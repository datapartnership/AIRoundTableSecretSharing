using System.Text.Json.Serialization;

namespace AIRoundTableSecretSharingCommon.Models;

public class AggregationResult
{
    public string Status { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Month { get; set; } = string.Empty;
    public string Indicator { get; set; } = string.Empty;
    public string Segment { get; set; } = string.Empty;

    /// <summary>
    /// Noise-cancelled aggregate across all partners. Serialized as a string so
    /// values beyond JavaScript MAX_SAFE_INTEGER stay exact in the web UI.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Total { get; set; }

    public int SubmissionCount { get; set; }
    public int ExpectedSubmissions { get; set; }
    public List<string> MissingProducers { get; set; } = new();
}

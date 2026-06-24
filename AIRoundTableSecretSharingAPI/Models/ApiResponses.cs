using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingAPI.Models;

public class MessageResponse
{
    public string Message { get; set; } = string.Empty;
}

public class TokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
}

public class AddProducerResponse
{
    public string Message { get; set; } = string.Empty;
    public ProducerEpoch Epoch { get; set; } = null!;
}

public class ReplaceProducersRequest
{
    public List<ReplaceProducerItem> Producers { get; set; } = new();
}

public class ReplaceProducerItem
{
    public string ProducerId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class ReplaceProducersResponse
{
    public string Message { get; set; } = string.Empty;
    public ProducerEpoch Epoch { get; set; } = null!;
    public List<string> Producers { get; set; } = new();
}

public class KeyExchangeStatusResponse
{
    public bool IsComplete { get; set; }
    public int RegisteredCount { get; set; }
    public int ExpectedCount { get; set; }
    public List<string> RegisteredPartners { get; set; } = new();
    public List<string> MissingPartners { get; set; } = new();
    public int ActualCiphertexts { get; set; }
    public int ExpectedCiphertexts { get; set; }
    public bool IsCiphertextExchangeComplete { get; set; }
    public List<string> MissingCiphertextSenders { get; set; } = new();
}

public class SubmittedEntry
{
    public string Country { get; set; } = string.Empty;
    public string Month { get; set; } = string.Empty;
}

public class ProducerSubmissionsResponse
{
    public int EpochId { get; set; }
    public List<SubmittedEntry> Submissions { get; set; } = new();
}

public class LatestEpochAggregatesResponse
{
    public int EpochId { get; set; }
    public DateTime StartDate { get; set; }
    public int PartnerCount { get; set; }
    public List<string> Partners { get; set; } = new();
    public List<AggregationResult> Aggregates { get; set; } = new();
}

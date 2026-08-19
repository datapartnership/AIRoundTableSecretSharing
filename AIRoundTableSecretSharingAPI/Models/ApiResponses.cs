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
    public string? StartMonth { get; set; }
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
    /// <summary>Caller's currently registered encapsulation key, if any.</summary>
    public string? MyPublicKeyBase64 { get; set; }
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
    public bool IsClosed { get; set; }
    public List<AggregationResult> Aggregates { get; set; } = new();
}

public class EpochSummary
{
    public int EpochId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int ProducerCount { get; set; }
    public bool IsClosed { get; set; }
}

public class EpochListResponse
{
    public List<EpochSummary> Epochs { get; set; } = new();
}

public class EpochPartnerInfo
{
    public string ProducerId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class MissingProducerStatus
{
    public string ProducerId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<SubmittedEntry> MissingCells { get; set; } = new();
}

public class EpochDetailResponse
{
    public int EpochId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int PartnerCount { get; set; }
    public bool IsClosed { get; set; }
    public List<EpochPartnerInfo> Partners { get; set; } = new();
    public List<MissingProducerStatus> MissingProducers { get; set; } = new();
    public List<AggregationResult> Aggregates { get; set; } = new();
}

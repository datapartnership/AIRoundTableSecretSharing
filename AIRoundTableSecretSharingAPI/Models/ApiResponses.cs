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

public class CancelEpochRequest
{
    [System.ComponentModel.DataAnnotations.MaxLength(500)]
    public string? Reason { get; set; }
}

public class ReplaceProducersRequest
{
    public string UsageType { get; set; } = UsageTypes.Api;
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
    public string Indicator { get; set; } = string.Empty;
    public string Segment { get; set; } = string.Empty;
    /// <summary>Set only in the caller's own submission list.</summary>
    public DateTime? SubmittedAt { get; set; }
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
    public string UsageType { get; set; } = UsageTypes.Api;
    public int? QuorumPeriodId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int ProducerCount { get; set; }
    public bool IsClosed { get; set; }
    public bool QuorumComplete { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public int? ReplacedByEpochId { get; set; }
    public bool IsCancelled => CancelledAt != null;
    public string Phase => IsCancelled ? "cancelled" : IsClosed ? "closed" : QuorumComplete ? "submission" : "quorum";
    public List<string> ProducerIds { get; set; } = new();
    public bool IsEligible { get; set; }
    public bool CanParticipate => IsEligible && !IsClosed && !IsCancelled;
    /// <summary>The caller submitted every cell they owe for this epoch.</summary>
    public bool MySubmissionComplete { get; set; }
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
    public string UsageType { get; set; } = UsageTypes.Api;
    public QuorumPeriodSummary? QuorumPeriod { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int PartnerCount { get; set; }
    public bool IsClosed { get; set; }
    public bool QuorumComplete { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public int? ReplacedByEpochId { get; set; }
    public bool IsCancelled => CancelledAt != null;
    public string Phase => IsCancelled ? "cancelled" : IsClosed ? "closed" : QuorumComplete ? "submission" : "quorum";
    public List<EpochPartnerInfo> Partners { get; set; } = new();
    public List<QuorumPartnerStatus> QuorumPartners { get; set; } = new();
    public List<QuorumCellInfo> QuorumCells { get; set; } = new();
    public List<MissingProducerStatus> MissingProducers { get; set; } = new();
    public List<AggregationResult> Aggregates { get; set; } = new();
}


public class QuorumPartnerStatus
{
    public string ProducerId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int AnsweredMetrics { get; set; }
    public bool Done { get; set; }
}

public class QuorumCellInfo
{
    public string Country { get; set; } = string.Empty;
    public string Indicator { get; set; } = string.Empty;
    public int ParticipantCount { get; set; }
    public bool Ignored { get; set; }
    public string Signature { get; set; } = string.Empty;
    /// <summary>The masked answers did not sum to a valid count, so the metric is ignored.</summary>
    public bool Unverified { get; set; }
}

public class QuorumPeriodSummary
{
    public QuorumPeriodSummary() { }

    public QuorumPeriodSummary(QuorumPeriod p)
    {
        PeriodId = p.PeriodId;
        UsageType = p.UsageType;
        StartDate = p.StartDate;
        EndDate = p.EndDate;
        ProducerIds = p.ProducerIds;
        ProducerCount = p.ProducerIds.Count;
        QuorumComplete = p.QuorumComplete;
        CreatedAt = p.CreatedAt;
        CancelledAt = p.CancelledAt;
        CancelReason = p.CancelReason;
    }

    public int PeriodId { get; set; }
    public string UsageType { get; set; } = UsageTypes.Api;
    public DateTime StartDate { get; set; }
    /// <summary>Exclusive: the first day after the period.</summary>
    public DateTime EndDate { get; set; }
    public List<string> ProducerIds { get; set; } = new();
    public int ProducerCount { get; set; }
    public bool QuorumComplete { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public bool IsCancelled => CancelledAt != null;
}

public class QuorumMetric
{
    public string Country { get; set; } = string.Empty;
    public string Indicator { get; set; } = string.Empty;
}

/// <summary>Partner-facing quorum state: progress, plus the metrics that fell below the quorum once it is complete.</summary>
public class QuorumStatusResponse
{
    /// <summary>Null when there is no period.</summary>
    public string? UsageType { get; set; }
    /// <summary>Null when no Quorum Check is active.</summary>
    public QuorumPeriodSummary? Period { get; set; }
    public bool IsEligible { get; set; }
    public bool QuorumComplete { get; set; }
    public bool MyDone { get; set; }
    public DateTime? MyAnsweredAt { get; set; }
    public int AnsweredPartners { get; set; }
    public int PartnerCount { get; set; }
    public List<QuorumMetric> IgnoredMetrics { get; set; } = new();
}

public class CreateQuorumPeriodRequest
{
    public string UsageType { get; set; } = UsageTypes.Api;
    public string? StartMonth { get; set; }
    public List<ReplaceProducerItem> Producers { get; set; } = new();
}

public class QuorumPeriodListResponse
{
    public List<QuorumPeriodSummary> Periods { get; set; } = new();
}

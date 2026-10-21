using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIRoundTableSecretSharingCommon.Models;

/// <summary>
/// A Quorum Check that covers every epoch of one usage type for <see cref="MonthCount"/> months. Its partners run
/// their own key exchange (key scope <see cref="KeyScopeId"/>) so their answers can be masked.
/// </summary>
public class QuorumPeriod
{
    public const int MonthCount = 12;

    public int PeriodId { get; set; }
    [MaxLength(16)]
    public string UsageType { get; set; } = UsageTypes.Api;
    public DateTime StartDate { get; set; }
    public List<string> ProducerIds { get; set; } = new();
    /// <summary>True once every partner answered.</summary>
    public bool QuorumComplete { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    [MaxLength(500)]
    public string? CancelReason { get; set; }

    [NotMapped]
    public DateTime EndDate => StartDate.AddMonths(MonthCount);
    [NotMapped]
    public bool IsCancelled => CancelledAt != null;
    /// <summary>Keys and ciphertexts of a period are stored under its negated id so they never collide with an epoch's.</summary>
    [NotMapped]
    public int KeyScopeId => -PeriodId;

    /// <summary>True when the epoch's reporting months all fall inside this period.</summary>
    public bool Covers(DateTime epochStart) =>
        epochStart >= StartDate && epochStart.AddMonths(EpochGrid.MonthCount) <= EndDate;
}

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

    /// <summary>Quarterly epochs in the period, created in order.</summary>
    [NotMapped]
    public int EpochCount => MonthCount / EpochGrid.QuarterMonths;

    public DateTime SlotStart(int slot) => StartDate.AddMonths(slot * EpochGrid.QuarterMonths);

    /// <summary>
    /// The quarter after the last month any of the period's epochs reports, cancelled ones included (a cancelled
    /// quarter is redone by recreating it); null once every quarter is used.
    /// </summary>
    public int? NextSlot(IEnumerable<ProducerEpoch> epochsOfPeriod)
    {
        var usedMonths = epochsOfPeriod
            .Select(e => (e.StartDate.Year - StartDate.Year) * 12 + e.StartDate.Month - StartDate.Month + e.MonthCount)
            .DefaultIfEmpty(0)
            .Max();
        var slot = (usedMonths + EpochGrid.QuarterMonths - 1) / EpochGrid.QuarterMonths;
        return slot < EpochCount ? slot : null;
    }
}

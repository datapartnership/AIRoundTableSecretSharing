using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIRoundTableSecretSharingCommon.Models;

public class ProducerEpoch
{
    public int EpochId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public List<string> ProducerIds { get; set; } = new List<string>();
    public int ProducerCount { get; set; }
    /// <summary>True once every producer has submitted every required country/month cell.</summary>
    public bool IsClosed { get; set; }
    /// <summary>False while the epoch is in its Quorum Check phase; true once every partner answered.</summary>
    public bool QuorumComplete { get; set; }
    /// <summary>Set when an admin cancels the epoch; a cancelled epoch accepts no further key exchange, quorum or submissions.</summary>
    public DateTime? CancelledAt { get; set; }
    [MaxLength(500)]
    public string? CancelReason { get; set; }
    /// <summary>The epoch created to replace this one when an admin recreated it.</summary>
    public int? ReplacedByEpochId { get; set; }
    [NotMapped]
    public bool IsCancelled => CancelledAt != null;
    [NotMapped]
    public bool IsEligible { get; set; }
    [NotMapped]
    public bool CanParticipate => IsEligible && !IsClosed && !IsCancelled;
}
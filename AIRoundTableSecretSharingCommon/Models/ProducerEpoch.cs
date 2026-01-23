namespace AIRoundTableSecretSharingCommon.Models;

public class ProducerEpoch
{
    public int EpochId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public List<string> ProducerIds { get; set; } = new List<string>();
    public int ProducerCount { get; set; }
}
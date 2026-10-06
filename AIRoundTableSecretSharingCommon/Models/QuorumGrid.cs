using System.Security.Cryptography;
using System.Text;

namespace AIRoundTableSecretSharingCommon.Models;

public sealed record QuorumCellResult(
    string Country,
    string Month,
    string Indicator,
    string Segment,
    int ParticipantCount,
    bool Ignored,
    string Signature,
    IReadOnlyList<string> Participants);

public static class QuorumGrid
{
    public const int MinParticipants = 3;

    /// <summary>
    /// Order-independent SHA-256 fingerprint of the participating partner set. Empty set yields the hash of "".
    /// </summary>
    public static string Signature(IEnumerable<string> participantIds)
    {
        var joined = string.Join("\n", participantIds.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined))).ToLowerInvariant();
    }

    public static List<QuorumCellResult> Evaluate(ProducerEpoch epoch, IEnumerable<QuorumResponse> responses)
    {
        var yes = responses
            .Where(r => r.Participates && epoch.ProducerIds.Contains(r.ProducerId))
            .GroupBy(r => EpochGrid.CellKey(r.Country, r.Month, r.Indicator, r.Segment))
            .ToDictionary(g => g.Key, g => g.Select(r => r.ProducerId).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList());

        var results = new List<QuorumCellResult>();
        foreach (var country in EpochGrid.Countries)
            foreach (var month in EpochGrid.Months(epoch.StartDate))
                foreach (var (indicator, segment) in EpochGrid.Series)
                {
                    var participants = yes.TryGetValue(EpochGrid.CellKey(country, month, indicator, segment), out var l)
                        ? l : new List<string>();
                    results.Add(new QuorumCellResult(
                        country, month, indicator, segment, participants.Count,
                        participants.Count < MinParticipants, Signature(participants), participants));
                }
        return results;
    }

    public static bool IsComplete(ProducerEpoch epoch, IEnumerable<QuorumResponse> responses)
    {
        if (epoch.ProducerIds.Count == 0) return false;
        var counts = responses
            .Where(r => epoch.ProducerIds.Contains(r.ProducerId))
            .GroupBy(r => r.ProducerId)
            .ToDictionary(g => g.Key, g => g.Select(r => EpochGrid.CellKey(r.Country, r.Month, r.Indicator, r.Segment)).Distinct().Count());
        return epoch.ProducerIds.All(id => counts.TryGetValue(id, out var c) && c >= EpochGrid.CellCount);
    }
}

using System.Globalization;
using AIRoundTableSecretSharingCommon.Core;

namespace AIRoundTableSecretSharingCommon.Models;

public sealed record QuorumCellResult(
    string Country,
    string Month,
    string Indicator,
    string Segment,
    int ParticipantCount,
    bool Ignored,
    string Signature);

public static class QuorumGrid
{
    public const int MinParticipants = 3;

    /// <summary>
    /// Sums the masked answers per cell. Only meaningful once every partner has answered (masks then cancel);
    /// until then no cells are returned.
    /// </summary>
    public static List<QuorumCellResult> Evaluate(ProducerEpoch epoch, IEnumerable<QuorumResponse> responses)
    {
        var results = new List<QuorumCellResult>();
        var list = responses.Where(r => epoch.ProducerIds.Contains(r.ProducerId)).ToList();
        if (!IsComplete(epoch, list))
            return results;

        var byCell = list.GroupBy(r => EpochGrid.CellKey(r.Country, r.Month, r.Indicator, r.Segment))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var country in EpochGrid.Countries)
            foreach (var month in EpochGrid.Months(epoch.StartDate))
                foreach (var (indicator, segment) in EpochGrid.Series)
                {
                    var rows = byCell[EpochGrid.CellKey(country, month, indicator, segment)];
                    var count = SecureNoiseGenerator.SumMasked(rows.Select(r => r.MaskedCount));
                    var tagA = SecureNoiseGenerator.SumMasked(rows.Select(r => r.MaskedTagA));
                    var tagB = SecureNoiseGenerator.SumMasked(rows.Select(r => r.MaskedTagB));
                    var n = (int)Math.Clamp(count, 0, epoch.ProducerIds.Count);
                    // Nobody participating has no composition to fingerprint
                    var signature = n == 0
                        ? string.Empty
                        : ((ulong)tagA).ToString("x16", CultureInfo.InvariantCulture) + ((ulong)tagB).ToString("x16", CultureInfo.InvariantCulture);
                    results.Add(new QuorumCellResult(country, month, indicator, segment, n, n < MinParticipants, signature));
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

using System.Globalization;
using AIRoundTableSecretSharingCommon.Core;

namespace AIRoundTableSecretSharingCommon.Models;

public sealed record QuorumCellResult(
    string Country,
    string Indicator,
    int ParticipantCount,
    bool Ignored,
    string Signature,
    bool Unverified = false);

/// <summary>
/// Quorum Check grid: one answer per country and indicator (5 × 3). An answer applies to every month and segment of
/// that indicator in every epoch of the period.
/// </summary>
public static class QuorumGrid
{
    public const int MinParticipants = 3;

    public static readonly string[] Indicators = EpochGrid.Series.Select(s => s.Indicator).Distinct().ToArray();

    public static int CellCount => EpochGrid.Countries.Length * Indicators.Length;

    public static string PairKey(string country, string indicator) => $"{country}|{indicator}";

    public static bool IsCell(string country, string indicator) =>
        EpochGrid.Countries.Contains(country, StringComparer.Ordinal) && Indicators.Contains(indicator, StringComparer.Ordinal);

    /// <summary>
    /// Sums the masked answers per cell. Only meaningful once every partner has answered (masks then cancel);
    /// until then no cells are returned.
    /// </summary>
    public static List<QuorumCellResult> Evaluate(QuorumPeriod period, IEnumerable<QuorumAnswer> answers)
    {
        var results = new List<QuorumCellResult>();
        var list = answers.Where(r => period.ProducerIds.Contains(r.ProducerId)).ToList();
        if (!IsComplete(period, list))
            return results;

        var byCell = list.GroupBy(r => PairKey(r.Country, r.Indicator)).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var country in EpochGrid.Countries)
            foreach (var indicator in Indicators)
            {
                var rows = byCell[PairKey(country, indicator)];
                var count = SecureNoiseGenerator.SumMasked(rows.Select(r => r.MaskedCount));
                var tagA = SecureNoiseGenerator.SumMasked(rows.Select(r => r.MaskedTagA));
                var tagB = SecureNoiseGenerator.SumMasked(rows.Select(r => r.MaskedTagB));
                // A sum outside [0, partners] means the masks did not cancel; the metric can't be verified, so it gets no data
                if (count < 0 || count > period.ProducerIds.Count)
                {
                    results.Add(new QuorumCellResult(country, indicator, -1, true, string.Empty, Unverified: true));
                    continue;
                }
                var n = (int)count;
                // Nobody participating has no composition to fingerprint
                var signature = n == 0
                    ? string.Empty
                    : ((ulong)tagA).ToString("x16", CultureInfo.InvariantCulture) + ((ulong)tagB).ToString("x16", CultureInfo.InvariantCulture);
                results.Add(new QuorumCellResult(country, indicator, n, n < MinParticipants, signature));
            }
        return results;
    }

    public static bool IsComplete(QuorumPeriod period, IEnumerable<QuorumAnswer> answers)
    {
        if (period.ProducerIds.Count == 0) return false;
        var counts = AnsweredCounts(period, answers);
        return period.ProducerIds.All(id => counts.TryGetValue(id, out var c) && c >= CellCount);
    }

    public static Dictionary<string, int> AnsweredCounts(QuorumPeriod period, IEnumerable<QuorumAnswer> answers) =>
        answers
            .Where(r => period.ProducerIds.Contains(r.ProducerId))
            .GroupBy(r => r.ProducerId)
            .ToDictionary(g => g.Key, g => g.Select(r => PairKey(r.Country, r.Indicator)).Distinct().Count());

    /// <summary>
    /// Epoch cell keys whose metric fell below the quorum. Partners submit no data for these cells and they don't
    /// count toward epoch completion.
    /// </summary>
    public static HashSet<string> IgnoredCellKeys(ProducerEpoch epoch, IEnumerable<QuorumCellResult> cells)
    {
        var ignored = cells.Where(c => c.Ignored).Select(c => PairKey(c.Country, c.Indicator)).ToHashSet(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var country in EpochGrid.Countries)
            foreach (var month in EpochGrid.Months(epoch))
                foreach (var (indicator, segment) in EpochGrid.Series)
                    if (ignored.Contains(PairKey(country, indicator)))
                        keys.Add(EpochGrid.CellKey(country, month, indicator, segment));
        return keys;
    }
}

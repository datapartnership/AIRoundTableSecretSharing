namespace AIRoundTableSecretSharingCommon.Models;

/// <summary>
/// Required submission cells for an epoch: 5 countries × its months (a quarter) × 10 indicator/segment series.
/// Must stay in sync with the web UI CSV schema (sample.csv).
/// </summary>
public static class EpochGrid
{
    public static readonly string[] Countries = ["IND", "BRA", "USA", "GBR", "NGA"];
    /// <summary>Months of a new epoch; older epochs keep their own <see cref="ProducerEpoch.MonthCount"/>.</summary>
    public const int QuarterMonths = 3;

    public static readonly (string Indicator, string Segment)[] Series =
    [
        ("tokens", "total"),
        ("token_access", "open_source"),
        ("token_access", "proprietary"),
        ("model_region_of_origin", "EAS"),
        ("model_region_of_origin", "ECS"),
        ("model_region_of_origin", "LCN"),
        ("model_region_of_origin", "MEA"),
        ("model_region_of_origin", "NAC"),
        ("model_region_of_origin", "SAS"),
        ("model_region_of_origin", "SSF"),
    ];

    public static int CellCount(ProducerEpoch epoch) => Countries.Length * epoch.MonthCount * Series.Length;

    public static IReadOnlyList<string> Months(ProducerEpoch epoch) =>
        Enumerable.Range(0, epoch.MonthCount)
            .Select(i => epoch.StartDate.AddMonths(i).ToString("yyyy-MM"))
            .ToList();

    public static bool IsRequiredCell(
        string country,
        string month,
        string indicator,
        string segment,
        ProducerEpoch epoch)
    {
        if (!Countries.Contains(country, StringComparer.Ordinal))
            return false;
        if (!Months(epoch).Contains(month, StringComparer.Ordinal))
            return false;
        return Series.Any(s =>
            string.Equals(s.Indicator, indicator, StringComparison.Ordinal) &&
            string.Equals(s.Segment, segment, StringComparison.Ordinal));
    }

    public static string CellKey(string country, string month, string indicator, string segment) =>
        $"{country}|{month}|{indicator}|{segment}";

    public static List<(string ProducerId, string Country, string Month, string Indicator, string Segment)> MissingCells(
        ProducerEpoch epoch,
        IEnumerable<MetricSubmission> submissions,
        IReadOnlySet<string>? excludedKeys = null)
    {
        var months = Months(epoch);
        var keys = submissions
            .Where(s => !string.IsNullOrEmpty(s.ProducerId))
            .Select(s => (s.ProducerId!, s.Country, s.Month, s.Indicator, s.Segment))
            .ToHashSet();

        var missing = new List<(string, string, string, string, string)>();
        foreach (var producerId in epoch.ProducerIds)
            foreach (var country in Countries)
                foreach (var month in months)
                    foreach (var (indicator, segment) in Series)
                    {
                        // Excluded cells (below the quorum) get no data from anyone
                        if (excludedKeys != null && excludedKeys.Contains(CellKey(country, month, indicator, segment)))
                            continue;
                        if (!keys.Contains((producerId, country, month, indicator, segment)))
                            missing.Add((producerId, country, month, indicator, segment));
                    }

        return missing;
    }

    public static bool IsFullySubmitted(
        ProducerEpoch epoch,
        IEnumerable<MetricSubmission> submissions,
        IReadOnlySet<string>? excludedKeys = null) =>
        epoch.ProducerIds.Count > 0 && MissingCells(epoch, submissions, excludedKeys).Count == 0;
}

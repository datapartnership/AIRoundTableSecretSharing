namespace AIRoundTableSecretSharingCommon.Models;

/// <summary>
/// Required submission cells for an epoch: 5 countries × 4 months × 10 indicator/segment series.
/// Must stay in sync with the web UI CSV schema (sample.csv).
/// </summary>
public static class EpochGrid
{
    public static readonly string[] Countries = ["IND", "BRA", "USA", "GBR", "NGA"];
    public const int MonthCount = 4;

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

    public static int CellCount => Countries.Length * MonthCount * Series.Length;

    public static IReadOnlyList<string> Months(DateTime startDate) =>
        Enumerable.Range(0, MonthCount)
            .Select(i => startDate.AddMonths(i).ToString("yyyy-MM"))
            .ToList();

    public static bool IsRequiredCell(
        string country,
        string month,
        string indicator,
        string segment,
        DateTime startDate)
    {
        if (!Countries.Contains(country, StringComparer.Ordinal))
            return false;
        if (!Months(startDate).Contains(month, StringComparer.Ordinal))
            return false;
        return Series.Any(s =>
            string.Equals(s.Indicator, indicator, StringComparison.Ordinal) &&
            string.Equals(s.Segment, segment, StringComparison.Ordinal));
    }

    public static string CellKey(string country, string month, string indicator, string segment) =>
        $"{country}|{month}|{indicator}|{segment}";

    public static List<(string ProducerId, string Country, string Month, string Indicator, string Segment)> MissingCells(
        ProducerEpoch epoch,
        IEnumerable<MetricSubmission> submissions)
    {
        var months = Months(epoch.StartDate);
        var keys = submissions
            .Where(s => !string.IsNullOrEmpty(s.ProducerId))
            .Select(s => (s.ProducerId!, s.Country, s.Month, s.Indicator, s.Segment))
            .ToHashSet();

        var missing = new List<(string, string, string, string, string)>();
        foreach (var producerId in epoch.ProducerIds)
            foreach (var country in Countries)
                foreach (var month in months)
                    foreach (var (indicator, segment) in Series)
                        if (!keys.Contains((producerId, country, month, indicator, segment)))
                            missing.Add((producerId, country, month, indicator, segment));

        return missing;
    }

    public static bool IsFullySubmitted(ProducerEpoch epoch, IEnumerable<MetricSubmission> submissions) =>
        epoch.ProducerIds.Count > 0 && MissingCells(epoch, submissions).Count == 0;
}

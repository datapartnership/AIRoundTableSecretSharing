namespace AIRoundTableSecretSharingCommon.Models;

/// <summary>
/// Required submission cells for an epoch: 3 countries × 3 months from the epoch start date.
/// Must stay in sync with the web UI grid.
/// </summary>
public static class EpochGrid
{
    public static readonly string[] Countries = ["US", "GB", "DE"];
    public const int MonthCount = 3;

    public static IReadOnlyList<string> Months(DateTime startDate) =>
        Enumerable.Range(0, MonthCount)
            .Select(i => startDate.AddMonths(i).ToString("yyyy-MM"))
            .ToList();

    public static List<(string ProducerId, string Country, string Month)> MissingCells(
        ProducerEpoch epoch,
        IEnumerable<MetricSubmission> submissions)
    {
        var months = Months(epoch.StartDate);
        var keys = submissions
            .Where(s => !string.IsNullOrEmpty(s.ProducerId))
            .Select(s => (s.ProducerId!, s.Country, s.Month))
            .ToHashSet();

        var missing = new List<(string, string, string)>();
        foreach (var producerId in epoch.ProducerIds)
            foreach (var country in Countries)
                foreach (var month in months)
                    if (!keys.Contains((producerId, country, month)))
                        missing.Add((producerId, country, month));

        return missing;
    }

    public static bool IsFullySubmitted(ProducerEpoch epoch, IEnumerable<MetricSubmission> submissions) =>
        epoch.ProducerIds.Count > 0 && MissingCells(epoch, submissions).Count == 0;
}

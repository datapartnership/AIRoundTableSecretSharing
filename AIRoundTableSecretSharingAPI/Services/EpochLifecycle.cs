using AIRoundTableSecretSharingAPI.Repositories;
using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingAPI.Services;

public static class EpochLifecycle
{
    public const int MinParticipants = 3;

    public static async Task CloseIfCompleteAsync(
        ProducerEpoch epoch,
        ISubmissionRepository submissions,
        IProducerRepository producers,
        IQuorumRepository quorum)
    {
        if (epoch.IsClosed || epoch.IsCancelled)
            return;

        var rows = await submissions.GetSubmissionsByEpochAsync(epoch.EpochId);
        var ignored = await IgnoredKeysAsync(epoch, quorum);
        if (!EpochGrid.IsFullySubmitted(epoch, rows, ignored))
            return;

        await producers.CloseEpochAsync(epoch.EpochId);
        epoch.IsClosed = true;
    }

    /// <summary>The epoch's Quorum Check period; null for epochs created before periods existed.</summary>
    public static async Task<QuorumPeriod?> PeriodOfAsync(ProducerEpoch epoch, IQuorumRepository quorum) =>
        epoch.QuorumPeriodId is { } id ? await quorum.GetPeriodAsync(id) : null;

    /// <summary>Epochs without a period have no Quorum Check, so it counts as complete.</summary>
    public static async Task<bool> IsQuorumCompleteAsync(ProducerEpoch epoch, IQuorumRepository quorum)
    {
        var period = await PeriodOfAsync(epoch, quorum);
        if (period == null)
            return true;
        await CompletePeriodIfAnsweredAsync(period, quorum);
        return period.QuorumComplete;
    }

    /// <summary>Cell keys below the quorum; empty until the period's Quorum Check is complete (and without a period).</summary>
    public static async Task<HashSet<string>> IgnoredKeysAsync(ProducerEpoch epoch, IQuorumRepository quorum)
    {
        var period = await PeriodOfAsync(epoch, quorum);
        if (period is not { QuorumComplete: true })
            return new HashSet<string>(StringComparer.Ordinal);
        return QuorumGrid.IgnoredCellKeys(epoch, QuorumGrid.Evaluate(period, await quorum.GetAnswersAsync(period.PeriodId)));
    }

    public static async Task CompletePeriodIfAnsweredAsync(QuorumPeriod period, IQuorumRepository quorum)
    {
        if (period.QuorumComplete || period.IsCancelled)
            return;

        if (!QuorumGrid.IsComplete(period, await quorum.GetAnswersAsync(period.PeriodId)))
            return;

        await quorum.MarkCompleteAsync(period.PeriodId);
        period.QuorumComplete = true;
    }

    public const string SupportEmail = "datapartnership@worldbank.org";

    /// <summary>Returns an error payload when an admin cancelled the epoch, otherwise null.</summary>
    public static object? CancelledError(ProducerEpoch epoch) =>
        epoch.IsCancelled
            ? new
            {
                error = epoch.ReplacedByEpochId is { } next
                    ? $"Epoch {epoch.EpochId} was cancelled by an admin and replaced by epoch {next}."
                    : $"Epoch {epoch.EpochId} was cancelled by an admin.",
                code = "epoch-cancelled",
                replacedByEpochId = epoch.ReplacedByEpochId
            }
            : null;

    /// <summary>Returns an error payload when an admin cancelled the period, otherwise null.</summary>
    public static object? CancelledError(QuorumPeriod period) =>
        period.IsCancelled
            ? new { error = $"Quorum Check period {period.PeriodId} was cancelled by an admin.", code = "period-cancelled" }
            : null;

    /// <summary>Returns an error payload when key exchange is not finished, otherwise null.</summary>
    public static async Task<object?> KeyExchangeErrorAsync(
        int keyScopeId,
        IReadOnlyList<string> producerIds,
        IKeyRepository keys,
        ICiphertextRepository ciphertexts)
    {
        var registeredKeyIds = (await keys.GetAllKeysAsync(keyScopeId)).Select(k => k.ProducerId).ToHashSet();
        var missingKeys = producerIds.Except(registeredKeyIds).ToList();
        if (missingKeys.Count > 0)
            return new { error = "Key exchange incomplete: missing public keys", missingPublicKeys = missingKeys };

        var n = producerIds.Count;
        var expectedCiphertexts = n * (n - 1) / 2;
        var actualCiphertexts = await ciphertexts.CountForPartnersAsync(keyScopeId, producerIds.ToList());
        if (actualCiphertexts < expectedCiphertexts)
            return new { error = "Key exchange incomplete: not all ciphertexts posted", expectedCiphertexts, actualCiphertexts };

        return null;
    }
}

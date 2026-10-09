using AIRoundTableSecretSharingAPI.Repositories;
using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingAPI.Services;

public static class EpochLifecycle
{
    public const int MinParticipants = 3;

    public static async Task CloseIfCompleteAsync(
        ProducerEpoch epoch,
        ISubmissionRepository submissions,
        IProducerRepository producers)
    {
        if (epoch.IsClosed || epoch.IsCancelled)
            return;

        var rows = await submissions.GetSubmissionsByEpochAsync(epoch.EpochId);
        if (!EpochGrid.IsFullySubmitted(epoch, rows))
            return;

        await producers.CloseEpochAsync(epoch.EpochId);
        epoch.IsClosed = true;
    }

    public static async Task CompleteQuorumIfAnsweredAsync(
        ProducerEpoch epoch,
        IQuorumRepository quorum,
        IProducerRepository producers)
    {
        if (epoch.QuorumComplete || epoch.IsCancelled)
            return;

        var responses = await quorum.GetByEpochAsync(epoch.EpochId);
        if (!QuorumGrid.IsComplete(epoch, responses))
            return;

        await producers.MarkQuorumCompleteAsync(epoch.EpochId);
        epoch.QuorumComplete = true;
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

    /// <summary>Returns an error payload when key exchange is not finished, otherwise null.</summary>
    public static async Task<object?> KeyExchangeErrorAsync(
        ProducerEpoch epoch,
        IKeyRepository keys,
        ICiphertextRepository ciphertexts)
    {
        var registeredKeyIds = (await keys.GetAllKeysAsync(epoch.EpochId)).Select(k => k.ProducerId).ToHashSet();
        var missingKeys = epoch.ProducerIds.Except(registeredKeyIds).ToList();
        if (missingKeys.Count > 0)
            return new { error = "Key exchange incomplete: missing public keys", missingPublicKeys = missingKeys };

        var n = epoch.ProducerIds.Count;
        var expectedCiphertexts = n * (n - 1) / 2;
        var actualCiphertexts = await ciphertexts.CountForPartnersAsync(epoch.EpochId, epoch.ProducerIds);
        if (actualCiphertexts < expectedCiphertexts)
            return new { error = "Key exchange incomplete: not all ciphertexts posted", expectedCiphertexts, actualCiphertexts };

        return null;
    }
}

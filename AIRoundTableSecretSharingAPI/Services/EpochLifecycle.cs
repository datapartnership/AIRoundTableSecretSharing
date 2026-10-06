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
        if (epoch.IsClosed)
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
        if (epoch.QuorumComplete)
            return;

        var responses = await quorum.GetByEpochAsync(epoch.EpochId);
        if (!QuorumGrid.IsComplete(epoch, responses))
            return;

        await producers.MarkQuorumCompleteAsync(epoch.EpochId);
        epoch.QuorumComplete = true;
    }
}

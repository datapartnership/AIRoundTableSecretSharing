using AIRoundTableSecretSharingAPI.Repositories;
using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingAPI.Services;

public static class EpochLifecycle
{
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
}

using AIRoundTableSecretSharingAPI.Repositories;
using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingAPI.Services;

/// <summary>
/// A set of partners that runs one ML-KEM key exchange: an epoch (for masked data) or a Quorum Check period
/// (for masked answers). Keys and ciphertexts are stored under <see cref="Id"/>: the epoch id, or the negated
/// period id.
/// </summary>
public sealed record KeyScope(int Id, string Label, List<string> ProducerIds, object? CancelledError)
{
    public static async Task<KeyScope?> ResolveAsync(
        int? epochId,
        int? periodId,
        IProducerRepository producers,
        IQuorumRepository quorum)
    {
        if (epochId is > 0 && periodId is null or 0)
        {
            var epoch = await producers.GetEpochByIdAsync(epochId.Value);
            return epoch == null
                ? null
                : new KeyScope(epoch.EpochId, $"epoch {epoch.EpochId}", epoch.ProducerIds, EpochLifecycle.CancelledError(epoch));
        }

        if (periodId is > 0 && epochId is null or 0)
        {
            var period = await quorum.GetPeriodAsync(periodId.Value);
            return period == null
                ? null
                : new KeyScope(period.KeyScopeId, $"Quorum Check period {period.PeriodId}", period.ProducerIds, EpochLifecycle.CancelledError(period));
        }

        return null;
    }
}

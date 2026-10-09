using AIRoundTableSecretSharingAPI.Models;
using AIRoundTableSecretSharingAPI.Repositories;
using AIRoundTableSecretSharingAPI.Data;
using AIRoundTableSecretSharingCommon.Core;
using AIRoundTableSecretSharingCommon.Models;
using AIRoundTableSecretSharingAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIRoundTableSecretSharingAPI.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IProducerRepository _producerRepo;
    private readonly ISubmissionRepository _submissionRepo;
    private readonly IQuorumRepository _quorumRepo;
    private readonly IKeyRepository _keyRepo;
    private readonly ICiphertextRepository _ciphertextRepo;
    private readonly IClientCredentialService _credentialService;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        AppDbContext db,
        IProducerRepository producerRepo,
        ISubmissionRepository submissionRepo,
        IQuorumRepository quorumRepo,
        IKeyRepository keyRepo,
        ICiphertextRepository ciphertextRepo,
        IClientCredentialService credentialService,
        ILogger<AdminController> logger)
    {
        _db = db;
        _producerRepo = producerRepo;
        _submissionRepo = submissionRepo;
        _quorumRepo = quorumRepo;
        _keyRepo = keyRepo;
        _ciphertextRepo = ciphertextRepo;
        _credentialService = credentialService;
        _logger = logger;
    }

    /// <summary>
    /// Wipes all data and re-seeds the database to its initial state:
    /// partnerA, partnerB, partnerC with epoch 1 starting 2025-01-01.
    /// </summary>
    [HttpPost("reset")]
    [ProducesResponseType(typeof(MessageResponse), 200)]
    public async Task<ActionResult<MessageResponse>> ResetDatabase()
    {
        _logger.LogWarning("Admin database reset initiated by {User}.", User.Identity?.Name ?? "unknown");

        await _submissionRepo.ClearAllAsync();
        await _quorumRepo.ClearAllAsync();
        await _ciphertextRepo.ClearAsync();
        await _keyRepo.ClearAsync();
        await _producerRepo.ClearAllAsync();
        await _credentialService.ResetToConfiguredCredentialsAsync(HttpContext.RequestAborted);

        _logger.LogInformation("Database reset complete. All data cleared.");

        return Ok(new MessageResponse { Message = "Database cleared." });
    }

    /// <summary>
    /// Replaces the active epoch with a new one for the submitted producers.
    /// Previous epochs and their data are kept; keys and ciphertexts are scoped per epoch,
    /// so the new round runs a fresh key exchange.
    /// </summary>
    [HttpPost("producers/reset-and-create-epoch")]
    [ProducesResponseType(typeof(ReplaceProducersResponse), 200)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<ReplaceProducersResponse>> ResetAndCreateEpoch([FromBody] ReplaceProducersRequest request)
    {
        if (request.Producers == null || request.Producers.Count < EpochLifecycle.MinParticipants)
            return BadRequest(new { error = $"At least {EpochLifecycle.MinParticipants} producers are required." });

        if (!TryResolveEpochStartDate(request.StartMonth, out var startDate))
            return BadRequest(new { error = "startMonth must be in YYYY-MM format (e.g. 2025-01)." });

        var normalizedProducers = request.Producers
            .Select(p => new ReplaceProducerItem
            {
                ProducerId = p.ProducerId.Trim(),
                DisplayName = p.DisplayName.Trim()
            })
            .ToList();

        if (normalizedProducers.Any(p => string.IsNullOrWhiteSpace(p.ProducerId) || string.IsNullOrWhiteSpace(p.DisplayName)))
            return BadRequest(new { error = "Each producer must include non-empty producerId and displayName." });

        var duplicateIds = normalizedProducers
            .GroupBy(p => p.ProducerId, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .OrderBy(x => x)
            .ToList();

        if (duplicateIds.Count > 0)
            return BadRequest(new { error = "Duplicate producerId values are not allowed.", duplicateProducerIds = duplicateIds });

        var callerId = User.GetOid();
        if (callerId != null && normalizedProducers.Any(p => p.ProducerId == callerId))
            return BadRequest(new { error = "Admins cannot be epoch participants." });

        var unregisteredIds = await UnregisteredProducerIdsAsync(normalizedProducers.Select(p => p.ProducerId).ToList());
        if (unregisteredIds.Count > 0)
        {
            return BadRequest(new
            {
                error = "Every producer must be an active registered partner.",
                unregisteredProducerIds = unregisteredIds
            });
        }

        _logger.LogWarning(
            "Admin producer replacement initiated by {User}. New producer count: {Count}",
            User.Identity?.Name ?? "unknown",
            normalizedProducers.Count);

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            foreach (var item in normalizedProducers)
            {
                await _producerRepo.UpsertProducerAsync(new ProducerInfo
                {
                    ProducerId = item.ProducerId,
                    DisplayName = item.DisplayName,
                    JoinedDate = startDate,
                    IsActive = true
                });
            }

            var sortedProducerIds = normalizedProducers
                .Select(p => p.ProducerId)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            var epoch = await NewEpochAsync(startDate, sortedProducerIds);
            await _producerRepo.CreateEpochAsync(epoch);
            await tx.CommitAsync();

            _logger.LogInformation(
                "Created epoch {EpochId} effective {StartDate} with {Count} producers. Previous epochs kept.",
                epoch.EpochId,
                epoch.StartDate,
                epoch.ProducerCount);

            return Ok(new ReplaceProducersResponse
            {
                Message = "New epoch created. Previous epoch data was kept.",
                Epoch = epoch,
                Producers = sortedProducerIds
            });
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Cancels an epoch. Partners still see it, marked as cancelled, but no further key exchange,
    /// quorum answers or submissions are accepted. Its data is kept.
    /// </summary>
    [HttpPost("epochs/{epochId:int}/cancel")]
    [ProducesResponseType(typeof(EpochSummary), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<ActionResult<EpochSummary>> CancelEpoch(int epochId, [FromBody] CancelEpochRequest? request)
    {
        var epoch = await _producerRepo.GetEpochByIdAsync(epochId);
        if (epoch == null)
            return NotFound(new { error = "Epoch not found" });
        if (epoch.IsCancelled)
            return Conflict(new { error = "This epoch is already cancelled.", code = "already-cancelled" });
        if (epoch.IsClosed)
            return Conflict(new { error = "A closed epoch cannot be cancelled.", code = "epoch-closed" });

        await _producerRepo.CancelEpochAsync(epochId, NormalizeReason(request?.Reason), null);

        _logger.LogWarning("Epoch {EpochId} cancelled by {User}", epochId, User.Identity?.Name ?? "unknown");
        return Ok(ToSummary((await _producerRepo.GetEpochByIdAsync(epochId))!));
    }

    /// <summary>
    /// Cancels an epoch and creates a replacement with the same partners and start month,
    /// so key exchange and the Quorum Check start over.
    /// </summary>
    [HttpPost("epochs/{epochId:int}/recreate")]
    [ProducesResponseType(typeof(ReplaceProducersResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<ActionResult<ReplaceProducersResponse>> RecreateEpoch(int epochId, [FromBody] CancelEpochRequest? request)
    {
        var old = await _producerRepo.GetEpochByIdAsync(epochId);
        if (old == null)
            return NotFound(new { error = "Epoch not found" });
        if (old.ReplacedByEpochId is { } existing)
            return Conflict(new { error = $"This epoch was already replaced by epoch {existing}.", code = "already-replaced" });
        if (old.IsClosed)
            return Conflict(new { error = "A closed epoch cannot be recreated.", code = "epoch-closed" });

        var unregisteredIds = await UnregisteredProducerIdsAsync(old.ProducerIds);
        if (unregisteredIds.Count > 0)
        {
            return BadRequest(new
            {
                error = "Every producer must still be an active registered partner. Create a new epoch with the current partners instead.",
                unregisteredProducerIds = unregisteredIds
            });
        }

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var epoch = await NewEpochAsync(old.StartDate, old.ProducerIds.OrderBy(id => id, StringComparer.Ordinal).ToList());
            // Keep the replaced epoch's place in the timeline instead of ending whichever epoch is current
            epoch.EndDate = old.EndDate;
            await _producerRepo.AddEpochAsync(epoch);
            await _producerRepo.CancelEpochAsync(old.EpochId, NormalizeReason(request?.Reason), epoch.EpochId);
            await tx.CommitAsync();

            _logger.LogWarning(
                "Epoch {OldEpochId} recreated as epoch {EpochId} by {User}",
                old.EpochId, epoch.EpochId, User.Identity?.Name ?? "unknown");

            return Ok(new ReplaceProducersResponse
            {
                Message = $"Epoch {old.EpochId} was cancelled and replaced by epoch {epoch.EpochId}.",
                Epoch = epoch,
                Producers = epoch.ProducerIds
            });
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private async Task<ProducerEpoch> NewEpochAsync(DateTime startDate, List<string> sortedProducerIds)
    {
        var existingEpochIds = (await _producerRepo.GetAllEpochsAsync()).Select(e => e.EpochId);
        return new ProducerEpoch
        {
            EpochId = existingEpochIds.DefaultIfEmpty(0).Max() + 1,
            StartDate = startDate,
            EndDate = null,
            ProducerIds = sortedProducerIds,
            ProducerCount = sortedProducerIds.Count,
            IsClosed = false,
            QuorumComplete = false
        };
    }

    // Admins are refused at self-registration, so requiring an active registration excludes them
    private async Task<List<string>> UnregisteredProducerIdsAsync(List<string> requestedIds)
    {
        var activeIds = (await _producerRepo.GetProducersByIdsAsync(requestedIds))
            .Where(p => p.IsActive)
            .Select(p => p.ProducerId)
            .ToHashSet(StringComparer.Ordinal);
        return requestedIds.Where(id => !activeIds.Contains(id)).OrderBy(id => id).ToList();
    }

    private static string? NormalizeReason(string? reason)
    {
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        return trimmed.Length > 500 ? trimmed[..500] : trimmed;
    }

    private static EpochSummary ToSummary(ProducerEpoch e) => new()
    {
        EpochId = e.EpochId,
        StartDate = e.StartDate,
        EndDate = e.EndDate,
        ProducerCount = e.ProducerCount,
        IsClosed = e.IsClosed,
        QuorumComplete = e.QuorumComplete,
        CancelledAt = e.CancelledAt,
        CancelReason = e.CancelReason,
        ReplacedByEpochId = e.ReplacedByEpochId
    };

    private static DateTime FirstDayOfNextMonthUtc()
    {
        // Note: This is used for AddProducer endpoint which normalizes to next month.
        // ResetAndCreateEpoch resolves the epoch to the first day of the requested month.
        var now = DateTime.UtcNow;
        return new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
    }

    private static bool TryResolveEpochStartDate(string? startMonth, out DateTime startDate)
    {
        if (string.IsNullOrWhiteSpace(startMonth))
        {
            var now = DateTime.UtcNow;
            startDate = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            return true;
        }

        if (DateTime.TryParseExact(
                startMonth,
                "yyyy-MM",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var parsedMonth))
        {
            startDate = new DateTime(parsedMonth.Year, parsedMonth.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            return true;
        }

        startDate = default;
        return false;
    }

    /// <summary>
    /// Returns every epoch, newest first.
    /// </summary>
    [HttpGet("epochs")]
    [ProducesResponseType(typeof(EpochListResponse), 200)]
    public async Task<ActionResult<EpochListResponse>> GetEpochs()
    {
        var epochs = await _producerRepo.GetAllEpochsAsync();
        return Ok(new EpochListResponse
        {
            Epochs = epochs.Select(ToSummary).ToList()
        });
    }

    /// <summary>
    /// Epoch detail: missing submitters always, aggregates only when every required cell is present.
    /// </summary>
    [HttpGet("epochs/{epochId:int}")]
    [ProducesResponseType(typeof(EpochDetailResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<EpochDetailResponse>> GetEpochDetail(int epochId)
    {
        var epoch = await _producerRepo.GetEpochByIdAsync(epochId);
        if (epoch == null)
            return NotFound(new { error = "Epoch not found" });

        await EpochLifecycle.CompleteQuorumIfAnsweredAsync(epoch, _quorumRepo, _producerRepo);
        await EpochLifecycle.CloseIfCompleteAsync(epoch, _submissionRepo, _producerRepo);
        return Ok(await BuildEpochDetailAsync(epoch));
    }

    /// <summary>
    /// Returns country/month aggregates for the latest active epoch.
    /// Aggregates are omitted when any required submission is missing.
    /// </summary>
    [HttpGet("aggregates-latest-epoch")]
    [ProducesResponseType(typeof(EpochDetailResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<EpochDetailResponse>> GetLatestEpochAggregates()
    {
        var epoch = await _producerRepo.GetEpochForDateAsync(DateTime.UtcNow);
        if (epoch == null)
        {
            _logger.LogWarning("No active epoch found for current date.");
            return NotFound(new { error = "No active epoch found" });
        }

        await EpochLifecycle.CompleteQuorumIfAnsweredAsync(epoch, _quorumRepo, _producerRepo);
        await EpochLifecycle.CloseIfCompleteAsync(epoch, _submissionRepo, _producerRepo);
        return Ok(await BuildEpochDetailAsync(epoch));
    }

    private async Task<EpochDetailResponse> BuildEpochDetailAsync(ProducerEpoch epoch)
    {
        var submissions = await _submissionRepo.GetSubmissionsByEpochAsync(epoch.EpochId);
        var missingCells = EpochGrid.MissingCells(epoch, submissions);
        var names = (await _producerRepo.GetProducersByIdsAsync(epoch.ProducerIds))
            .ToDictionary(p => p.ProducerId, p => p.DisplayName, StringComparer.Ordinal);

        string NameOf(string id) => names.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n) ? n : id;

        var partners = epoch.ProducerIds
            .Select(id => new EpochPartnerInfo { ProducerId = id, DisplayName = NameOf(id) })
            .ToList();

        var missingProducers = missingCells
            .GroupBy(m => m.ProducerId)
            .Select(g => new MissingProducerStatus
            {
                ProducerId = g.Key,
                DisplayName = NameOf(g.Key),
                MissingCells = g.Select(c => new SubmittedEntry
                {
                    Country = c.Country,
                    Month = c.Month,
                    Indicator = c.Indicator,
                    Segment = c.Segment
                }).ToList()
            })
            .ToList();

        var quorumResponses = await _quorumRepo.GetByEpochAsync(epoch.EpochId);
        var quorumCells = QuorumGrid.Evaluate(epoch, quorumResponses);
        var ignoredKeys = epoch.QuorumComplete
            ? quorumCells.Where(c => c.Ignored).Select(c => EpochGrid.CellKey(c.Country, c.Month, c.Indicator, c.Segment)).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        var answered = quorumResponses
            .GroupBy(r => r.ProducerId)
            .ToDictionary(g => g.Key, g => g.Select(r => EpochGrid.CellKey(r.Country, r.Month, r.Indicator, r.Segment)).Distinct().Count());

        var aggregates = new List<AggregationResult>();
        if (missingCells.Count == 0 && epoch.ProducerIds.Count > 0)
        {
            foreach (var country in EpochGrid.Countries)
            {
                foreach (var month in EpochGrid.Months(epoch.StartDate))
                {
                    foreach (var (indicator, segment) in EpochGrid.Series)
                    {
                        // Cells below quorum are never aggregated
                        if (ignoredKeys.Contains(EpochGrid.CellKey(country, month, indicator, segment)))
                            continue;
                        var cell = submissions
                            .Where(s => s.Country == country && s.Month == month
                                        && s.Indicator == indicator && s.Segment == segment)
                            .ToList();
                        aggregates.Add(new AggregationResult
                        {
                            Status = "complete",
                            Country = country,
                            Month = month,
                            Indicator = indicator,
                            Segment = segment,
                            Total = SecureNoiseGenerator.SumMasked(cell.Select(s => s.Value)).ToString(System.Globalization.CultureInfo.InvariantCulture),
                            SubmissionCount = cell.Count,
                            ExpectedSubmissions = epoch.ProducerCount,
                            MissingProducers = new List<string>()
                        });
                    }
                }
            }
        }

        _logger.LogInformation(
            "Epoch {EpochId} detail: missing {MissingCount} cells from {MissingProducers} producers; aggregates={HasAggregates}",
            epoch.EpochId, missingCells.Count, missingProducers.Count, aggregates.Count > 0);

        return new EpochDetailResponse
        {
            EpochId = epoch.EpochId,
            StartDate = epoch.StartDate,
            EndDate = epoch.EndDate,
            PartnerCount = epoch.ProducerCount,
            IsClosed = epoch.IsClosed,
            QuorumComplete = epoch.QuorumComplete,
            CancelledAt = epoch.CancelledAt,
            CancelReason = epoch.CancelReason,
            ReplacedByEpochId = epoch.ReplacedByEpochId,
            Partners = partners,
            QuorumPartners = epoch.ProducerIds.Select(id =>
            {
                var n = answered.GetValueOrDefault(id);
                return new QuorumPartnerStatus
                {
                    ProducerId = id,
                    DisplayName = NameOf(id),
                    AnsweredCells = n,
                    Done = n >= EpochGrid.CellCount
                };
            }).ToList(),
            QuorumCells = quorumCells.Select(c => new QuorumCellInfo
            {
                Country = c.Country,
                Month = c.Month,
                Indicator = c.Indicator,
                Segment = c.Segment,
                ParticipantCount = c.ParticipantCount,
                Ignored = c.Ignored,
                Signature = c.Signature
            }).ToList(),
            MissingProducers = missingProducers,
            Aggregates = aggregates
        };
    }
}

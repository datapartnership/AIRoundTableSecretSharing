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
    /// Creates the period's next quarterly epoch, with the period's partners. Epochs are created in order, once the
    /// Quorum Check is complete, and only when no other epoch is open. Previous epochs and their data are kept;
    /// keys and ciphertexts are scoped per epoch, so the new epoch runs a fresh key exchange.
    /// </summary>
    [HttpPost("quorum-periods/{periodId:int}/next-epoch")]
    [ProducesResponseType(typeof(ReplaceProducersResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<ActionResult<ReplaceProducersResponse>> CreateNextEpoch(int periodId)
    {
        var period = await _quorumRepo.GetPeriodAsync(periodId);
        if (period == null)
            return NotFound(new { error = "Quorum Check period not found" });
        await EpochLifecycle.CompletePeriodIfAnsweredAsync(period, _quorumRepo);

        var epochsOfPeriod = (await _producerRepo.GetAllEpochsAsync()).Where(e => e.QuorumPeriodId == periodId).ToList();
        var openEpoch = await EpochLifecycle.OpenEpochAsync(_producerRepo, _submissionRepo, _quorumRepo);
        switch (EpochLifecycle.NextEpochBlocker(period, epochsOfPeriod, openEpoch))
        {
            case "period-cancelled":
                return Conflict(new { error = $"Quorum Check period {periodId} was cancelled.", code = "period-cancelled" });
            case "quorum-incomplete":
                return Conflict(new { error = $"Quorum Check period {periodId} is still answering. Epochs can be created once every partner has answered.", code = "quorum-incomplete" });
            case "period-full":
                return Conflict(new { error = $"Every quarter of Quorum Check period {periodId} already has an epoch.", code = "period-full" });
            case "epoch-open":
                return Conflict(new
                {
                    error = $"Epoch {openEpoch!.EpochId} is still open. It closes when every partner has submitted; cancel it to move on.",
                    code = "epoch-open",
                    epochId = openEpoch.EpochId
                });
        }

        var unregisteredIds = await UnregisteredProducerIdsAsync(period.ProducerIds);
        if (unregisteredIds.Count > 0)
        {
            return BadRequest(new
            {
                error = "Every producer must still be an active registered partner.",
                unregisteredProducerIds = unregisteredIds
            });
        }

        var slot = period.NextSlot(epochsOfPeriod)!.Value;
        var producerIds = period.ProducerIds.OrderBy(id => id, StringComparer.Ordinal).ToList();
        var epoch = await NewEpochAsync(period.SlotStart(slot), producerIds, period.UsageType, period.PeriodId);
        // Ends the previous epoch of the usage type in the timeline; it is already closed or cancelled
        await _producerRepo.CreateEpochAsync(epoch);

        _logger.LogInformation(
            "Created {UsageType} epoch {EpochId} (Q{Quarter} of period {PeriodId}) effective {StartDate} with {Count} producers",
            epoch.UsageType, epoch.EpochId, slot + 1, period.PeriodId, epoch.StartDate, epoch.ProducerCount);

        return Ok(new ReplaceProducersResponse
        {
            Message = "New epoch created. Previous epoch data was kept.",
            Epoch = epoch,
            Producers = producerIds
        });
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
        return Ok(await ToSummaryAsync((await _producerRepo.GetEpochByIdAsync(epochId))!));
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
        // An open epoch is cancelled by its own recreation; a cancelled one may only come back while nothing else is open
        if (old.IsCancelled && await EpochLifecycle.OpenEpochAsync(_producerRepo, _submissionRepo, _quorumRepo) is { } open)
        {
            return Conflict(new
            {
                error = $"Epoch {open.EpochId} is still open. Only one epoch can be open at a time.",
                code = "epoch-open",
                epochId = open.EpochId
            });
        }

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
            var epoch = await NewEpochAsync(
                old.StartDate, old.ProducerIds.OrderBy(id => id, StringComparer.Ordinal).ToList(), old.UsageType, old.QuorumPeriodId);
            // Keep the replaced epoch's place in the timeline instead of ending whichever epoch is current
            epoch.EndDate = old.EndDate;
            epoch.MonthCount = old.MonthCount;
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

    private async Task<ProducerEpoch> NewEpochAsync(DateTime startDate, List<string> sortedProducerIds, string usageType, int? periodId)
    {
        var existingEpochIds = (await _producerRepo.GetAllEpochsAsync()).Select(e => e.EpochId);
        return new ProducerEpoch
        {
            EpochId = existingEpochIds.DefaultIfEmpty(0).Max() + 1,
            UsageType = usageType,
            QuorumPeriodId = periodId,
            StartDate = startDate,
            EndDate = null,
            ProducerIds = sortedProducerIds,
            ProducerCount = sortedProducerIds.Count,
            IsClosed = false
        };
    }

    /// <summary>
    /// Opens a Quorum Check period: one check covering every epoch of a usage type for 12 months.
    /// Its partners are the partners of every epoch created in it. Only one period can be unfinished at a time.
    /// </summary>
    [HttpPost("quorum-periods")]
    [ProducesResponseType(typeof(QuorumPeriodSummary), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public async Task<ActionResult<QuorumPeriodSummary>> CreateQuorumPeriod([FromBody] CreateQuorumPeriodRequest request)
    {
        if (!UsageTypes.IsValid(request.UsageType))
            return BadRequest(new { error = "usageType must be api or chat." });
        if (!TryResolveEpochStartDate(request.StartMonth, out var startDate))
            return BadRequest(new { error = "startMonth must be in YYYY-MM format (e.g. 2025-01)." });

        var ids = (request.Producers ?? new())
            .Select(p => p.ProducerId.Trim())
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        if (ids.Count < EpochLifecycle.MinParticipants)
            return BadRequest(new { error = $"At least {EpochLifecycle.MinParticipants} producers are required." });

        var callerId = User.GetOid();
        if (callerId != null && ids.Contains(callerId))
            return BadRequest(new { error = "Admins cannot be participants." });

        var unregisteredIds = await UnregisteredProducerIdsAsync(ids);
        if (unregisteredIds.Count > 0)
            return BadRequest(new { error = "Every producer must be an active registered partner.", unregisteredProducerIds = unregisteredIds });

        // Partners answer one Quorum Check at a time, across usage types
        if (await EpochLifecycle.ActivePeriodAsync(_quorumRepo) is { } active)
        {
            return Conflict(new
            {
                error = $"Quorum Check period {active.PeriodId} ({active.UsageType}) is still answering. Cancel it or wait until every partner has answered.",
                code = "period-active",
                periodId = active.PeriodId
            });
        }

        var periods = await _quorumRepo.GetAllPeriodsAsync();
        var end = startDate.AddMonths(QuorumPeriod.MonthCount);
        var overlap = periods.FirstOrDefault(p => p.UsageType == request.UsageType && !p.IsCancelled
                                                  && p.StartDate < end && startDate < p.EndDate);
        if (overlap != null)
        {
            return BadRequest(new
            {
                error = $"Quorum Check period {overlap.PeriodId} already covers part of these months. Cancel it first.",
                code = "period-overlap"
            });
        }

        var period = new QuorumPeriod
        {
            PeriodId = periods.Select(p => p.PeriodId).DefaultIfEmpty(0).Max() + 1,
            UsageType = request.UsageType,
            StartDate = startDate,
            ProducerIds = ids,
            CreatedAt = DateTime.UtcNow
        };
        await _quorumRepo.AddPeriodAsync(period);

        _logger.LogInformation(
            "Created {UsageType} Quorum Check period {PeriodId} from {StartDate} with {Count} producers",
            period.UsageType, period.PeriodId, period.StartDate, ids.Count);
        return Ok(new QuorumPeriodSummary(period));
    }

    [HttpGet("quorum-periods")]
    [ProducesResponseType(typeof(QuorumPeriodListResponse), 200)]
    public async Task<ActionResult<QuorumPeriodListResponse>> GetQuorumPeriods()
    {
        var periods = await _quorumRepo.GetAllPeriodsAsync();
        foreach (var p in periods)
            await EpochLifecycle.CompletePeriodIfAnsweredAsync(p, _quorumRepo);
        var epochs = await _producerRepo.GetAllEpochsAsync();
        var openEpoch = await EpochLifecycle.OpenEpochAsync(_producerRepo, _submissionRepo, _quorumRepo);
        return Ok(new QuorumPeriodListResponse
        {
            Periods = periods
                .Select(p => new QuorumPeriodSummary(p).WithNextEpoch(p, epochs.Where(e => e.QuorumPeriodId == p.PeriodId).ToList(), openEpoch))
                .ToList()
        });
    }

    /// <summary>
    /// Cancels a Quorum Check period. Its epochs keep the answers already given; no new epochs can be created in it.
    /// </summary>
    [HttpPost("quorum-periods/{periodId:int}/cancel")]
    [ProducesResponseType(typeof(QuorumPeriodSummary), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<ActionResult<QuorumPeriodSummary>> CancelQuorumPeriod(int periodId, [FromBody] CancelEpochRequest? request)
    {
        var period = await _quorumRepo.GetPeriodAsync(periodId);
        if (period == null)
            return NotFound(new { error = "Quorum Check period not found" });
        if (period.IsCancelled)
            return Conflict(new { error = "This period is already cancelled.", code = "already-cancelled" });

        await _quorumRepo.CancelPeriodAsync(periodId, NormalizeReason(request?.Reason));
        _logger.LogWarning("Quorum Check period {PeriodId} cancelled by {User}", periodId, User.Identity?.Name ?? "unknown");
        return Ok(new QuorumPeriodSummary((await _quorumRepo.GetPeriodAsync(periodId))!));
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

    private async Task<EpochSummary> ToSummaryAsync(ProducerEpoch e) => new()
    {
        EpochId = e.EpochId,
        UsageType = e.UsageType,
        QuorumPeriodId = e.QuorumPeriodId,
        StartDate = e.StartDate,
        EndDate = e.EndDate,
        ProducerCount = e.ProducerCount,
        IsClosed = e.IsClosed,
        QuorumComplete = await EpochLifecycle.IsQuorumCompleteAsync(e, _quorumRepo),
        CancelledAt = e.CancelledAt,
        CancelReason = e.CancelReason,
        ReplacedByEpochId = e.ReplacedByEpochId,
        MaskBits = e.MaskBits,
        MonthCount = e.MonthCount
    };

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
        var summaries = new List<EpochSummary>();
        foreach (var e in epochs)
            summaries.Add(await ToSummaryAsync(e));
        return Ok(new EpochListResponse { Epochs = summaries });
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

        await EpochLifecycle.CloseIfCompleteAsync(epoch, _submissionRepo, _producerRepo, _quorumRepo);
        return Ok(await BuildEpochDetailAsync(epoch));
    }

    /// <summary>
    /// Returns country/month aggregates for the latest active epoch.
    /// Aggregates are omitted when any required submission is missing.
    /// </summary>
    [HttpGet("aggregates-latest-epoch")]
    [ProducesResponseType(typeof(EpochDetailResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<EpochDetailResponse>> GetLatestEpochAggregates([FromQuery] string usageType = UsageTypes.Api)
    {
        var epoch = await _producerRepo.GetEpochForDateAsync(DateTime.UtcNow, usageType);
        if (epoch == null)
        {
            _logger.LogWarning("No active epoch found for current date.");
            return NotFound(new { error = "No active epoch found" });
        }

        await EpochLifecycle.CloseIfCompleteAsync(epoch, _submissionRepo, _producerRepo, _quorumRepo);
        return Ok(await BuildEpochDetailAsync(epoch));
    }

    private async Task<EpochDetailResponse> BuildEpochDetailAsync(ProducerEpoch epoch)
    {
        var submissions = await _submissionRepo.GetSubmissionsByEpochAsync(epoch.EpochId);
        var period = await EpochLifecycle.PeriodOfAsync(epoch, _quorumRepo);
        if (period != null)
            await EpochLifecycle.CompletePeriodIfAnsweredAsync(period, _quorumRepo);
        var quorumAnswers = period != null ? await _quorumRepo.GetAnswersAsync(period.PeriodId) : new List<QuorumAnswer>();
        var quorumCells = period != null ? QuorumGrid.Evaluate(period, quorumAnswers) : new List<QuorumCellResult>();
        var quorumComplete = period?.QuorumComplete ?? true;
        var ignoredKeys = quorumComplete
            ? QuorumGrid.IgnoredCellKeys(epoch, quorumCells)
            : new HashSet<string>(StringComparer.Ordinal);
        // Cells below quorum get no data, so they are never missing
        var missingCells = EpochGrid.MissingCells(epoch, submissions, ignoredKeys);
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

        var answered = period != null
            ? QuorumGrid.AnsweredCounts(period, quorumAnswers)
            : new Dictionary<string, int>();

        var aggregates = new List<AggregationResult>();
        if (quorumComplete && missingCells.Count == 0 && epoch.ProducerIds.Count > 0)
        {
            foreach (var country in EpochGrid.Countries)
            {
                foreach (var month in EpochGrid.Months(epoch))
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
                            Total = SecureNoiseGenerator.SumMasked(cell.Select(s => s.Value), epoch.MaskBits).ToString(System.Globalization.CultureInfo.InvariantCulture),
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
            UsageType = epoch.UsageType,
            QuorumPeriod = period != null ? new QuorumPeriodSummary(period) : null,
            StartDate = epoch.StartDate,
            EndDate = epoch.EndDate,
            PartnerCount = epoch.ProducerCount,
            IsClosed = epoch.IsClosed,
            QuorumComplete = quorumComplete,
            CancelledAt = epoch.CancelledAt,
            CancelReason = epoch.CancelReason,
            ReplacedByEpochId = epoch.ReplacedByEpochId,
            MaskBits = epoch.MaskBits,
            MonthCount = epoch.MonthCount,
            Partners = partners,
            QuorumPartners = period == null ? new() : period.ProducerIds.Select(id =>
            {
                var n = answered.GetValueOrDefault(id);
                return new QuorumPartnerStatus
                {
                    ProducerId = id,
                    DisplayName = NameOf(id),
                    AnsweredMetrics = n,
                    Done = n >= QuorumGrid.CellCount
                };
            }).ToList(),
            QuorumCells = quorumCells.Select(c => new QuorumCellInfo
            {
                Country = c.Country,
                Indicator = c.Indicator,
                ParticipantCount = c.ParticipantCount,
                Ignored = c.Ignored,
                Signature = c.Signature,
                Unverified = c.Unverified
            }).ToList(),
            MissingProducers = missingProducers,
            Aggregates = aggregates
        };
    }
}

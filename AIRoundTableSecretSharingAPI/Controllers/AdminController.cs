using AIRoundTableSecretSharingAPI.Models;
using AIRoundTableSecretSharingAPI.Repositories;
using AIRoundTableSecretSharingAPI.Data;
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
    private readonly IKeyRepository _keyRepo;
    private readonly ICiphertextRepository _ciphertextRepo;
    private readonly IClientCredentialService _credentialService;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        AppDbContext db,
        IProducerRepository producerRepo,
        ISubmissionRepository submissionRepo,
        IKeyRepository keyRepo,
        ICiphertextRepository ciphertextRepo,
        IClientCredentialService credentialService,
        ILogger<AdminController> logger)
    {
        _db = db;
        _producerRepo = producerRepo;
        _submissionRepo = submissionRepo;
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
        await _ciphertextRepo.ClearAsync();
        await _keyRepo.ClearAsync();
        await _producerRepo.ClearAllAsync();
        await _credentialService.ResetToConfiguredCredentialsAsync(HttpContext.RequestAborted);

        _logger.LogInformation("Database reset complete. All data cleared.");

        return Ok(new MessageResponse { Message = "Database cleared." });
    }

    /// <summary>
    /// Replaces the active epoch with a new one for the submitted producers.
    /// Previous epochs and their submissions are kept. Keys and ciphertexts are cleared
    /// so the new round can run key exchange.
    /// </summary>
    [HttpPost("producers/reset-and-create-epoch")]
    [ProducesResponseType(typeof(ReplaceProducersResponse), 200)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<ReplaceProducersResponse>> ResetAndCreateEpoch([FromBody] ReplaceProducersRequest request)
    {
        if (request.Producers == null || request.Producers.Count < 2)
            return BadRequest(new { error = "At least 2 producers are required." });

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

        _logger.LogWarning(
            "Admin producer replacement initiated by {User}. New producer count: {Count}",
            User.Identity?.Name ?? "unknown",
            normalizedProducers.Count);

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            await _ciphertextRepo.ClearAsync();
            await _keyRepo.ClearAsync();

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

            var epoch = new ProducerEpoch
            {
                EpochId = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                StartDate = startDate,
                EndDate = null,
                ProducerIds = sortedProducerIds,
                ProducerCount = sortedProducerIds.Count,
                IsClosed = false
            };

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
            Epochs = epochs.Select(e => new EpochSummary
            {
                EpochId = e.EpochId,
                StartDate = e.StartDate,
                EndDate = e.EndDate,
                ProducerCount = e.ProducerCount,
                IsClosed = e.IsClosed
            }).ToList()
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
                MissingCells = g.Select(c => new SubmittedEntry { Country = c.Country, Month = c.Month }).ToList()
            })
            .ToList();

        var aggregates = new List<AggregationResult>();
        if (missingCells.Count == 0 && epoch.ProducerIds.Count > 0)
        {
            foreach (var country in EpochGrid.Countries)
            {
                foreach (var month in EpochGrid.Months(epoch.StartDate))
                {
                    var cell = submissions.Where(s => s.Country == country && s.Month == month).ToList();
                    aggregates.Add(new AggregationResult
                    {
                        Status = "complete",
                        Country = country,
                        Month = month,
                        Total = cell.Sum(s => s.Value),
                        SubmissionCount = cell.Count,
                        ExpectedSubmissions = epoch.ProducerCount,
                        MissingProducers = new List<string>()
                    });
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
            Partners = partners,
            MissingProducers = missingProducers,
            Aggregates = aggregates
        };
    }
}

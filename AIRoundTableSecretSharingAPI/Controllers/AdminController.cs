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

        var startDate = new DateTime(2025, 1, 1);

        await _producerRepo.AddProducerAsync(new ProducerInfo { ProducerId = "partnerA", DisplayName = "Partner A", JoinedDate = startDate, IsActive = true });
        await _producerRepo.AddProducerAsync(new ProducerInfo { ProducerId = "partnerB", DisplayName = "Partner B", JoinedDate = startDate, IsActive = true });
        await _producerRepo.AddProducerAsync(new ProducerInfo { ProducerId = "partnerC", DisplayName = "Partner C", JoinedDate = startDate, IsActive = true });

        // CreateEpochAsync closes any open epoch first — since we just cleared, add directly
        var epoch = new ProducerEpoch
        {
            EpochId = 1,
            StartDate = startDate,
            EndDate = null,
            ProducerIds = new List<string> { "partnerA", "partnerB", "partnerC" },
            ProducerCount = 3
        };
        await _producerRepo.AddEpochAsync(epoch);

        _logger.LogInformation("Database reset complete. Re-seeded 3 producers and epoch 1.");

        return Ok(new MessageResponse { Message = "Database reset to initial state." });
    }

    /// <summary>
    /// Replaces all producers with the submitted list and creates a fresh epoch.
    /// Also clears submissions, keys, and ciphertexts to ensure protocol consistency.
    /// Epoch start date is normalized to the first day of next month.
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
                DisplayName = p.DisplayName.Trim(),
                ClientSecret = p.ClientSecret.Trim()
            })
            .ToList();

        if (normalizedProducers.Any(p => string.IsNullOrWhiteSpace(p.ProducerId) || string.IsNullOrWhiteSpace(p.DisplayName)))
            return BadRequest(new { error = "Each producer must include non-empty producerId and displayName." });

        if (normalizedProducers.Any(p => string.IsNullOrWhiteSpace(p.ClientSecret)))
            return BadRequest(new { error = "Each producer must include a non-empty clientSecret." });

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
            await _submissionRepo.ClearAllAsync();
            await _ciphertextRepo.ClearAsync();
            await _keyRepo.ClearAsync();
            await _producerRepo.ClearAllAsync();

            foreach (var item in normalizedProducers)
            {
                await _producerRepo.AddProducerAsync(new ProducerInfo
                {
                    ProducerId = item.ProducerId,
                    DisplayName = item.DisplayName,
                    JoinedDate = startDate,
                    IsActive = true
                });
            }

            await _credentialService.ReplaceProducerCredentialsAsync(
                normalizedProducers.Select(p => (p.ProducerId, p.ClientSecret)),
                HttpContext.RequestAborted);

            var sortedProducerIds = normalizedProducers
                .Select(p => p.ProducerId)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            var epoch = new ProducerEpoch
            {
                EpochId = 1,
                StartDate = startDate,
                EndDate = null,
                ProducerIds = sortedProducerIds,
                ProducerCount = sortedProducerIds.Count
            };

            await _producerRepo.AddEpochAsync(epoch);
            await tx.CommitAsync();

            _logger.LogInformation(
                "Producer replacement complete. Created epoch {EpochId} effective {StartDate} with {Count} producers.",
                epoch.EpochId,
                epoch.StartDate,
                epoch.ProducerCount);

            return Ok(new ReplaceProducersResponse
            {
                Message = "Producers replaced and new epoch created.",
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
    /// Returns all country/month aggregates for the latest active epoch, including both complete
    /// (all partners submitted) and incomplete (missing submissions) states.
    /// </summary>
    [HttpGet("aggregates-latest-epoch")]
    [ProducesResponseType(typeof(LatestEpochAggregatesResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<LatestEpochAggregatesResponse>> GetLatestEpochAggregates()
    {
        // Get the latest active epoch (current date)
        var epoch = await _producerRepo.GetEpochForDateAsync(DateTime.UtcNow);
        if (epoch == null)
        {
            _logger.LogWarning("No active epoch found for current date.");
            return NotFound(new { error = "No active epoch found" });
        }

        // Retrieve all distinct (country, month) pairs with submissions in this epoch
        var countryMonthPairs = await _submissionRepo.GetDistinctCountryMonthPairsAsync(epoch.EpochId);

        if (countryMonthPairs.Count == 0)
        {
            _logger.LogInformation("No submissions found for epoch {EpochId}. Returning empty aggregates list.", epoch.EpochId);
            return Ok(new LatestEpochAggregatesResponse
            {
                EpochId = epoch.EpochId,
                StartDate = epoch.StartDate,
                PartnerCount = epoch.ProducerCount,
                Partners = epoch.ProducerIds,
                Aggregates = new List<AggregationResult>()
            });
        }

        // For each (country, month) pair, compute the AggregationResult
        var aggregates = new List<AggregationResult>();
        foreach (var (country, month) in countryMonthPairs)
        {
            var submissions = await _submissionRepo.GetSubmissionsAsync(country, month, epoch.EpochId);
            var submittedProducers = submissions.Select(s => s.ProducerId).ToHashSet();
            var missingProducers = epoch.ProducerIds.Except(submittedProducers).ToList();

            if (missingProducers.Any())
            {
                aggregates.Add(new AggregationResult
                {
                    Status = "incomplete",
                    Country = country,
                    Month = month,
                    Total = null,
                    SubmissionCount = submissions.Count,
                    ExpectedSubmissions = epoch.ProducerCount,
                    MissingProducers = missingProducers
                });
            }
            else
            {
                // All submissions received - compute aggregate
                long total = submissions.Sum(s => s.Value);
                aggregates.Add(new AggregationResult
                {
                    Status = "complete",
                    Country = country,
                    Month = month,
                    Total = total,
                    SubmissionCount = submissions.Count,
                    ExpectedSubmissions = epoch.ProducerCount,
                    MissingProducers = new List<string>()
                });
            }
        }

        _logger.LogInformation("Returning {Count} aggregates for epoch {EpochId}. Complete: {CompleteCount}, Incomplete: {IncompleteCount}",
            aggregates.Count, epoch.EpochId, aggregates.Count(a => a.Status == "complete"), aggregates.Count(a => a.Status == "incomplete"));

        return Ok(new LatestEpochAggregatesResponse
        {
            EpochId = epoch.EpochId,
            StartDate = epoch.StartDate,
            PartnerCount = epoch.ProducerCount,
            Partners = epoch.ProducerIds,
            Aggregates = aggregates
        });
    }
}

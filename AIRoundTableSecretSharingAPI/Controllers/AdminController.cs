using AIRoundTableSecretSharingAPI.Models;
using AIRoundTableSecretSharingAPI.Repositories;
using AIRoundTableSecretSharingCommon.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIRoundTableSecretSharingAPI.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly IProducerRepository _producerRepo;
    private readonly ISubmissionRepository _submissionRepo;
    private readonly IKeyRepository _keyRepo;
    private readonly ICiphertextRepository _ciphertextRepo;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IProducerRepository producerRepo,
        ISubmissionRepository submissionRepo,
        IKeyRepository keyRepo,
        ICiphertextRepository ciphertextRepo,
        ILogger<AdminController> logger)
    {
        _producerRepo = producerRepo;
        _submissionRepo = submissionRepo;
        _keyRepo = keyRepo;
        _ciphertextRepo = ciphertextRepo;
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

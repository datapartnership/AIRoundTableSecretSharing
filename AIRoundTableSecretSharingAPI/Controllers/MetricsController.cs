// Controllers/MetricsController.cs
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AIRoundTableSecretSharingAPI.Models;
using AIRoundTableSecretSharingAPI.Repositories;
using AIRoundTableSecretSharingCommon.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIRoundTableSecretSharingAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class MetricsController : ControllerBase
{
    private readonly IProducerRepository _producerRepo;
    private readonly ISubmissionRepository _submissionRepo;
    private readonly ILogger<MetricsController> _logger;

    public MetricsController(
        IProducerRepository producerRepo,
        ISubmissionRepository submissionRepo,
        ILogger<MetricsController> logger)
    {
        _producerRepo = producerRepo;
        _submissionRepo = submissionRepo;
        _logger = logger;
    }

    [HttpPost("submit")]
    [ProducesResponseType(typeof(MessageResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public async Task<ActionResult<MessageResponse>> SubmitMetric([FromBody] MetricSubmission submission)
    {
        // Normalize month to first day
        var monthStart = new DateTime(submission.Month.Year, submission.Month.Month, 1);
        submission.Month = monthStart;
        submission.SubmittedAt = DateTime.UtcNow;

        // Validate epoch
        var epoch = await _producerRepo.GetEpochForDateAsync(monthStart);
        if (epoch == null || epoch.EpochId != submission.EpochId)
        {
            return BadRequest(new
            {
                error = "Invalid epoch",
                expectedEpoch = epoch?.EpochId,
                submittedEpoch = submission.EpochId
            });
        }

        // Verify producer is in epoch
        if (!epoch.ProducerIds.Contains(submission.ProducerId))
        {
            return BadRequest(new
            {
                error = "Producer not in epoch",
                producerId = submission.ProducerId,
                epochId = epoch.EpochId
            });
        }

        // Store submission
        var added = await _submissionRepo.AddSubmissionAsync(submission);
        if (!added)
        {
            return Conflict("Duplicate submission");
        }

        _logger.LogInformation(
            "RECEIVED submission from {Producer} for {Country} - {Month}: Value = {Value:N0}",
            submission.ProducerId, submission.Country, submission.Month, submission.Value);

        return Ok(new MessageResponse { Message = "Submission received" });
    }

    [HttpGet("mysubmissions")]
    [ProducesResponseType(typeof(ProducerSubmissionsResponse), 200)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<ProducerSubmissionsResponse>> GetMySubmissions()
    {
        var producerId = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(producerId))
            return Unauthorized();

        var epoch = await _producerRepo.GetEpochForDateAsync(DateTime.UtcNow);
        if (epoch == null)
            return Ok(new ProducerSubmissionsResponse { EpochId = 0, Submissions = [] });

        var submissions = await _submissionRepo.GetSubmissionsByProducerAsync(producerId, epoch.EpochId);

        return Ok(new ProducerSubmissionsResponse
        {
            EpochId = epoch.EpochId,
            Submissions = submissions
                .Select(s => new SubmittedEntry { Country = s.Country, Month = s.Month })
                .ToList()
        });
    }

    [HttpGet("aggregate")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(AggregationResult), 200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<AggregationResult>> GetAggregate(
        [FromQuery] string country,
        [FromQuery] DateTime month)
    {
        var monthStart = new DateTime(month.Year, month.Month, 1);
        var epoch = await _producerRepo.GetEpochForDateAsync(monthStart);

        if (epoch == null)
            return NotFound("No epoch for date");

        var submissions = await _submissionRepo.GetSubmissionsAsync(country, monthStart, epoch.EpochId);
        var submittedProducers = submissions.Select(s => s.ProducerId).ToHashSet();
        var missingProducers = epoch.ProducerIds.Except(submittedProducers).ToList();

        if (missingProducers.Any())
        {
            _logger.LogWarning(
                "INCOMPLETE: {Country} - {Month} has {Received}/{Expected} submissions. Missing: {Missing}",
                country, month, submissions.Count, epoch.ProducerCount, string.Join(", ", missingProducers));

            return Ok(new AggregationResult
            {
                Status = "incomplete",
                Country = country,
                Month = monthStart,
                Total = null,
                SubmissionCount = submissions.Count,
                ExpectedSubmissions = epoch.ProducerCount,
                MissingProducers = missingProducers
            });
        }

        // All submissions received - compute aggregates
        long total = submissions.Sum(s => s.Value);

        _logger.LogInformation(
            "AGGREGATION COMPLETE for {Country} - {Month}: Total = {Total:N0} (noise canceled!)",
            country, month, total);

        return Ok(new AggregationResult
        {
            Status = "complete",
            Country = country,
            Month = monthStart,
            Total = total,
            SubmissionCount = submissions.Count,
            ExpectedSubmissions = epoch.ProducerCount,
            MissingProducers = new List<string>()
        });
    }
}
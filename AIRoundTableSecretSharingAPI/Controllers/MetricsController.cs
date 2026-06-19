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
    private readonly IKeyRepository _keyRepo;
    private readonly ICiphertextRepository _ciphertextRepo;
    private readonly ILogger<MetricsController> _logger;

    public MetricsController(
        IProducerRepository producerRepo,
        ISubmissionRepository submissionRepo,
        IKeyRepository keyRepo,
        ICiphertextRepository ciphertextRepo,
        ILogger<MetricsController> logger)
    {
        _producerRepo = producerRepo;
        _submissionRepo = submissionRepo;
        _keyRepo = keyRepo;
        _ciphertextRepo = ciphertextRepo;
        _logger = logger;
    }

    [HttpPost("submit")]
    [ProducesResponseType(typeof(MessageResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public async Task<ActionResult<MessageResponse>> SubmitMetric([FromBody] MetricSubmission submission)
    {
        // Identity is taken from the authenticated token — body field is ignored
        submission.ProducerId = User.FindFirstValue(JwtRegisteredClaimNames.Sub)!;

        // Validate and normalise month format to YYYY-MM
        if (string.IsNullOrEmpty(submission.Month) ||
            !System.Text.RegularExpressions.Regex.IsMatch(submission.Month, @"^\d{4}-\d{2}$"))
        {
            return BadRequest(new { error = "Month must be in YYYY-MM format (e.g. 2025-01)" });
        }

        submission.SubmittedAt = DateTime.UtcNow;

        // Parse month string for epoch lookup
        var monthDate = DateTime.ParseExact(submission.Month, "yyyy-MM",
            System.Globalization.CultureInfo.InvariantCulture);

        // Validate epoch
        var epoch = await _producerRepo.GetEpochForDateAsync(monthDate);
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

        // Verify key exchange is complete before accepting any submission.
        // Every partner must have registered a public key and every pair must
        // have exchanged a ciphertext, otherwise noise will not cancel.
        var registeredKeys = await _keyRepo.GetAllKeysAsync();
        var registeredKeyIds = registeredKeys.Select(k => k.ProducerId).ToHashSet();
        var missingKeys = epoch.ProducerIds.Except(registeredKeyIds).ToList();
        if (missingKeys.Count > 0)
        {
            return UnprocessableEntity(new
            {
                error = "Key exchange incomplete: missing public keys",
                missingPublicKeys = missingKeys
            });
        }

        var n = epoch.ProducerIds.Count;
        var expectedCiphertexts = n * (n - 1) / 2;
        var actualCiphertexts = await _ciphertextRepo.CountForPartnersAsync(epoch.ProducerIds);
        if (actualCiphertexts < expectedCiphertexts)
        {
            return UnprocessableEntity(new
            {
                error = "Key exchange incomplete: not all ciphertexts posted",
                expectedCiphertexts,
                actualCiphertexts
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
        [FromQuery] string month)
    {
        if (string.IsNullOrEmpty(month) ||
            !System.Text.RegularExpressions.Regex.IsMatch(month, @"^\d{4}-\d{2}$"))
        {
            return BadRequest(new { error = "month must be in YYYY-MM format (e.g. 2025-01)" });
        }

        var monthDate = DateTime.ParseExact(month, "yyyy-MM",
            System.Globalization.CultureInfo.InvariantCulture);
        var epoch = await _producerRepo.GetEpochForDateAsync(monthDate);

        if (epoch == null)
            return NotFound("No epoch for date");

        var submissions = await _submissionRepo.GetSubmissionsAsync(country, month, epoch.EpochId);
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
                Month = month,
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
            Month = month,
            Total = total,
            SubmissionCount = submissions.Count,
            ExpectedSubmissions = epoch.ProducerCount,
            MissingProducers = new List<string>()
        });
    }
}
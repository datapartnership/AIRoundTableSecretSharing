// Controllers/MetricsController.cs
using System.Globalization;
using System.Text.RegularExpressions;
using AIRoundTableSecretSharingAPI.Models;
using AIRoundTableSecretSharingAPI.Repositories;
using AIRoundTableSecretSharingAPI.Services;
using AIRoundTableSecretSharingCommon.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIRoundTableSecretSharingAPI.Controllers;

[ApiController]
[Authorize(Policy = "Partner")]
[Route("api/[controller]")]
public class MetricsController : ControllerBase
{
    private static readonly Regex MonthRegex = new(@"^\d{4}-(0[1-9]|1[0-2])$", RegexOptions.Compiled);

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
        var producerId = User.GetOid()!;
        var prepared = PrepareSubmission(submission, producerId);
        if (prepared.Error is { } fieldError)
            return fieldError;

        var gate = await GateEpochForSubmitAsync(prepared.MonthDate, submission.EpochId, producerId);
        if (gate.Error is { } gateError)
            return gateError;

        var epoch = gate.Epoch!;
        if (!EpochGrid.IsRequiredCell(
                submission.Country, submission.Month, submission.Indicator, submission.Segment, epoch.StartDate))
        {
            return BadRequest(new
            {
                error = "Cell is not in the required epoch grid",
                country = submission.Country,
                month = submission.Month,
                indicator = submission.Indicator,
                segment = submission.Segment
            });
        }

        var added = await _submissionRepo.AddSubmissionAsync(submission);
        if (!added)
            return Conflict("Duplicate submission");

        _logger.LogInformation(
            "RECEIVED submission from {Producer} for {Country}/{Month}/{Indicator}/{Segment}: Value = {Value:N0}",
            submission.ProducerId, submission.Country, submission.Month, submission.Indicator, submission.Segment, submission.Value);

        await EpochLifecycle.CloseIfCompleteAsync(epoch, _submissionRepo, _producerRepo);
        if (epoch.IsClosed)
            _logger.LogInformation("Epoch {EpochId} closed — all producers submitted every cell.", epoch.EpochId);

        return Ok(new MessageResponse
        {
            Message = epoch.IsClosed ? "Submission received. Epoch is now closed." : "Submission received"
        });
    }

    [HttpPost("submit-batch")]
    [ProducesResponseType(typeof(MessageResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public async Task<ActionResult<MessageResponse>> SubmitMetricBatch([FromBody] List<MetricSubmission>? submissions)
    {
        if (submissions == null || submissions.Count == 0)
            return BadRequest(new { error = "Batch must contain at least one row" });

        var producerId = User.GetOid()!;
        var now = DateTime.UtcNow;
        DateTime? monthDate = null;
        int? epochId = null;
        var batchKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var submission in submissions)
        {
            var prepared = PrepareSubmission(submission, producerId);
            if (prepared.Error is { } fieldError)
                return fieldError;

            submission.SubmittedAt = now;

            if (monthDate == null)
            {
                monthDate = prepared.MonthDate;
                epochId = submission.EpochId;
            }
            else if (submission.EpochId != epochId)
            {
                return BadRequest(new { error = "All rows in a batch must share the same epochId" });
            }

            var key = EpochGrid.CellKey(submission.Country, submission.Month, submission.Indicator, submission.Segment);
            if (!batchKeys.Add(key))
                return BadRequest(new { error = $"Duplicate cell in batch: {key}" });
        }

        var gate = await GateEpochForSubmitAsync(monthDate!.Value, epochId!.Value, producerId);
        if (gate.Error is { } gateError)
            return gateError;

        var epoch = gate.Epoch!;
        foreach (var submission in submissions)
        {
            if (!EpochGrid.IsRequiredCell(
                    submission.Country, submission.Month, submission.Indicator, submission.Segment, epoch.StartDate))
            {
                return BadRequest(new
                {
                    error = "Cell is not in the required epoch grid",
                    country = submission.Country,
                    month = submission.Month,
                    indicator = submission.Indicator,
                    segment = submission.Segment
                });
            }
        }

        var existing = await _submissionRepo.GetSubmissionsByProducerAsync(producerId, epoch.EpochId);
        var existingKeys = existing
            .Select(s => EpochGrid.CellKey(s.Country, s.Month, s.Indicator, s.Segment))
            .ToHashSet(StringComparer.Ordinal);

        var toInsert = submissions
            .Where(s => !existingKeys.Contains(EpochGrid.CellKey(s.Country, s.Month, s.Indicator, s.Segment)))
            .ToList();

        await _submissionRepo.AddSubmissionsAsync(toInsert);

        _logger.LogInformation(
            "RECEIVED batch from {Producer}: {Inserted} new, {Skipped} already submitted, {Total} in request",
            producerId, toInsert.Count, submissions.Count - toInsert.Count, submissions.Count);

        await EpochLifecycle.CloseIfCompleteAsync(epoch, _submissionRepo, _producerRepo);
        if (epoch.IsClosed)
            _logger.LogInformation("Epoch {EpochId} closed — all producers submitted every cell.", epoch.EpochId);

        return Ok(new MessageResponse
        {
            Message = epoch.IsClosed
                ? "Submissions received. Epoch is now closed."
                : $"Submissions received ({toInsert.Count} new)"
        });
    }

    [HttpGet("mysubmissions")]
    [ProducesResponseType(typeof(ProducerSubmissionsResponse), 200)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<ProducerSubmissionsResponse>> GetMySubmissions()
    {
        var producerId = User.GetOid();

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
                .Select(s => new SubmittedEntry
                {
                    Country = s.Country,
                    Month = s.Month,
                    Indicator = s.Indicator,
                    Segment = s.Segment
                })
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
        [FromQuery] string month,
        [FromQuery] string indicator,
        [FromQuery] string segment)
    {
        if (string.IsNullOrEmpty(month) || !MonthRegex.IsMatch(month))
            return BadRequest(new { error = "month must be in YYYY-MM format (e.g. 2025-01)" });

        if (string.IsNullOrWhiteSpace(country) ||
            string.IsNullOrWhiteSpace(indicator) ||
            string.IsNullOrWhiteSpace(segment))
        {
            return BadRequest(new { error = "country, indicator, and segment are required" });
        }

        var monthDate = DateTime.ParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture);
        var epoch = await _producerRepo.GetEpochForDateAsync(monthDate);

        if (epoch == null)
            return NotFound("No epoch for date");

        var submissions = await _submissionRepo.GetSubmissionsAsync(country, month, indicator, segment, epoch.EpochId);
        var submittedProducers = submissions
            .Select(s => s.ProducerId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Cast<string>()
            .ToHashSet();
        var missingProducers = epoch.ProducerIds.Except(submittedProducers).ToList();

        if (missingProducers.Count > 0)
        {
            _logger.LogWarning(
                "INCOMPLETE: {Country}/{Month}/{Indicator}/{Segment} has {Received}/{Expected} submissions. Missing: {Missing}",
                country, month, indicator, segment, submissions.Count, epoch.ProducerCount, string.Join(", ", missingProducers));

            return Ok(new AggregationResult
            {
                Status = "incomplete",
                Country = country,
                Month = month,
                Indicator = indicator,
                Segment = segment,
                Total = null,
                SubmissionCount = submissions.Count,
                ExpectedSubmissions = epoch.ProducerCount,
                MissingProducers = missingProducers
            });
        }

        long total = submissions.Sum(s => s.Value);

        _logger.LogInformation(
            "AGGREGATION COMPLETE for {Country}/{Month}/{Indicator}/{Segment}: Total = {Total:N0} (noise canceled!)",
            country, month, indicator, segment, total);

        return Ok(new AggregationResult
        {
            Status = "complete",
            Country = country,
            Month = month,
            Indicator = indicator,
            Segment = segment,
            Total = total.ToString(CultureInfo.InvariantCulture),
            SubmissionCount = submissions.Count,
            ExpectedSubmissions = epoch.ProducerCount,
            MissingProducers = new List<string>()
        });
    }

    private sealed record PreparedSubmission(DateTime MonthDate, ActionResult? Error);

    private PreparedSubmission PrepareSubmission(MetricSubmission submission, string producerId)
    {
        submission.ProducerId = producerId;
        submission.Country = submission.Country?.Trim() ?? string.Empty;
        submission.Month = submission.Month?.Trim() ?? string.Empty;
        submission.Indicator = submission.Indicator?.Trim() ?? string.Empty;
        submission.Segment = submission.Segment?.Trim() ?? string.Empty;
        submission.SubmittedAt = DateTime.UtcNow;

        if (!MonthRegex.IsMatch(submission.Month))
            return new PreparedSubmission(default, BadRequest(new { error = "Month must be in YYYY-MM format (e.g. 2025-01)" }));

        if (string.IsNullOrEmpty(submission.Country) ||
            string.IsNullOrEmpty(submission.Indicator) ||
            string.IsNullOrEmpty(submission.Segment))
        {
            return new PreparedSubmission(default, BadRequest(new { error = "country, indicator, and segment are required" }));
        }

        var monthDate = DateTime.ParseExact(submission.Month, "yyyy-MM", CultureInfo.InvariantCulture);
        return new PreparedSubmission(monthDate, null);
    }

    private sealed record EpochGate(ProducerEpoch? Epoch, ActionResult? Error);

    private async Task<EpochGate> GateEpochForSubmitAsync(DateTime monthDate, int submittedEpochId, string producerId)
    {
        var epoch = await _producerRepo.GetEpochForDateAsync(monthDate);
        if (epoch == null || epoch.EpochId != submittedEpochId)
        {
            return new EpochGate(null, BadRequest(new
            {
                error = "Invalid epoch",
                expectedEpoch = epoch?.EpochId,
                submittedEpoch = submittedEpochId
            }));
        }

        if (!epoch.ProducerIds.Contains(producerId))
        {
            return new EpochGate(null, BadRequest(new
            {
                error = "Producer not in epoch",
                producerId,
                epochId = epoch.EpochId
            }));
        }

        if (epoch.IsClosed)
            return new EpochGate(null, BadRequest(new { error = "Epoch is closed", epochId = epoch.EpochId }));

        await EpochLifecycle.CloseIfCompleteAsync(epoch, _submissionRepo, _producerRepo);
        if (epoch.IsClosed)
            return new EpochGate(null, BadRequest(new { error = "Epoch is closed", epochId = epoch.EpochId }));

        var registeredKeys = await _keyRepo.GetAllKeysAsync(epoch.EpochId);
        var registeredKeyIds = registeredKeys.Select(k => k.ProducerId).ToHashSet();
        var missingKeys = epoch.ProducerIds.Except(registeredKeyIds).ToList();
        if (missingKeys.Count > 0)
        {
            return new EpochGate(null, UnprocessableEntity(new
            {
                error = "Key exchange incomplete: missing public keys",
                missingPublicKeys = missingKeys
            }));
        }

        var n = epoch.ProducerIds.Count;
        var expectedCiphertexts = n * (n - 1) / 2;
        var actualCiphertexts = await _ciphertextRepo.CountForPartnersAsync(epoch.EpochId, epoch.ProducerIds);
        if (actualCiphertexts < expectedCiphertexts)
        {
            return new EpochGate(null, UnprocessableEntity(new
            {
                error = "Key exchange incomplete: not all ciphertexts posted",
                expectedCiphertexts,
                actualCiphertexts
            }));
        }

        return new EpochGate(epoch, null);
    }
}

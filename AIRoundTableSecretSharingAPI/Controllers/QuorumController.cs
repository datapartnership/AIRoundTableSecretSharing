using AIRoundTableSecretSharingAPI.Models;
using AIRoundTableSecretSharingAPI.Repositories;
using AIRoundTableSecretSharingAPI.Services;
using AIRoundTableSecretSharingCommon.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIRoundTableSecretSharingAPI.Controllers;

/// <summary>
/// Quorum Check: once per period and usage type, partners declare per country and indicator whether they will
/// report it (1) or not (0). Answers are masked with the period's pairwise key-exchange noise, so the server
/// only learns per-metric totals (participant count and a composition fingerprint).
/// </summary>
[ApiController]
[Authorize(Policy = "Partner")]
[Route("api/[controller]")]
public class QuorumController : ControllerBase
{
    private readonly IQuorumRepository _quorumRepo;
    private readonly IKeyRepository _keyRepo;
    private readonly ICiphertextRepository _ciphertextRepo;
    private readonly ILogger<QuorumController> _logger;

    public QuorumController(
        IQuorumRepository quorumRepo,
        IKeyRepository keyRepo,
        ICiphertextRepository ciphertextRepo,
        ILogger<QuorumController> logger)
    {
        _quorumRepo = quorumRepo;
        _keyRepo = keyRepo;
        _ciphertextRepo = ciphertextRepo;
        _logger = logger;
    }

    [HttpPost("submit")]
    [Authorize(Policy = "Participant")]
    [ProducesResponseType(typeof(MessageResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public async Task<ActionResult<MessageResponse>> Submit([FromBody] QuorumSubmission? submission)
    {
        if (submission?.Answers == null || submission.Answers.Count == 0)
            return BadRequest(new { error = "Submission must contain answers" });

        var producerId = User.GetOid()!;
        var period = await _quorumRepo.GetPeriodAsync(submission.PeriodId);
        if (period == null)
            return BadRequest(new { error = "Invalid Quorum Check period", periodId = submission.PeriodId });
        if (!period.ProducerIds.Contains(producerId))
            return BadRequest(new { error = "Producer not in period", producerId, periodId = period.PeriodId });
        if (EpochLifecycle.CancelledError(period) is { } cancelled)
            return Conflict(cancelled);
        if (period.QuorumComplete)
            return BadRequest(new { error = "Quorum Check is already complete", periodId = period.PeriodId });

        // Masks only cancel once every pairwise secret exists
        if (await EpochLifecycle.KeyExchangeErrorAsync(period.KeyScopeId, period.ProducerIds, _keyRepo, _ciphertextRepo) is { } keyError)
            return UnprocessableEntity(keyError);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var now = DateTime.UtcNow;
        var answers = new List<QuorumAnswer>();
        foreach (var a in submission.Answers)
        {
            var country = a.Country?.Trim() ?? string.Empty;
            var indicator = a.Indicator?.Trim() ?? string.Empty;

            if (!QuorumGrid.IsCell(country, indicator))
                return BadRequest(new { error = "Answer is not in the Quorum Check grid", country, indicator });
            if (!seen.Add(QuorumGrid.PairKey(country, indicator)))
                return BadRequest(new { error = $"Duplicate answer: {QuorumGrid.PairKey(country, indicator)}" });

            answers.Add(new QuorumAnswer
            {
                PeriodId = period.PeriodId,
                ProducerId = producerId,
                Country = country,
                Indicator = indicator,
                MaskedCount = a.MaskedCount,
                MaskedTagA = a.MaskedTagA,
                MaskedTagB = a.MaskedTagB,
                SubmittedAt = now
            });
        }

        // The whole grid is required so partial answers cannot leave the quorum ambiguous
        if (answers.Count != QuorumGrid.CellCount)
            return BadRequest(new { error = $"Quorum Check must cover all {QuorumGrid.CellCount} metrics", received = answers.Count });

        var existing = await _quorumRepo.GetAnswersAsync(period.PeriodId);
        if (existing.Any(r => r.ProducerId == producerId))
            return Conflict(new { error = "Quorum Check already submitted", code = "already-answered" });

        await _quorumRepo.AddAnswersAsync(answers);
        _logger.LogInformation("RECEIVED quorum answers from {Producer} for period {PeriodId}", producerId, period.PeriodId);

        await EpochLifecycle.CompletePeriodIfAnsweredAsync(period, _quorumRepo);
        return Ok(new MessageResponse
        {
            Message = period.QuorumComplete
                ? "Quorum Check received. All partners answered; data upload is open."
                : "Quorum Check received"
        });
    }

    /// <summary>The caller's view of the one active (unfinished) Quorum Check period, of any usage type.</summary>
    [HttpGet("current")]
    [ProducesResponseType(typeof(QuorumStatusResponse), 200)]
    public async Task<ActionResult<QuorumStatusResponse>> GetCurrent()
    {
        var producerId = User.GetOid();
        if (string.IsNullOrEmpty(producerId))
            return Unauthorized();

        var period = await EpochLifecycle.ActivePeriodAsync(_quorumRepo);
        if (period == null)
            return Ok(new QuorumStatusResponse());
        return Ok(await BuildStatusAsync(period, producerId));
    }

    /// <summary>The caller's view of one Quorum Check period, for the epochs it covers.</summary>
    [HttpGet("periods/{periodId:int}")]
    [ProducesResponseType(typeof(QuorumStatusResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<QuorumStatusResponse>> GetPeriod(int periodId)
    {
        var producerId = User.GetOid();
        if (string.IsNullOrEmpty(producerId))
            return Unauthorized();

        var period = await _quorumRepo.GetPeriodAsync(periodId);
        if (period == null || !period.ProducerIds.Contains(producerId))
            return NotFound(new { error = "Quorum Check period not found" });
        return Ok(await BuildStatusAsync(period, producerId));
    }

    private async Task<QuorumStatusResponse> BuildStatusAsync(QuorumPeriod period, string producerId)
    {
        await EpochLifecycle.CompletePeriodIfAnsweredAsync(period, _quorumRepo);
        var answers = await _quorumRepo.GetAnswersAsync(period.PeriodId);
        var counts = QuorumGrid.AnsweredCounts(period, answers);
        var cells = QuorumGrid.Evaluate(period, answers);
        var unverified = cells.Where(c => c.Unverified).Select(c => QuorumGrid.PairKey(c.Country, c.Indicator)).ToList();
        if (unverified.Count > 0)
            _logger.LogWarning("Quorum Check period {PeriodId}: masks did not cancel for {Cells}; these metrics are ignored",
                period.PeriodId, string.Join(", ", unverified));

        return new QuorumStatusResponse
        {
            UsageType = period.UsageType,
            Period = new QuorumPeriodSummary(period),
            IsEligible = period.ProducerIds.Contains(producerId),
            QuorumComplete = period.QuorumComplete,
            MyDone = counts.GetValueOrDefault(producerId) >= QuorumGrid.CellCount,
            MyAnsweredAt = answers.Where(r => r.ProducerId == producerId).Select(r => (DateTime?)r.SubmittedAt).Max(),
            AnsweredPartners = counts.Count(c => c.Value >= QuorumGrid.CellCount),
            PartnerCount = period.ProducerIds.Count,
            IgnoredMetrics = cells
                .Where(c => c.Ignored)
                .Select(c => new QuorumMetric { Country = c.Country, Indicator = c.Indicator })
                .ToList()
        };
    }
}

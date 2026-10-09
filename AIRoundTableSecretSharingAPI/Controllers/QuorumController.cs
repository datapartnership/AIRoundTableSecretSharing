using System.Globalization;
using System.Text.RegularExpressions;
using AIRoundTableSecretSharingAPI.Models;
using AIRoundTableSecretSharingAPI.Repositories;
using AIRoundTableSecretSharingAPI.Services;
using AIRoundTableSecretSharingCommon.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIRoundTableSecretSharingAPI.Controllers;

/// <summary>
/// Quorum Check: before submissions, partners declare per metric cell whether they will
/// participate (1) or not (0). Answers are masked with the pairwise key-exchange noise, so the server
/// only learns per-metric totals (participant count and a composition fingerprint).
/// </summary>
[ApiController]
[Authorize(Policy = "Partner")]
[Route("api/[controller]")]
public class QuorumController : ControllerBase
{
    private readonly IProducerRepository _producerRepo;
    private readonly IQuorumRepository _quorumRepo;
    private readonly IKeyRepository _keyRepo;
    private readonly ICiphertextRepository _ciphertextRepo;
    private readonly ILogger<QuorumController> _logger;

    public QuorumController(
        IProducerRepository producerRepo,
        IQuorumRepository quorumRepo,
        IKeyRepository keyRepo,
        ICiphertextRepository ciphertextRepo,
        ILogger<QuorumController> logger)
    {
        _producerRepo = producerRepo;
        _quorumRepo = quorumRepo;
        _keyRepo = keyRepo;
        _ciphertextRepo = ciphertextRepo;
        _logger = logger;
    }

    [HttpPost("submit-batch")]
    [Authorize(Policy = "Participant")]
    [ProducesResponseType(typeof(MessageResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public async Task<ActionResult<MessageResponse>> SubmitBatch([FromBody] List<QuorumSubmission>? rows)
    {
        if (rows == null || rows.Count == 0)
            return BadRequest(new { error = "Batch must contain at least one row" });

        var producerId = User.GetOid()!;
        var epochId = rows[0].EpochId;
        if (rows.Any(r => r.EpochId != epochId))
            return BadRequest(new { error = "All rows in a batch must share the same epochId" });

        var epoch = await _producerRepo.GetEpochByIdAsync(epochId);
        if (epoch == null)
            return BadRequest(new { error = "Invalid epoch", submittedEpoch = epochId });
        if (!epoch.ProducerIds.Contains(producerId))
            return BadRequest(new { error = "Producer not in epoch", producerId, epochId });
        if (EpochLifecycle.CancelledError(epoch) is { } cancelled)
            return Conflict(cancelled);
        if (epoch.IsClosed)
            return BadRequest(new { error = "Epoch is closed", epochId });
        if (epoch.QuorumComplete)
            return BadRequest(new { error = "Quorum Check is already complete", epochId });

        // Masks only cancel once every pairwise secret exists
        if (await EpochLifecycle.KeyExchangeErrorAsync(epoch, _keyRepo, _ciphertextRepo) is { } keyError)
            return UnprocessableEntity(keyError);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var now = DateTime.UtcNow;
        var responses = new List<QuorumResponse>();
        foreach (var r in rows)
        {
            var country = r.Country?.Trim() ?? string.Empty;
            var month = r.Month?.Trim() ?? string.Empty;
            var indicator = r.Indicator?.Trim() ?? string.Empty;
            var segment = r.Segment?.Trim() ?? string.Empty;

            if (!EpochGrid.IsRequiredCell(country, month, indicator, segment, epoch.StartDate))
                return BadRequest(new { error = "Cell is not in the required epoch grid", country, month, indicator, segment });
            if (!seen.Add(EpochGrid.CellKey(country, month, indicator, segment)))
                return BadRequest(new { error = $"Duplicate cell in batch: {EpochGrid.CellKey(country, month, indicator, segment)}" });

            responses.Add(new QuorumResponse
            {
                EpochId = epochId,
                ProducerId = producerId,
                Country = country,
                Month = month,
                Indicator = indicator,
                Segment = segment,
                MaskedCount = r.MaskedCount,
                MaskedTagA = r.MaskedTagA,
                MaskedTagB = r.MaskedTagB,
                SubmittedAt = now
            });
        }

        // The whole grid is required so partial answers cannot leave the quorum ambiguous
        if (responses.Count != EpochGrid.CellCount)
            return BadRequest(new { error = $"Quorum Check must cover all {EpochGrid.CellCount} cells", received = responses.Count });

        var existing = await _quorumRepo.GetByProducerAsync(epochId, producerId);
        if (existing.Count > 0)
            return Conflict(new { error = "Quorum Check already submitted" });

        await _quorumRepo.AddAsync(responses);
        _logger.LogInformation("RECEIVED quorum answers from {Producer} for epoch {EpochId}", producerId, epochId);

        await EpochLifecycle.CompleteQuorumIfAnsweredAsync(epoch, _quorumRepo, _producerRepo);
        return Ok(new MessageResponse
        {
            Message = epoch.QuorumComplete
                ? "Quorum Check received. All partners answered; submission phase is open."
                : "Quorum Check received"
        });
    }

    [HttpGet("status")]
    [ProducesResponseType(typeof(QuorumStatusResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<QuorumStatusResponse>> GetStatus([FromQuery] int epochId)
    {
        var producerId = User.GetOid();
        if (string.IsNullOrEmpty(producerId))
            return Unauthorized();

        var epoch = await _producerRepo.GetEpochByIdAsync(epochId);
        if (epoch == null || !epoch.ProducerIds.Contains(producerId))
            return NotFound(new { error = "Epoch not found" });

        await EpochLifecycle.CompleteQuorumIfAnsweredAsync(epoch, _quorumRepo, _producerRepo);
        var all = await _quorumRepo.GetByEpochAsync(epochId);
        var mine = all.Where(r => r.ProducerId == producerId).ToList();
        var answeredPartners = all.GroupBy(r => r.ProducerId)
            .Count(g => epoch.ProducerIds.Contains(g.Key) && g.Count() >= EpochGrid.CellCount);

        var response = new QuorumStatusResponse
        {
            EpochId = epochId,
            QuorumComplete = epoch.QuorumComplete,
            MyDone = mine.Count >= EpochGrid.CellCount,
            AnsweredPartners = answeredPartners,
            PartnerCount = epoch.ProducerCount
        };

        if (epoch.QuorumComplete)
        {
            response.IgnoredCells = QuorumGrid.Evaluate(epoch, all)
                .Where(c => c.Ignored)
                .Select(c => new SubmittedEntry { Country = c.Country, Month = c.Month, Indicator = c.Indicator, Segment = c.Segment })
                .ToList();
        }

        return Ok(response);
    }
}


// Controllers/RegistryController.cs
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AIRoundTableSecretSharingAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AIRoundTableSecretSharingAPI.Repositories;
using AIRoundTableSecretSharingAPI.Services;
using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingAPI.Controllers;

[ApiController]
[Authorize(Policy = "Partner")]
[Route("api/[controller]")]
public class RegistryController : ControllerBase
{
    private const int CancelledEpochVisibleDays = 30;

    private readonly IProducerRepository _producerRepo;
    private readonly ISubmissionRepository _submissionRepo;
    private readonly IQuorumRepository _quorumRepo;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RegistryController> _logger;

    public RegistryController(
        IProducerRepository producerRepo,
        ISubmissionRepository submissionRepo,
        IQuorumRepository quorumRepo,
        IConfiguration configuration,
        ILogger<RegistryController> logger)
    {
        _producerRepo = producerRepo;
        _submissionRepo = submissionRepo;
        _quorumRepo = quorumRepo;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("producers")]
    [ProducesResponseType(typeof(List<ProducerInfo>), 200)]
    public async Task<ActionResult<List<ProducerInfo>>> GetProducers([FromQuery] DateTime? effectiveDate = null)
    {
        var date = effectiveDate ?? DateTime.UtcNow;
        var producers = await _producerRepo.GetActiveProducersAsync(date);

        _logger.LogInformation(
            "Producer list requested for {Date}: {Count} producers",
            date, producers.Count);

        return Ok(producers);
    }

    [HttpGet("epoch")]
    [ProducesResponseType(typeof(ProducerEpoch), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<ProducerEpoch>> GetEpoch([FromQuery] DateTime? date = null, [FromQuery] string usageType = UsageTypes.Api)
    {
        if (!UsageTypes.IsValid(usageType))
            return BadRequest(new { error = "usageType must be api or chat" });

        var effectiveDate = date ?? DateTime.UtcNow;
        var epoch = await _producerRepo.GetEpochForDateAsync(effectiveDate, usageType);

        if (epoch == null)
            return NotFound("No epoch found for date");

        await EpochLifecycle.CloseIfCompleteAsync(epoch, _submissionRepo, _producerRepo, _quorumRepo);
        epoch.IsEligible = User.GetOid() is { } oid && epoch.ProducerIds.Contains(oid);

        _logger.LogInformation(
            "Epoch requested for {Date}: Epoch {EpochId} with {Count} producers, closed={Closed}",
            effectiveDate, epoch.EpochId, epoch.ProducerCount, epoch.IsClosed);

        return Ok(epoch);
    }

    [HttpGet("epochs")]
    [ProducesResponseType(typeof(EpochListResponse), 200)]
    public async Task<ActionResult<EpochListResponse>> GetEpochsForPartner()
    {
        var producerId = User.GetOid();
        if (producerId is null)
        {
            var claims = User.Claims.Select(c => $"{c.Type}={c.Value}");
            _logger.LogWarning(
                "Epoch list request rejected because the access token has no resolvable object ID. Claims present: {Claims}",
                string.Join(", ", claims));
            return Unauthorized();
        }

        var now = DateTime.UtcNow;
        var epochs = (await _producerRepo.GetAllEpochsAsync())
            .Where(e => e.IsCancelled
                // Participants keep seeing a cancelled epoch for a while so they learn why it stopped
                ? e.ProducerIds.Contains(producerId) && e.CancelledAt > now.AddDays(-CancelledEpochVisibleDays)
                // Closed epochs stay listed for their participants as history
                : e.IsClosed ? e.ProducerIds.Contains(producerId) : e.EndDate == null || e.EndDate > now)
            .OrderBy(e => e.IsCancelled)
            .ToList();

        var summaries = new List<EpochSummary>();
        foreach (var e in epochs)
        {
            var isEligible = e.ProducerIds.Contains(producerId);
            summaries.Add(new EpochSummary
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
                MonthCount = e.MonthCount,
                ProducerIds = e.ProducerIds,
                IsEligible = isEligible,
                MySubmissionComplete = isEligible && !e.IsCancelled && await MySubmissionCompleteAsync(e, producerId)
            });
        }
        return Ok(new EpochListResponse { Epochs = summaries });
    }

    private async Task<bool> MySubmissionCompleteAsync(ProducerEpoch epoch, string producerId)
    {
        if (epoch.IsClosed)
            return true;
        var mine = await _submissionRepo.GetSubmissionsByProducerAsync(producerId, epoch.EpochId);
        if (mine.Count == 0)
            return false;
        var ignored = await EpochLifecycle.IgnoredKeysAsync(epoch, _quorumRepo);
        return !EpochGrid.MissingCells(epoch, mine, ignored).Any(m => m.ProducerId == producerId);
    }

    [HttpPost("producers/me")]
    [ProducesResponseType(200)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> SelfRegister()
    {
        // Azure AD OID claim — name varies depending on MapInboundClaims setting
        var producerId = User.GetOid();

        if (producerId is null)
        {
            var allClaims = User.Claims.Select(c => $"{c.Type}={c.Value}").ToList();
            _logger.LogError("SelfRegister: could not resolve producerId. Claims present: {Claims}", string.Join(", ", allClaims));
            return BadRequest(new { error = "Unable to resolve user identity from token.", claims = allClaims });
        }

        if (User.IsAdmin(_configuration))
        {
            await _producerRepo.DeactivateProducerAsync(producerId);
            _logger.LogWarning("SelfRegister rejected for admin {ProducerId}", producerId);
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Admins cannot register as participants." });
        }

        var displayName = User.FindFirstValue("name")
            ?? User.FindFirstValue("preferred_username")
            ?? producerId;

        await _producerRepo.UpsertProducerAsync(new ProducerInfo
        {
            ProducerId = producerId,
            DisplayName = displayName,
            JoinedDate = DateTime.UtcNow,
            IsActive = true,
        });

        _logger.LogInformation("Self-registered producer {ProducerId} ({DisplayName})", producerId, displayName);
        return Ok(new { producerId, displayName });
    }
}

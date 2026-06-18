
// Controllers/RegistryController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AIRoundTableSecretSharingAPI.Repositories;
using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class RegistryController : ControllerBase
{
    private readonly IProducerRepository _producerRepo;
    private readonly ILogger<RegistryController> _logger;

    public RegistryController(IProducerRepository producerRepo, ILogger<RegistryController> logger)
    {
        _producerRepo = producerRepo;
        _logger = logger;
    }

    [HttpGet("producers")]
    public async Task<IActionResult> GetProducers([FromQuery] DateTime? effectiveDate = null)
    {
        var date = effectiveDate ?? DateTime.UtcNow;
        var producers = await _producerRepo.GetActiveProducersAsync(date);

        _logger.LogInformation(
            "Producer list requested for {Date}: {Count} producers",
            date, producers.Count);

        return Ok(producers);
    }

    [HttpGet("epoch")]
    public async Task<IActionResult> GetEpoch([FromQuery] DateTime? date = null)
    {
        var effectiveDate = date ?? DateTime.UtcNow;
        var epoch = await _producerRepo.GetEpochForDateAsync(effectiveDate);

        if (epoch == null)
            return NotFound("No epoch found for date");

        _logger.LogInformation(
            "Epoch requested for {Date}: Epoch {EpochId} with {Count} producers",
            effectiveDate, epoch.EpochId, epoch.ProducerCount);

        return Ok(epoch);
    }

    [HttpPost("producers")]
    public async Task<IActionResult> AddProducer([FromBody] AddProducerRequest request)
    {
        var startDate = new DateTime(
            request.StartDate.Year,
            request.StartDate.Month,
            1).AddMonths(1);

        var producer = new ProducerInfo
        {
            ProducerId = request.ProducerId,
            DisplayName = request.DisplayName,
            JoinedDate = startDate,
            IsActive = true
        };

        await _producerRepo.AddProducerAsync(producer);

        // Build new epoch from all active producers (including the one just added)
        var activeProducers = await _producerRepo.GetActiveProducersAsync(startDate);
        var currentEpoch = await _producerRepo.GetEpochForDateAsync(DateTime.UtcNow);
        var newEpoch = new ProducerEpoch
        {
            EpochId = (currentEpoch?.EpochId ?? 0) + 1,
            StartDate = startDate,
            EndDate = null,
            ProducerIds = activeProducers.Select(p => p.ProducerId).OrderBy(id => id).ToList(),
            ProducerCount = activeProducers.Count
        };

        await _producerRepo.CreateEpochAsync(newEpoch);

        _logger.LogInformation(
            "Added producer {ProducerId}, effective {StartDate}. New epoch {EpochId} created.",
            request.ProducerId, startDate, newEpoch.EpochId);

        return Ok(new { message = "Producer added", epoch = newEpoch });
    }
}

public class AddProducerRequest
{
    public string ProducerId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
}
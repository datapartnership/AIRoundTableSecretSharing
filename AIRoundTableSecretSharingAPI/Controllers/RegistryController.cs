
// Controllers/RegistryController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AIRoundTableSecretSharingAPI.Services;
using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class RegistryController : ControllerBase
{
    private readonly InMemoryDataStore _dataStore;
    private readonly ILogger<RegistryController> _logger;
    
    public RegistryController(InMemoryDataStore dataStore, ILogger<RegistryController> logger)
    {
        _dataStore = dataStore;
        _logger = logger;
    }
    
    [HttpGet("producers")]
    public IActionResult GetProducers([FromQuery] DateTime? effectiveDate = null)
    {
        var date = effectiveDate ?? DateTime.UtcNow;
        var producers = _dataStore.GetActiveProducers(date);
        
        _logger.LogInformation(
            "Producer list requested for {Date}: {Count} producers",
            date, producers.Count);
        
        return Ok(producers);
    }
    
    [HttpGet("epoch")]
    public IActionResult GetEpoch([FromQuery] DateTime? date = null)
    {
        var effectiveDate = date ?? DateTime.UtcNow;
        var epoch = _dataStore.GetEpochForDate(effectiveDate);
        
        if (epoch == null)
            return NotFound("No epoch found for date");
        
        _logger.LogInformation(
            "Epoch requested for {Date}: Epoch {EpochId} with {Count} producers",
            effectiveDate, epoch.EpochId, epoch.ProducerCount);
        
        return Ok(epoch);
    }
    
    [HttpPost("producers")]
    public IActionResult AddProducer([FromBody] AddProducerRequest request)
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
        
        _dataStore.AddProducer(producer);
        
        // Create new epoch
        var activeProducers = _dataStore.GetActiveProducers(startDate);
        var newEpoch = new ProducerEpoch
        {
            EpochId = _dataStore.GetEpochForDate(DateTime.UtcNow).EpochId + 1,
            StartDate = startDate,
            EndDate = null,
            ProducerIds = activeProducers.Select(p => p.ProducerId).OrderBy(id => id).ToList(),
            ProducerCount = activeProducers.Count
        };
        
        _dataStore.CreateEpoch(newEpoch);
        
        _logger.LogInformation(
            "Added producer {ProducerId}, effective {StartDate}. New epoch {EpochId} created.",
            request.ProducerId, startDate, newEpoch.EpochId);
        
        return Ok(new { message = "Producer added", epoch = newEpoch });
    }
}

public class AddProducerRequest
{
    public string ProducerId { get; set; }
    public string DisplayName { get; set; }
    public DateTime StartDate { get; set; }
}
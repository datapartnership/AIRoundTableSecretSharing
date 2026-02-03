// Controllers/MetricsController.cs
using AIRoundTableSecretSharingAPI.Services;
using AIRoundTableSecretSharingCommon.Models;
using Microsoft.AspNetCore.Mvc;

namespace AIRoundTableSecretSharingAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MetricsController : ControllerBase
{
    private readonly InMemoryDataStore _dataStore;
    private readonly ILogger<MetricsController> _logger;
    
    public MetricsController(InMemoryDataStore dataStore, ILogger<MetricsController> logger)
    {
        _dataStore = dataStore;
        _logger = logger;
    }
    
    [HttpPost("submit")]
    public IActionResult SubmitMetric([FromBody] MetricSubmission submission)
    {
        // Normalize month to first day
        var monthStart = new DateTime(submission.Month.Year, submission.Month.Month, 1);
        submission.Month = monthStart;
        submission.SubmittedAt = DateTime.UtcNow;
        
        // Validate epoch
        var epoch = _dataStore.GetEpochForDate(monthStart);
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
        var added = _dataStore.AddSubmission(submission);
        if (!added)
        {
            return Conflict("Duplicate submission");
        }
        
        var weightedInfo = submission.WeightedValue.HasValue 
            ? $", WeightedValue = {submission.WeightedValue.Value:N0}" 
            : "";
        
        _logger.LogInformation(
            "RECEIVED submission from {Producer} for {Country} - {Month}: Value = {Value:N0}{WeightedInfo}",
            submission.ProducerId, submission.Country, submission.Month, submission.Value, weightedInfo);
        
        return Ok(new { message = "Submission received" });
    }
    
    [HttpGet("aggregate")]
    public IActionResult GetAggregate(
        [FromQuery] string country,
        [FromQuery] DateTime month)
    {
        var monthStart = new DateTime(month.Year, month.Month, 1);
        var epoch = _dataStore.GetEpochForDate(monthStart);
        
        if (epoch == null)
            return NotFound("No epoch for date");
        
        var submissions = _dataStore.GetSubmissions(country, monthStart, epoch.EpochId);
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
                WeightedTotal = null,
                SubmissionCount = submissions.Count,
                ExpectedSubmissions = epoch.ProducerCount,
                MissingProducers = missingProducers
            });
        }
        
        // All submissions received - compute aggregates
        long total = submissions.Sum(s => s.Value);
        
        // Compute weighted total if all submissions have weighted values
        long? weightedTotal = null;
        double? weightedRatio = null;
        
        if (submissions.All(s => s.WeightedValue.HasValue))
        {
            weightedTotal = submissions.Sum(s => s.WeightedValue!.Value);
            // Compute weighted ratio: TotalWeightedMAU / TotalMAU
            // This gives the aggregate weighted coefficient
            weightedRatio = total > 0 ? (double)weightedTotal / total : null;
            
            _logger.LogInformation(
                "AGGREGATION COMPLETE for {Country} - {Month}: Total = {Total:N0}, WeightedTotal = {WeightedTotal:N0}, Ratio = {Ratio:F4} (noise canceled!)",
                country, month, total, weightedTotal, weightedRatio);
        }
        else
        {
            _logger.LogInformation(
                "AGGREGATION COMPLETE for {Country} - {Month}: Total = {Total:N0} (noise canceled!)",
                country, month, total);
        }
        
        return Ok(new AggregationResult
        {
            Status = "complete",
            Country = country,
            Month = monthStart,
            Total = total,
            WeightedTotal = weightedTotal,
            WeightedRatio = weightedRatio,
            SubmissionCount = submissions.Count,
            ExpectedSubmissions = epoch.ProducerCount,
            MissingProducers = new List<string>()
        });
    }
}
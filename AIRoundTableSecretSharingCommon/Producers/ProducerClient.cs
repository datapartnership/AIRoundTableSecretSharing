using System.Net.Http.Json;
using AIRoundTableSecretSharingCommon.Core;
using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingCommon.Producers;
public class ProducerClient
{
    private readonly string _producerId;
    private readonly string _displayName;
    private readonly HttpClient _httpClient;
    
    public ProducerClient(string producerId, string displayName, string apiBaseUrl)
    {
        _producerId = producerId;
        _displayName = displayName;
        _httpClient = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };
    }
    
    public async Task<SubmissionResult> SubmitMetric(
        string country,
        DateTime month,
        long actualValue)
    {
        Console.WriteLine();
        Console.WriteLine($"═══════════════════════════════════════════════════");
        Console.WriteLine($"  {_displayName} ({_producerId})");
        Console.WriteLine($"═══════════════════════════════════════════════════");
        Console.WriteLine($"Country: {country}");
        Console.WriteLine($"Month: {month:yyyy-MM}");
        Console.WriteLine($"Actual Value: {actualValue:N0}");
        Console.WriteLine();
        
        // Step 1: Get producer list from Development Data Partnership (NO COMMUNICATION WITH OTHER PRODUCERS!)
        var monthStart = new DateTime(month.Year, month.Month, 1);
        
        Console.WriteLine("Step 1: Fetching producer list from Development Data Partnership API...");
        var producersResponse = await _httpClient.GetAsync(
            $"/api/registry/producers?effectiveDate={monthStart:yyyy-MM-dd}");
        
        if (!producersResponse.IsSuccessStatusCode)
        {
            return new SubmissionResult
            {
                Success = false,
                Message = "Failed to fetch producer list"
            };
        }
        
        var producers = await producersResponse.Content.ReadFromJsonAsync<List<ProducerInfo>>();
        var producerIds = producers.Select(p => p.ProducerId).OrderBy(id => id).ToList();
        
        Console.WriteLine($"  Active producers: {string.Join(", ", producerIds)}");
        Console.WriteLine();
        
        // Verify we're in the list
        if (!producerIds.Contains(_producerId))
        {
            return new SubmissionResult
            {
                Success = false,
                Message = "Not an active producer for this period"
            };
        }
        
        // Step 2: Get epoch
        var epochResponse = await _httpClient.GetAsync(
            $"/api/registry/epoch?date={monthStart:yyyy-MM-dd}");
        var epoch = await epochResponse.Content.ReadFromJsonAsync<ProducerEpoch>();
        
        Console.WriteLine($"Step 2: Current epoch is {epoch.EpochId} with {epoch.ProducerCount} producers");
        Console.WriteLine();
        
        // Step 3: Calculate noise with each other producer (INDEPENDENTLY!)
        Console.WriteLine("Step 3: Calculating noise with other producers...");
        Console.WriteLine("  (NO COMMUNICATION - using deterministic function)");
        Console.WriteLine();
        
        long maskedValue = actualValue;
        var noiseBreakdown = new Dictionary<string, long>();
        
        foreach (var otherProducerId in producerIds.Where(id => id != _producerId))
        {
            // CRITICAL: Both producers compute the SAME noise independently
            // IMPORTANT: maxNoise must be the SAME for all partners to ensure cancellation!
            var noise = DeterministicNoiseGenerator.GenerateNoise(
                _producerId,
                otherProducerId,
                country,
                monthStart,
                maxNoise: 100_000_000 // Fixed value agreed by all partners
            );
            
            // Determine sign based on alphabetical order
            var sign = DeterministicNoiseGenerator.GetNoiseSign(_producerId, otherProducerId);
            var appliedNoise = noise * sign;
            
            maskedValue += appliedNoise;
            noiseBreakdown[otherProducerId] = appliedNoise;
            
            Console.WriteLine($"  With {otherProducerId}:");
            Console.WriteLine($"    Raw noise: {noise:N0}");
            Console.WriteLine($"    Sign: {(sign > 0 ? "+" : "-")} (alphabetical: {_producerId} vs {otherProducerId})");
            Console.WriteLine($"    Applied: {appliedNoise:+#,0;-#,0}");
        }
        
        Console.WriteLine();
        Console.WriteLine($"Total noise applied: {(maskedValue - actualValue):+#,0;-#,0}");
        Console.WriteLine($"Masked value to submit: {maskedValue:N0}");
        Console.WriteLine();
        
        // Step 4: Submit to Development Data Partnership
        Console.WriteLine("Step 4: Submitting masked value to Development Data Partnership...");
        
        var submission = new MetricSubmission
        {
            ProducerId = _producerId,
            Country = country,
            Month = monthStart.ToString("yyyy-MM"),
            Value = maskedValue,
            EpochId = epoch.EpochId,
            Signature = "demo-signature",
            SubmittedAt = DateTime.UtcNow
        };
        
        var submitResponse = await _httpClient.PostAsJsonAsync("/api/metrics/submit", submission);
        
        if (!submitResponse.IsSuccessStatusCode)
        {
            var error = await submitResponse.Content.ReadAsStringAsync();
            Console.WriteLine($"  ❌ FAILED: {error}");
            
            return new SubmissionResult
            {
                Success = false,
                ProducerId = _producerId,
                Message = error
            };
        }
        
        Console.WriteLine($"  ✅ SUCCESS!");
        Console.WriteLine($"═══════════════════════════════════════════════════");
        
        return new SubmissionResult
        {
            Success = true,
            ProducerId = _producerId,
            Message = "Submitted successfully",
            OriginalValue = actualValue,
            MaskedValue = maskedValue,
            NoiseApplied = maskedValue - actualValue,
            NoiseBreakdown = noiseBreakdown
        };
    }
}
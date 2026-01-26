using System.Net.Http.Json;
using AIRoundTableSecretSharingCommon.Models;
using AIRoundTableSecretSharingCommon.Producers;

namespace AIRoundTableSecretSharingDemo;

class Program
{
    private const string ApiBaseUrl = "http://localhost:5149";
    
    static async Task Main(string[] args)
    {
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║     Privacy-Preserving Data Aggregation Demo                     ║");
        Console.WriteLine("║     Using Pairwise Noise Cancellation                            ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        
        // Configuration
        var country = "USA";
        var month = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        
        // Actual values (these are SECRET - only each partner knows their own)
        var partnerValues = new Dictionary<string, long>
        {
            { "partnerA", 1_000_000 },  // Partner A has 1 million users
            { "partnerB", 500_000 },    // Partner B has 500k users
            { "partnerC", 200_000 }     // Partner C has 200k users
        };
        
        var expectedTotal = partnerValues.Values.Sum();
        
        Console.WriteLine("┌─────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│  SCENARIO: World Bank collecting monthly active users          │");
        Console.WriteLine("│  Partners want to share aggregate WITHOUT revealing individual │");
        Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");
        Console.WriteLine();
        Console.WriteLine($"  Country: {country}");
        Console.WriteLine($"  Month: {month:yyyy-MM}");
        Console.WriteLine();
        Console.WriteLine("  ACTUAL VALUES (secret - only each partner knows their own):");
        Console.WriteLine("  ──────────────────────────────────────────────────────────────");
        foreach (var (partner, value) in partnerValues)
        {
            Console.WriteLine($"    {partner}: {value:N0}");
        }
        Console.WriteLine($"  ──────────────────────────────────────────────────────────────");
        Console.WriteLine($"    EXPECTED TOTAL: {expectedTotal:N0}");
        Console.WriteLine();
        
        // Check if API is running
        Console.WriteLine("Checking API connection...");
        using var httpClient = new HttpClient { BaseAddress = new Uri(ApiBaseUrl) };
        
        try
        {
            var epochResponse = await httpClient.GetAsync("/api/registry/epoch");
            if (!epochResponse.IsSuccessStatusCode)
            {
                Console.WriteLine();
                Console.WriteLine("ERROR: Cannot connect to API. Please make sure the API is running:");
                Console.WriteLine("  cd AIRoundTableSecretSharingAPI && dotnet run");
                Console.WriteLine();
                return;
            }
            Console.WriteLine("  ✓ API is running");
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"ERROR: Cannot connect to API at {ApiBaseUrl}");
            Console.WriteLine($"  {ex.Message}");
            Console.WriteLine();
            Console.WriteLine("Please make sure the API is running:");
            Console.WriteLine("  cd AIRoundTableSecretSharingAPI && dotnet run");
            Console.WriteLine();
            return;
        }
        
        Console.WriteLine("Press any key to start the demo...");
        Console.ReadKey(true);
        Console.WriteLine();
        
        // Submit from each partner
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  PHASE 1: Each partner submits their MASKED value               ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        
        var submissions = new List<SubmissionResult>();
        
        foreach (var (partnerId, actualValue) in partnerValues)
        {
            var client = new ProducerClient(partnerId, partnerId.ToUpper(), ApiBaseUrl);
            var result = await client.SubmitMetric(country, month, actualValue);
            submissions.Add(result);
            
            Console.WriteLine();
            Console.WriteLine("Press any key to continue to next partner...");
            Console.ReadKey(true);
        }
        
        // Summary of submissions
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  SUBMISSION SUMMARY                                              ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine("  Partner     │ Actual Value │ Noise Applied │ Masked Value");
        Console.WriteLine("  ────────────┼──────────────┼───────────────┼──────────────");
        
        long totalMasked = 0;
        long totalNoise = 0;
        
        foreach (var result in submissions.Where(r => r.Success))
        {
            Console.WriteLine($"  {result.ProducerId,-11} │ {result.OriginalValue,12:N0} │ {result.NoiseApplied,+13:N0} │ {result.MaskedValue,12:N0}");
            totalMasked += result.MaskedValue;
            totalNoise += result.NoiseApplied;
        }
        
        Console.WriteLine("  ────────────┼──────────────┼───────────────┼──────────────");
        Console.WriteLine($"  {"TOTAL",-11} │ {expectedTotal,12:N0} │ {totalNoise,+13:N0} │ {totalMasked,12:N0}");
        Console.WriteLine();
        
        if (totalNoise == 0)
        {
            Console.WriteLine("  ✓ Notice: Total noise = 0 (noise canceled client-side too!)");
        }
        else
        {
            Console.WriteLine($"  Note: Client-side noise sum = {totalNoise:N0} (should be 0 when aggregated)");
        }
        
        Console.WriteLine();
        Console.WriteLine("Press any key to fetch the aggregated result from the API...");
        Console.ReadKey(true);
        
        // Fetch aggregate
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  PHASE 2: Aggregator computes the total                         ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        
        var aggregateResponse = await httpClient.GetAsync(
            $"/api/metrics/aggregate?country={country}&month={month:yyyy-MM-dd}");
        
        if (aggregateResponse.IsSuccessStatusCode)
        {
            var aggregate = await aggregateResponse.Content.ReadFromJsonAsync<AggregationResult>();
            
            if (aggregate?.Status == "complete")
            {
                Console.WriteLine("  ┌─────────────────────────────────────────────────────────────┐");
                Console.WriteLine($"  │  AGGREGATED TOTAL: {aggregate.Total,12:N0}                        │");
                Console.WriteLine("  └─────────────────────────────────────────────────────────────┘");
                Console.WriteLine();
                
                if (aggregate.Total == expectedTotal)
                {
                    Console.WriteLine("  ✅ SUCCESS! The aggregate matches the expected total!");
                    Console.WriteLine();
                    Console.WriteLine("  The aggregator learned ONLY the total ({0:N0}).", aggregate.Total);
                    Console.WriteLine("  Individual partner values remain PRIVATE!");
                    Console.WriteLine();
                    Console.WriteLine("  What the aggregator sees:");
                    Console.WriteLine("    - Masked values that look random");
                    Console.WriteLine("    - No way to determine individual actual values");
                    Console.WriteLine("    - Only the sum is meaningful (noise cancels out)");
                }
                else
                {
                    Console.WriteLine($"  ⚠️  Aggregate ({aggregate.Total:N0}) does not match expected ({expectedTotal:N0})");
                    Console.WriteLine("     This indicates a bug in the noise cancellation.");
                }
            }
            else
            {
                Console.WriteLine($"  Status: {aggregate?.Status}");
                Console.WriteLine($"  Submissions: {aggregate?.SubmissionCount} / {aggregate?.ExpectedSubmissions}");
                if (aggregate?.MissingProducers?.Any() == true)
                {
                    Console.WriteLine($"  Missing: {string.Join(", ", aggregate.MissingProducers)}");
                }
            }
        }
        else
        {
            Console.WriteLine($"  Failed to fetch aggregate: {aggregateResponse.StatusCode}");
        }
        
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  Demo complete!                                                  ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
    }
}

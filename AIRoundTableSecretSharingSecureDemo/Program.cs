using System.Net.Http.Json;
using AIRoundTableSecretSharingCommon.Core;
using AIRoundTableSecretSharingCommon.Models;
using AIRoundTableSecretSharingCommon.Producers;

namespace AIRoundTableSecretSharingSecureDemo;

class Program
{
    private const string ApiBaseUrl = "http://localhost:5149";
    
    static async Task Main(string[] args)
    {
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║     SECURE Privacy-Preserving Data Aggregation Demo                      ║");
        Console.WriteLine("║     Using Diffie-Hellman Key Exchange                                    ║");
        Console.WriteLine("║     WITH DUAL METRICS: MAU + Weighted MAU                                ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  This version is ACTUALLY SECURE - the aggregator cannot determine       ║");
        Console.WriteLine("║  individual values OR coefficients because it doesn't know the secrets.  ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        
        // Configuration
        var country = "USA";
        var month = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        
        // Actual values (these are SECRET - only each partner knows their own)
        // Each partner also has a SECRET coefficient used for weighted calculations
        var partnerData = new Dictionary<string, (long MAU, double Coefficient)>
        {
            { "partnerA", (1_000_000, 1.5) },   // Partner A: 1M users, coefficient 1.5
            { "partnerB", (500_000, 2.0) },     // Partner B: 500k users, coefficient 2.0
            { "partnerC", (200_000, 0.8) }      // Partner C: 200k users, coefficient 0.8
        };
        
        var expectedMAUTotal = partnerData.Values.Sum(v => v.MAU);
        var expectedWeightedTotal = partnerData.Values.Sum(v => (long)(v.MAU * v.Coefficient));
        
        Console.WriteLine("┌──────────────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│  SCENARIO: World Bank collecting dual metrics                           │");
        Console.WriteLine("│  1) Monthly Active Users (MAU) - raw count                              │");
        Console.WriteLine("│  2) Weighted MAU (MAU × coefficient) - e.g., quality-adjusted users     │");
        Console.WriteLine("│  Partners want to share aggregates WITHOUT revealing individual values  │");
        Console.WriteLine("│  OR their secret coefficients!                                          │");
        Console.WriteLine("└──────────────────────────────────────────────────────────────────────────┘");
        Console.WriteLine();
        Console.WriteLine($"  Country: {country}");
        Console.WriteLine($"  Month: {month:yyyy-MM}");
        Console.WriteLine();
        Console.WriteLine("  ACTUAL VALUES (secret - only each partner knows their own):");
        Console.WriteLine("  ────────────────────────────────────────────────────────────────────────");
        Console.WriteLine("  Partner      │   MAU       │ Coefficient │ Weighted MAU");
        Console.WriteLine("  ─────────────┼─────────────┼─────────────┼─────────────");
        foreach (var (partner, data) in partnerData)
        {
            var weighted = (long)(data.MAU * data.Coefficient);
            Console.WriteLine($"  {partner,-12} │ {data.MAU,11:N0} │ {data.Coefficient,11:F2} │ {weighted,11:N0}");
        }
        Console.WriteLine("  ─────────────┼─────────────┼─────────────┼─────────────");
        Console.WriteLine($"  {"TOTAL",-12} │ {expectedMAUTotal,11:N0} │      -      │ {expectedWeightedTotal,11:N0}");
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
        
        Console.WriteLine("Press any key to start the secure demo...");
        Console.ReadKey(true);
        Console.WriteLine();
        
        // ═══════════════════════════════════════════════════════════════════════════
        // PHASE 1: Key Exchange (Diffie-Hellman)
        // ═══════════════════════════════════════════════════════════════════════════
        
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  PHASE 1: Diffie-Hellman Key Exchange                                   ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  Each partner generates a key pair and registers their PUBLIC key.       ║");
        Console.WriteLine("║  Private keys NEVER leave the partners' systems.                         ║");
        Console.WriteLine("║  The aggregator facilitates exchange but CANNOT compute shared secrets.  ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝"
        Console.WriteLine();
        
        // Create secure producer clients
        var clients = new Dictionary<string, SecureProducerClient>();
        foreach (var partnerId in partnerData.Keys)
        {
            clients[partnerId] = new SecureProducerClient(partnerId, partnerId.ToUpper(), ApiBaseUrl);
        }
        
        // Step 1a: Each partner registers their public key
        Console.WriteLine("Step 1a: Each partner registers their public key");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────────────");
        foreach (var (partnerId, client) in clients)
        {
            await client.RegisterPublicKeyAsync();
        }
        Console.WriteLine();
        
        Console.WriteLine("Press any key to perform key exchange...");
        Console.ReadKey(true);
        Console.WriteLine();
        
        // Step 1b: Each partner fetches others' keys and computes shared secrets
        Console.WriteLine("Step 1b: Each partner computes shared secrets with all other partners");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────────────");
        Console.WriteLine();
        
        foreach (var (partnerId, client) in clients)
        {
            await client.PerformKeyExchangeAsync();
        }
        
        // Demonstrate that shared secrets match
        Console.WriteLine();
        Console.WriteLine("┌──────────────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│  KEY INSIGHT: Partners A and B compute the SAME shared secret!          │");
        Console.WriteLine("│                                                                          │");
        Console.WriteLine("│  PartnerA computes: shared_AB = B_public ^ A_private                    │");
        Console.WriteLine("│  PartnerB computes: shared_AB = A_public ^ B_private                    │");
        Console.WriteLine("│                                                                          │");
        Console.WriteLine("│  These are EQUAL due to the Diffie-Hellman property:                    │");
        Console.WriteLine("│    (g^a)^b = (g^b)^a = g^(ab)                                           │");
        Console.WriteLine("│                                                                          │");
        Console.WriteLine("│  The aggregator sees only public keys (g^a, g^b) but cannot compute     │");
        Console.WriteLine("│  g^(ab) without knowing a or b (computational Diffie-Hellman problem).  │");
        Console.WriteLine("└──────────────────────────────────────────────────────────────────────────┘");
        Console.WriteLine();
        
        Console.WriteLine("Press any key to start secure submissions...");
        Console.ReadKey(true);
        Console.WriteLine();
        
        // ═══════════════════════════════════════════════════════════════════════════
        // PHASE 2: Secure Submissions
        // ═══════════════════════════════════════════════════════════════════════════
        
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  PHASE 2: Secure Dual Metric Submissions                                ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  Each partner computes noise for BOTH metrics using shared secrets:     ║");
        Console.WriteLine("║  1. MAU - masked with one set of noise                                  ║");
        Console.WriteLine("║  2. Weighted MAU - masked with independent noise (same secrets)         ║");
        Console.WriteLine("║  The aggregator receives masked values but CANNOT reverse the noise.    ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝");
        
        var submissions = new List<SubmissionResult>();
        
        foreach (var (partnerId, client) in clients)
        {
            var data = partnerData[partnerId];
            var result = await client.SubmitMetricSecure(country, month, data.MAU, data.Coefficient);
            submissions.Add(result);
            
            Console.WriteLine();
            Console.WriteLine("Press any key to continue to next partner...");
            Console.ReadKey(true);
        }
        
        // ═══════════════════════════════════════════════════════════════════════════
        // Summary and Verification
        // ═══════════════════════════════════════════════════════════════════════════
        
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  SUBMISSION SUMMARY                                                      ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine("  METRIC 1: Monthly Active Users (MAU)");
        Console.WriteLine("  Partner     │ Actual MAU   │ MAU Noise      │ Masked MAU");
        Console.WriteLine("  ────────────┼──────────────┼────────────────┼──────────────");
        
        long totalMaskedMAU = 0;
        long totalMAUNoise = 0;
        
        foreach (var result in submissions.Where(r => r.Success))
        {
            Console.WriteLine($"  {result.ProducerId,-11} │ {result.OriginalValue,12:N0} │ {result.NoiseApplied,+14:N0} │ {result.MaskedValue,12:N0}");
            totalMaskedMAU += result.MaskedValue;
            totalMAUNoise += result.NoiseApplied;
        }
        
        Console.WriteLine("  ────────────┼──────────────┼────────────────┼──────────────");
        Console.WriteLine($"  {"TOTAL",-11} │ {expectedMAUTotal,12:N0} │ {totalMAUNoise,+14:N0} │ {totalMaskedMAU,12:N0}");
        Console.WriteLine();
        
        if (totalMAUNoise == 0)
        {
            Console.WriteLine("  ✓ MAU Total noise = 0 (noise cancels perfectly!)");
        }
        
        Console.WriteLine();
        Console.WriteLine("  METRIC 2: Weighted MAU (MAU × Coefficient)");
        Console.WriteLine("  Partner     │ Actual Wtd   │ Wtd Noise      │ Masked Wtd");
        Console.WriteLine("  ────────────┼──────────────┼────────────────┼──────────────");
        
        long totalMaskedWeighted = 0;
        long totalWeightedNoise = 0;
        
        foreach (var result in submissions.Where(r => r.Success && r.MaskedWeightedValue.HasValue))
        {
            Console.WriteLine($"  {result.ProducerId,-11} │ {result.OriginalWeightedValue,12:N0} │ {result.WeightedNoiseApplied,+14:N0} │ {result.MaskedWeightedValue,12:N0}");
            totalMaskedWeighted += result.MaskedWeightedValue!.Value;
            totalWeightedNoise += result.WeightedNoiseApplied!.Value;
        }
        
        Console.WriteLine("  ────────────┼──────────────┼────────────────┼──────────────");
        Console.WriteLine($"  {"TOTAL",-11} │ {expectedWeightedTotal,12:N0} │ {totalWeightedNoise,+14:N0} │ {totalMaskedWeighted,12:N0}");
        Console.WriteLine();
        
        if (totalWeightedNoise == 0)
        {
            Console.WriteLine("  ✓ Weighted Total noise = 0 (noise cancels perfectly!)");
        }
        
        Console.WriteLine();
        Console.WriteLine("Press any key to fetch the aggregated result from the API...");
        Console.ReadKey(true);
        
        // ═══════════════════════════════════════════════════════════════════════════
        // PHASE 3: Aggregation
        // ═══════════════════════════════════════════════════════════════════════════
        
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  PHASE 3: Aggregation                                                   ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  The aggregator sums all masked values for BOTH metrics.                ║");
        Console.WriteLine("║  Noise cancels for each metric independently, revealing only true totals║");
        Console.WriteLine("║  Individual values AND coefficients remain CRYPTOGRAPHICALLY PROTECTED. ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        
        var aggregateResponse = await httpClient.GetAsync(
            $"/api/metrics/aggregate?country={country}&month={month:yyyy-MM-dd}");
        
        if (aggregateResponse.IsSuccessStatusCode)
        {
            var aggregate = await aggregateResponse.Content.ReadFromJsonAsync<AggregationResult>();
            
            if (aggregate?.Status == "complete")
            {
                Console.WriteLine("  ┌────────────────────────────────────────────────────────────────────┐");
                Console.WriteLine($"  │  AGGREGATED MAU TOTAL:      {aggregate.Total,15:N0}                    │");
                Console.WriteLine($"  │  AGGREGATED WEIGHTED TOTAL: {aggregate.WeightedTotal,15:N0}                    │");
                Console.WriteLine("  └────────────────────────────────────────────────────────────────────┘");
                Console.WriteLine();
                
                var mauMatches = aggregate.Total == expectedMAUTotal;
                var weightedMatches = aggregate.WeightedTotal == expectedWeightedTotal;
                
                if (mauMatches && weightedMatches)
                {
                    Console.WriteLine("  ╔════════════════════════════════════════════════════════════════════╗");
                    Console.WriteLine("  ║  ✅ SUCCESS! Both aggregates match the expected totals!            ║");
                    Console.WriteLine("  ╠════════════════════════════════════════════════════════════════════╣");
                    Console.WriteLine("  ║                                                                    ║");
                    Console.WriteLine("  ║  SECURITY ANALYSIS:                                                ║");
                    Console.WriteLine("  ║                                                                    ║");
                    Console.WriteLine("  ║  • The aggregator learned ONLY the totals:                        ║");
                    Console.WriteLine($"  ║    - MAU Total: {expectedMAUTotal:N0}                                     ║");
                    Console.WriteLine($"  ║    - Weighted Total: {expectedWeightedTotal:N0}                               ║");
                    Console.WriteLine("  ║  • Individual partner MAU values: PROTECTED                       ║");
                    Console.WriteLine("  ║  • Partner coefficients: COMPLETELY HIDDEN                        ║");
                    Console.WriteLine("  ║  • The aggregator CANNOT:                                         ║");
                    Console.WriteLine("  ║    - Compute the shared secrets (DH problem is hard)              ║");
                    Console.WriteLine("  ║    - Reverse the noise without the secrets                        ║");
                    Console.WriteLine("  ║    - Determine any partner's MAU or coefficient                   ║");
                    Console.WriteLine("  ║                                                                    ║");
                    Console.WriteLine("  ║  This is TRUE privacy-preserving dual-metric aggregation!         ║");
                    Console.WriteLine("  ╚════════════════════════════════════════════════════════════════════╝");
                }
                else
                {
                    if (!mauMatches)
                        Console.WriteLine($"  ⚠️  MAU Aggregate ({aggregate.Total:N0}) does not match expected ({expectedMAUTotal:N0})");
                    if (!weightedMatches)
                        Console.WriteLine($"  ⚠️  Weighted Aggregate ({aggregate.WeightedTotal:N0}) does not match expected ({expectedWeightedTotal:N0})");
                }
            }
            else
            {
                Console.WriteLine($"  Status: {aggregate?.Status}");
                Console.WriteLine($"  Submissions: {aggregate?.SubmissionCount} / {aggregate?.ExpectedSubmissions}");
            }
        }
        
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  Secure dual-metric demo complete!                                       ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        
        // Cleanup
        foreach (var client in clients.Values)
        {
            client.Dispose();
        }
    }
}

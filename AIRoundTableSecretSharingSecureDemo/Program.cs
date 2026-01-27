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
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  This version is ACTUALLY SECURE - the aggregator cannot determine       ║");
        Console.WriteLine("║  individual values because it doesn't know the shared secrets.           ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝");
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
        
        Console.WriteLine("┌──────────────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│  SCENARIO: World Bank collecting monthly active users                   │");
        Console.WriteLine("│  Partners want to share aggregate WITHOUT revealing individual values   │");
        Console.WriteLine("└──────────────────────────────────────────────────────────────────────────┘");
        Console.WriteLine();
        Console.WriteLine($"  Country: {country}");
        Console.WriteLine($"  Month: {month:yyyy-MM}");
        Console.WriteLine();
        Console.WriteLine("  ACTUAL VALUES (secret - only each partner knows their own):");
        Console.WriteLine("  ────────────────────────────────────────────────────────────────────────");
        foreach (var (partner, value) in partnerValues)
        {
            Console.WriteLine($"    {partner}: {value:N0}");
        }
        Console.WriteLine($"  ────────────────────────────────────────────────────────────────────────");
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
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        
        // Create secure producer clients
        var clients = new Dictionary<string, SecureProducerClient>();
        foreach (var partnerId in partnerValues.Keys)
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
        Console.WriteLine("║  PHASE 2: Secure Submissions                                            ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  Each partner computes noise from shared secrets and submits masked     ║");
        Console.WriteLine("║  values. The aggregator receives values but CANNOT reverse the noise.   ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝");
        
        var submissions = new List<SubmissionResult>();
        
        foreach (var (partnerId, client) in clients)
        {
            var result = await client.SubmitMetricSecure(country, month, partnerValues[partnerId]);
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
        Console.WriteLine("  Partner     │ Actual Value │ Noise Applied  │ Masked Value");
        Console.WriteLine("  ────────────┼──────────────┼────────────────┼──────────────");
        
        long totalMasked = 0;
        long totalNoise = 0;
        
        foreach (var result in submissions.Where(r => r.Success))
        {
            Console.WriteLine($"  {result.ProducerId,-11} │ {result.OriginalValue,12:N0} │ {result.NoiseApplied,+14:N0} │ {result.MaskedValue,12:N0}");
            totalMasked += result.MaskedValue;
            totalNoise += result.NoiseApplied;
        }
        
        Console.WriteLine("  ────────────┼──────────────┼────────────────┼──────────────");
        Console.WriteLine($"  {"TOTAL",-11} │ {expectedTotal,12:N0} │ {totalNoise,+14:N0} │ {totalMasked,12:N0}");
        Console.WriteLine();
        
        if (totalNoise == 0)
        {
            Console.WriteLine("  ✓ Total noise = 0 (noise cancels perfectly!)");
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
        Console.WriteLine("║  The aggregator sums all masked values. Noise cancels, revealing only   ║");
        Console.WriteLine("║  the true total. Individual values remain CRYPTOGRAPHICALLY PROTECTED.  ║");
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
                Console.WriteLine($"  │  AGGREGATED TOTAL: {aggregate.Total,15:N0}                          │");
                Console.WriteLine("  └────────────────────────────────────────────────────────────────────┘");
                Console.WriteLine();
                
                if (aggregate.Total == expectedTotal)
                {
                    Console.WriteLine("  ╔════════════════════════════════════════════════════════════════════╗");
                    Console.WriteLine("  ║  ✅ SUCCESS! The aggregate matches the expected total!             ║");
                    Console.WriteLine("  ╠════════════════════════════════════════════════════════════════════╣");
                    Console.WriteLine("  ║                                                                    ║");
                    Console.WriteLine("  ║  SECURITY ANALYSIS:                                                ║");
                    Console.WriteLine("  ║                                                                    ║");
                    Console.WriteLine("  ║  • The aggregator learned ONLY the total (1,700,000)              ║");
                    Console.WriteLine("  ║  • Individual partner values are CRYPTOGRAPHICALLY PROTECTED      ║");
                    Console.WriteLine("  ║  • The aggregator saw masked values but CANNOT:                   ║");
                    Console.WriteLine("  ║    - Compute the shared secrets (DH problem is hard)              ║");
                    Console.WriteLine("  ║    - Reverse the noise without the secrets                        ║");
                    Console.WriteLine("  ║    - Determine any individual partner's actual value              ║");
                    Console.WriteLine("  ║                                                                    ║");
                    Console.WriteLine("  ║  This is TRUE privacy-preserving aggregation!                     ║");
                    Console.WriteLine("  ╚════════════════════════════════════════════════════════════════════╝");
                }
                else
                {
                    Console.WriteLine($"  ⚠️  Aggregate ({aggregate.Total:N0}) does not match expected ({expectedTotal:N0})");
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
        Console.WriteLine("║  Secure demo complete!                                                   ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        
        // Cleanup
        foreach (var client in clients.Values)
        {
            client.Dispose();
        }
    }
}

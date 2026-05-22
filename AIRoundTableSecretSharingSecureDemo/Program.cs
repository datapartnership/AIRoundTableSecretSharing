using System.Net.Http.Json;
using AIRoundTableSecretSharingCommon.Models;
using AIRoundTableSecretSharingCommon.Producers;

namespace AIRoundTableSecretSharingSecureDemo;

class Program
{
    private const string ApiBaseUrl = "http://localhost:5149";

    private static readonly Dictionary<string, string> ApiKeys = new()
    {
        { "partnerA", "pA-secret-key-2026-abc123" },
        { "partnerB", "pB-secret-key-2026-def456" },
        { "partnerC", "pC-secret-key-2026-ghi789" }
    };

    static async Task Main(string[] args)
    {
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║     SECURE Privacy-Preserving Data Aggregation Demo                      ║");
        Console.WriteLine("║     Using ML-KEM-768 (FIPS 203, post-quantum KEM)                        ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  Each pair of partners establishes a shared secret via ML-KEM:           ║");
        Console.WriteLine("║    - Larger partner encapsulates → posts ciphertext to aggregator         ║");
        Console.WriteLine("║    - Smaller partner decapsulates → recovers the same secret             ║");
        Console.WriteLine("║  The aggregator stores only opaque ciphertexts; it CANNOT recover        ║");
        Console.WriteLine("║  any shared secret or individual metric value.                            ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        var country = "USA";
        var month = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

        var partnerData = new Dictionary<string, long>
        {
            { "partnerA", 1_000_000 },
            { "partnerB",   500_000 },
            { "partnerC",   200_000 }
        };

        var expectedMAUTotal = partnerData.Values.Sum();

        Console.WriteLine("┌──────────────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│  SCENARIO: Development Data Partnership collecting MAU metrics          │");
        Console.WriteLine("│  Partners share aggregate totals WITHOUT revealing individual values     │");
        Console.WriteLine("└──────────────────────────────────────────────────────────────────────────┘");
        Console.WriteLine();
        Console.WriteLine($"  Country: {country}");
        Console.WriteLine($"  Month: {month:yyyy-MM}");
        Console.WriteLine();
        Console.WriteLine("  ACTUAL VALUES (each partner knows only their own):");
        Console.WriteLine("  ─────────────┬─────────────");
        Console.WriteLine("  Partner      │   MAU");
        Console.WriteLine("  ─────────────┼─────────────");
        foreach (var (partner, mau) in partnerData)
            Console.WriteLine($"  {partner,-12} │ {mau,11:N0}");
        Console.WriteLine("  ─────────────┼─────────────");
        Console.WriteLine($"  {"TOTAL",-12} │ {expectedMAUTotal,11:N0}");
        Console.WriteLine();

        // Check API connectivity
        Console.WriteLine("Checking API connection...");
        using var httpClient = new HttpClient { BaseAddress = new Uri(ApiBaseUrl) };
        httpClient.DefaultRequestHeaders.Add("X-API-Key", ApiKeys["partnerA"]);

        try
        {
            var epochResponse = await httpClient.GetAsync("/api/registry/epoch");
            if (!epochResponse.IsSuccessStatusCode)
            {
                Console.WriteLine();
                Console.WriteLine("ERROR: Cannot connect to API. Please start it first:");
                Console.WriteLine("  cd AIRoundTableSecretSharingAPI && dotnet run");
                return;
            }
            Console.WriteLine("  ✓ API is running");
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
            Console.WriteLine("  cd AIRoundTableSecretSharingAPI && dotnet run");
            return;
        }

        Console.WriteLine("Press any key to start the ML-KEM demo...");
        Console.ReadKey(true);
        Console.WriteLine();

        // Create clients
        var clients = new Dictionary<string, SecureProducerClient>();
        foreach (var (partnerId, _) in partnerData)
            clients[partnerId] = new SecureProducerClient(partnerId, partnerId.ToUpper(), ApiBaseUrl, ApiKeys[partnerId]);

        // ═══════════════════════════════════════════════════════════════════════
        // PHASE 1: Each partner registers their ML-KEM encapsulation key
        // ═══════════════════════════════════════════════════════════════════════

        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  PHASE 1: Register ML-KEM-768 Encapsulation Keys                         ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  Each partner generates a keypair locally and posts the encapsulation    ║");
        Console.WriteLine("║  (public) key. Decapsulation (private) keys NEVER leave each partner.   ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        foreach (var (partnerId, client) in clients)
            await client.RegisterPublicKeyAsync();

        Console.WriteLine();
        Console.WriteLine("Press any key to continue to Phase 2 (encapsulation)...");
        Console.ReadKey(true);
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════════════
        // PHASE 2: Alphabetically larger partners encapsulate for smaller ones
        // ═══════════════════════════════════════════════════════════════════════

        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  PHASE 2: Encapsulation — Larger Partner → Smaller Partner               ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  Convention: B encapsulates for A; C encapsulates for A and B.           ║");
        Console.WriteLine("║  Each ciphertext is posted to the aggregator (opaque blob).              ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        foreach (var (partnerId, client) in clients)
            await client.FetchKeysAndEncapsulateAsync();

        Console.WriteLine("Press any key to continue to Phase 3 (decapsulation)...");
        Console.ReadKey(true);
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════════════
        // PHASE 3: Smaller partners fetch ciphertexts and decapsulate
        // ═══════════════════════════════════════════════════════════════════════

        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  PHASE 3: Decapsulation — Smaller Partner Recovers Shared Secret         ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  Each partner fetches ciphertexts addressed to them and decapsulates.    ║");
        Console.WriteLine("║  Only the key holder can recover the secret — not the aggregator.        ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        foreach (var (partnerId, client) in clients)
            await client.DecapsulateIncomingAsync();

        Console.WriteLine();
        Console.WriteLine("┌──────────────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│  KEY INSIGHT: Partners sharing a ciphertext arrive at the SAME secret.  │");
        Console.WriteLine("│                                                                          │");
        Console.WriteLine("│  B calls Encapsulate(pk_A) → (ct_BA, secret_AB)  POST ct_BA            │");
        Console.WriteLine("│  A calls Decapsulate(ct_BA)           → secret_AB                       │");
        Console.WriteLine("│                                                                          │");
        Console.WriteLine("│  The aggregator holds ct_BA but CANNOT recover secret_AB.               │");
        Console.WriteLine("│  This is the KEM security property (IND-CCA2).                          │");
        Console.WriteLine("└──────────────────────────────────────────────────────────────────────────┘");
        Console.WriteLine();

        Console.WriteLine("Press any key to start secure submissions (Phase 4)...");
        Console.ReadKey(true);
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════════════
        // PHASE 4: Submit masked values
        // ═══════════════════════════════════════════════════════════════════════

        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  PHASE 4: Secure Metric Submissions                                      ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  Each partner masks their MAU with HMAC noise from shared secrets.       ║");
        Console.WriteLine("║  Noise signs are opposite between each pair → sums cancel to zero.       ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝");

        var submissions = new List<SubmissionResult>();

        foreach (var (partnerId, client) in clients)
        {
            var mau = partnerData[partnerId];
            var result = await client.SubmitMetricSecure(country, month, mau);
            submissions.Add(result);
            Console.WriteLine();
            Console.WriteLine("Press any key for next partner...");
            Console.ReadKey(true);
        }

        // ─── Summary ────────────────────────────────────────────────────────────

        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  SUBMISSION SUMMARY                                                      ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
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
            Console.WriteLine("  ✓ Total noise = 0 (noise cancels perfectly!)");

        Console.WriteLine();
        Console.WriteLine("Press any key to fetch the aggregated result...");
        Console.ReadKey(true);

        // ─── Aggregation ─────────────────────────────────────────────────────────

        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  AGGREGATION                                                             ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  The aggregator sums all masked values. Noise cancels → true total.     ║");
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
                Console.WriteLine("  └────────────────────────────────────────────────────────────────────┘");
                Console.WriteLine();

                if (aggregate.Total == expectedMAUTotal)
                {
                    Console.WriteLine("  ╔════════════════════════════════════════════════════════════════════╗");
                    Console.WriteLine("  ║  ✅ SUCCESS! Aggregate matches the expected total!                 ║");
                    Console.WriteLine("  ╠════════════════════════════════════════════════════════════════════╣");
                    Console.WriteLine("  ║                                                                    ║");
                    Console.WriteLine("  ║  SECURITY ANALYSIS (ML-KEM-768):                                   ║");
                    Console.WriteLine("  ║                                                                    ║");
                    Console.WriteLine($"  ║  • Aggregator learned ONLY the total: {expectedMAUTotal:N0}           ║");
                    Console.WriteLine("  ║  • Individual MAU values: CRYPTOGRAPHICALLY PROTECTED             ║");
                    Console.WriteLine("  ║  • Aggregator CANNOT:                                             ║");
                    Console.WriteLine("  ║    - Recover shared secrets from stored ciphertexts               ║");
                    Console.WriteLine("  ║    - Reverse HMAC noise without the secrets                       ║");
                    Console.WriteLine("  ║    - Determine any partner's individual MAU                        ║");
                    Console.WriteLine("  ║  • Security holds against quantum adversaries (post-quantum KEM)  ║");
                    Console.WriteLine("  ║                                                                    ║");
                    Console.WriteLine("  ║  This is TRUE post-quantum privacy-preserving aggregation!        ║");
                    Console.WriteLine("  ╚════════════════════════════════════════════════════════════════════╝");
                }
                else
                {
                    Console.WriteLine($"  ⚠️  MAU Aggregate ({aggregate.Total:N0}) does not match expected ({expectedMAUTotal:N0})");
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
        Console.WriteLine("║  ML-KEM demo complete!                                                   ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        foreach (var client in clients.Values)
            client.Dispose();
    }
}

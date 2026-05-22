using System.Net.Http.Headers;
using System.Net.Http.Json;
using AIRoundTableSecretSharingCommon.Core;
using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingCommon.Producers;

/// <summary>
/// Privacy-preserving producer client using ML-KEM-768 (FIPS 203).
///
/// Protocol (alphabetically larger partner encapsulates for the smaller one):
///   Phase 1 — RegisterPublicKeyAsync:       POST own encapsulation key
///   Phase 2 — FetchKeysAndEncapsulateAsync: GET all keys; encapsulate for every alphabetically
///                                           smaller partner; POST ciphertexts to aggregator
///   Phase 3 — DecapsulateIncomingAsync:     GET ciphertexts addressed to self; decapsulate each
///   Phase 4 — SubmitMetricSecure:           compute HMAC noise, POST masked value
///
/// The aggregator never sees a private key or shared secret at any point.
/// </summary>
public class SecureProducerClient : IDisposable
{
    private readonly string _producerId;
    private readonly string _displayName;
    private readonly HttpClient _httpClient;
    private readonly MlKemKeyExchange _kem;

    // Shared secrets keyed by the other partner's ID
    private readonly Dictionary<string, byte[]> _sharedSecrets = new();

    private bool _keysRegistered = false;
    private bool _encapsulationDone = false;
    private bool _decapsulationDone = false;

    private SecureProducerClient(string producerId, string displayName, HttpClient httpClient)
    {
        _producerId = producerId;
        _displayName = displayName;
        _httpClient = httpClient;
        _kem = new MlKemKeyExchange();
    }

    /// <summary>
    /// Creates a client authenticated via OAuth 2.0 client credentials flow.
    /// </summary>
    public static async Task<SecureProducerClient> CreateAsync(
        string producerId, string displayName, string apiBaseUrl,
        string clientId, string clientSecret)
    {
        var httpClient = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };

        var tokenRequest = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret),
        });

        var tokenResponse = await httpClient.PostAsync("/auth/token", tokenRequest);
        tokenResponse.EnsureSuccessStatusCode();

        var tokenData = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>();
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokenData!.AccessToken);

        return new SecureProducerClient(producerId, displayName, httpClient);
    }

    private record TokenResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken
    );

    // ── Phase 1 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// POST our ML-KEM-768 encapsulation key to the aggregator.
    /// </summary>
    public async Task<bool> RegisterPublicKeyAsync()
    {
        Console.WriteLine($"[{_producerId}] Registering ML-KEM-768 encapsulation key...");

        var request = new RegisterKeyRequest
        {
            ProducerId = _producerId,
            PublicKeyBase64 = _kem.GetPublicKeyBase64()
        };

        var response = await _httpClient.PostAsJsonAsync("/api/keyexchange/register", request);

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[{_producerId}] Failed: {await response.Content.ReadAsStringAsync()}");
            return false;
        }

        Console.WriteLine($"[{_producerId}] ✓ Encapsulation key registered (1184 bytes)");
        _keysRegistered = true;
        return true;
    }

    // ── Phase 2 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// GET all partners' encapsulation keys, then encapsulate for every partner
    /// that is alphabetically smaller than us. POST each resulting ciphertext to the aggregator.
    /// Store the corresponding shared secrets locally.
    /// Partners that are alphabetically larger than us will encapsulate for us (Phase 3).
    /// </summary>
    public async Task<bool> FetchKeysAndEncapsulateAsync()
    {
        Console.WriteLine($"[{_producerId}] Fetching partner encapsulation keys...");

        var response = await _httpClient.GetAsync($"/api/keyexchange/keys?excludeProducerId={_producerId}");

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[{_producerId}] Failed to fetch keys");
            return false;
        }

        var keyResponse = await response.Content.ReadFromJsonAsync<KeyExchangeResponse>();

        if (keyResponse?.PartnerKeys == null || keyResponse.PartnerKeys.Count == 0)
        {
            Console.WriteLine($"[{_producerId}] No partner keys available yet");
            return false;
        }

        Console.WriteLine($"[{_producerId}] Found {keyResponse.PartnerKeys.Count} partner key(s)");
        Console.WriteLine();

        foreach (var partnerKey in keyResponse.PartnerKeys)
        {
            var otherId = partnerKey.ProducerId;
            var comparison = string.Compare(_producerId, otherId, StringComparison.Ordinal);

            if (comparison > 0)
            {
                // We are alphabetically LARGER → we encapsulate for them
                Console.WriteLine($"[{_producerId}] Encapsulating for {otherId} (we are larger)...");

                var partnerPublicKeyBytes = Convert.FromBase64String(partnerKey.PublicKeyBase64);
                var (ciphertext, sharedSecret) = _kem.Encapsulate(partnerPublicKeyBytes);

                _sharedSecrets[otherId] = sharedSecret;

                // POST ciphertext to aggregator so the smaller partner can decapsulate
                var ctRequest = new StoreCiphertextRequest
                {
                    SenderId = _producerId,
                    RecipientId = otherId,
                    CiphertextBase64 = Convert.ToBase64String(ciphertext)
                };

                var ctResponse = await _httpClient.PostAsJsonAsync("/api/ciphertext", ctRequest);

                if (!ctResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[{_producerId}] Failed to post ciphertext: {await ctResponse.Content.ReadAsStringAsync()}");
                    return false;
                }

                Console.WriteLine($"[{_producerId}] ✓ Ciphertext posted for {otherId}");
                Console.WriteLine($"           Shared secret (first 8 bytes): {BitConverter.ToString(sharedSecret.Take(8).ToArray())}...");
                Console.WriteLine($"           (Aggregator CANNOT recover this!)");
            }
            else
            {
                // We are alphabetically SMALLER → the other partner will encapsulate for us (Phase 3)
                Console.WriteLine($"[{_producerId}] Waiting for ciphertext from {otherId} (they are larger)");
            }
        }

        _encapsulationDone = true;
        Console.WriteLine();
        return true;
    }

    // ── Phase 3 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// GET ciphertexts addressed to us (posted by alphabetically larger partners).
    /// Decapsulate each one to recover the shared secret.
    /// </summary>
    public async Task<bool> DecapsulateIncomingAsync()
    {
        Console.WriteLine($"[{_producerId}] Fetching incoming ciphertexts...");

        var response = await _httpClient.GetAsync($"/api/ciphertext?recipientId={_producerId}");

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[{_producerId}] Failed to fetch ciphertexts");
            return false;
        }

        var ctResponse = await response.Content.ReadFromJsonAsync<CiphertextResponse>();

        if (ctResponse == null)
        {
            Console.WriteLine($"[{_producerId}] No ciphertext response");
            return false;
        }

        if (ctResponse.Ciphertexts.Count == 0)
        {
            Console.WriteLine($"[{_producerId}] No incoming ciphertexts (we are the largest partner)");
        }

        foreach (var ct in ctResponse.Ciphertexts)
        {
            var ciphertextBytes = Convert.FromBase64String(ct.CiphertextBase64);
            var sharedSecret = _kem.Decapsulate(ciphertextBytes);

            _sharedSecrets[ct.SenderId] = sharedSecret;

            Console.WriteLine($"[{_producerId}] ✓ Decapsulated ciphertext from {ct.SenderId}");
            Console.WriteLine($"           Shared secret (first 8 bytes): {BitConverter.ToString(sharedSecret.Take(8).ToArray())}...");
            Console.WriteLine($"           (Aggregator CANNOT recover this!)");
        }

        _decapsulationDone = true;
        Console.WriteLine();
        return true;
    }

    // ── Phase 4 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Mask the actual metric value with HMAC-based noise derived from shared secrets, then submit.
    /// Noise sign: alphabetically smaller partner adds, larger subtracts → sums cancel to zero.
    /// </summary>
    public async Task<SubmissionResult> SubmitMetricSecure(
        string country,
        DateTime month,
        long actualValue)
    {
        if (!_keysRegistered || !_encapsulationDone || !_decapsulationDone)
        {
            return new SubmissionResult
            {
                Success = false,
                ProducerId = _producerId,
                Message = "Key exchange incomplete. Run all three phases first."
            };
        }

        Console.WriteLine();
        Console.WriteLine($"╔═══════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  {_displayName} ({_producerId}) - SECURE METRIC SUBMISSION (ML-KEM-768)");
        Console.WriteLine($"╠═══════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Country: {country,-20}                                            ║");
        Console.WriteLine($"║  Month: {month:yyyy-MM,-22}                                            ║");
        Console.WriteLine($"║  ─────────────────────────────────────────────────────────────────────────║");
        Console.WriteLine($"║  Actual MAU:          {actualValue,15:N0}                                   ║");
        Console.WriteLine($"╚═══════════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        var monthStart = new DateTime(month.Year, month.Month, 1);

        var epochResponse = await _httpClient.GetAsync($"/api/registry/epoch?date={monthStart:yyyy-MM-dd}");
        var epoch = await epochResponse.Content.ReadFromJsonAsync<ProducerEpoch>();

        if (epoch == null)
        {
            return new SubmissionResult
            {
                Success = false,
                ProducerId = _producerId,
                Message = "Failed to get epoch information"
            };
        }

        Console.WriteLine($"Step 1: Generating HMAC noise from ML-KEM shared secrets");
        Console.WriteLine($"        (Aggregator CANNOT reproduce this!)");
        Console.WriteLine();

        long maskedMAU = actualValue;
        var noiseBreakdown = new Dictionary<string, long>();

        Console.WriteLine($"  MAU Noise Calculation:");
        foreach (var (otherProducerId, sharedSecret) in _sharedSecrets)
        {
            var noise = SecureNoiseGenerator.GenerateNoise(sharedSecret, country, monthStart);
            var sign = SecureNoiseGenerator.GetNoiseSign(_producerId, otherProducerId);
            var appliedNoise = noise * sign;

            maskedMAU += appliedNoise;
            noiseBreakdown[otherProducerId] = appliedNoise;

            Console.WriteLine($"    With {otherProducerId}: {appliedNoise:+#,0;-#,0}");
        }

        Console.WriteLine($"    ─────────────────────────────────────────");
        Console.WriteLine($"    Total noise: {(maskedMAU - actualValue):+#,0;-#,0}");
        Console.WriteLine($"    Masked MAU:  {maskedMAU:N0}");
        Console.WriteLine();

        Console.WriteLine($"Step 2: Submitting masked value to aggregator...");
        Console.WriteLine($"        MAU: {maskedMAU:N0} (actual: {actualValue:N0})");

        var submission = new MetricSubmission
        {
            ProducerId = _producerId,
            Country = country,
            Month = monthStart,
            Value = maskedMAU,
            EpochId = epoch.EpochId,
            Signature = "mlkem-demo",
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
        Console.WriteLine($"═══════════════════════════════════════════════════════════════════════════");

        return new SubmissionResult
        {
            Success = true,
            ProducerId = _producerId,
            Message = "Submitted securely",
            OriginalValue = actualValue,
            MaskedValue = maskedMAU,
            NoiseApplied = maskedMAU - actualValue,
            NoiseBreakdown = noiseBreakdown
        };
    }

    public void Dispose()
    {
        _kem?.Dispose();
        _httpClient?.Dispose();
    }
}

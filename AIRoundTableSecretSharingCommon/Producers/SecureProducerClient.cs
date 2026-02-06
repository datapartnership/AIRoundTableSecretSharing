using System.Net.Http.Json;
using AIRoundTableSecretSharingCommon.Core;
using AIRoundTableSecretSharingCommon.Models;

namespace AIRoundTableSecretSharingCommon.Producers;

/// <summary>
/// A secure producer client that uses Diffie-Hellman key exchange.
/// The aggregator CANNOT determine individual values because it doesn't know the shared secrets.
/// </summary>
public class SecureProducerClient : IDisposable
{
    private readonly string _producerId;
    private readonly string _displayName;
    private readonly HttpClient _httpClient;
    private readonly DiffieHellmanKeyExchange _keyExchange;
    private readonly Dictionary<string, byte[]> _sharedSecrets = new();
    private bool _keysExchanged = false;
    
    public SecureProducerClient(string producerId, string displayName, string apiBaseUrl, string apiKey)
    {
        _producerId = producerId;
        _displayName = displayName;
        _httpClient = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };
        _httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        _keyExchange = new DiffieHellmanKeyExchange();
    }
    
    /// <summary>
    /// Step 1: Register our public key with the aggregator.
    /// This must be called once before any submissions.
    /// </summary>
    public async Task<bool> RegisterPublicKeyAsync()
    {
        Console.WriteLine($"[{_producerId}] Registering public key with aggregator...");
        
        var request = new RegisterKeyRequest
        {
            ProducerId = _producerId,
            PublicKeyBase64 = _keyExchange.GetPublicKeyBase64()
        };
        
        var response = await _httpClient.PostAsJsonAsync("/api/keyexchange/register", request);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[{_producerId}] Failed to register public key: {error}");
            return false;
        }
        
        Console.WriteLine($"[{_producerId}] ✓ Public key registered");
        return true;
    }
    
    /// <summary>
    /// Step 2: Fetch other partners' public keys and compute shared secrets.
    /// This establishes secure pairwise channels without the aggregator knowing the secrets.
    /// </summary>
    public async Task<bool> PerformKeyExchangeAsync()
    {
        Console.WriteLine($"[{_producerId}] Fetching other partners' public keys...");
        
        var response = await _httpClient.GetAsync($"/api/keyexchange/keys?excludeProducerId={_producerId}");
        
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[{_producerId}] Failed to fetch public keys");
            return false;
        }
        
        var keyResponse = await response.Content.ReadFromJsonAsync<KeyExchangeResponse>();
        
        if (keyResponse?.PartnerKeys == null || keyResponse.PartnerKeys.Count == 0)
        {
            Console.WriteLine($"[{_producerId}] No other partners' keys available yet");
            return false;
        }
        
        Console.WriteLine($"[{_producerId}] Computing shared secrets with {keyResponse.PartnerKeys.Count} partners...");
        Console.WriteLine();
        
        foreach (var partnerKey in keyResponse.PartnerKeys)
        {
            // Compute shared secret using Diffie-Hellman
            var sharedSecret = _keyExchange.ComputeSharedSecret(partnerKey.PublicKeyBase64);
            _sharedSecrets[partnerKey.ProducerId] = sharedSecret;
            
            Console.WriteLine($"[{_producerId}] ✓ Shared secret established with {partnerKey.ProducerId}");
            Console.WriteLine($"           Secret (first 8 bytes): {BitConverter.ToString(sharedSecret.Take(8).ToArray())}...");
            Console.WriteLine($"           (Aggregator CANNOT compute this!)");
        }
        
        _keysExchanged = true;
        Console.WriteLine();
        return true;
    }
    
    /// <summary>
    /// Step 3: Submit a metric with secure noise masking.
    /// The noise is computed from shared secrets that the aggregator doesn't know.
    /// </summary>
    public async Task<SubmissionResult> SubmitMetricSecure(
        string country,
        DateTime month,
        long actualValue)
    {
        if (!_keysExchanged)
        {
            return new SubmissionResult
            {
                Success = false,
                ProducerId = _producerId,
                Message = "Key exchange not completed. Call PerformKeyExchangeAsync first."
            };
        }
        
        Console.WriteLine();
        Console.WriteLine($"╔═══════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  {_displayName} ({_producerId}) - SECURE METRIC SUBMISSION                   ");
        Console.WriteLine($"╠═══════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Country: {country,-20}                                            ║");
        Console.WriteLine($"║  Month: {month:yyyy-MM,-22}                                            ║");
        Console.WriteLine($"║  ─────────────────────────────────────────────────────────────────────────║");
        Console.WriteLine($"║  Actual MAU:          {actualValue,15:N0}                                   ║");
        Console.WriteLine($"╚═══════════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        
        // Get epoch info
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
        
        Console.WriteLine($"Step 1: Generating secure noise using shared secrets");
        Console.WriteLine($"        (Aggregator CANNOT reproduce this!)");
        Console.WriteLine();
        
        // === Mask MAU value ===
        long maskedMAU = actualValue;
        var noiseBreakdown = new Dictionary<string, long>();
        
        Console.WriteLine($"  📊 MAU Noise Calculation:");
        foreach (var (otherProducerId, sharedSecret) in _sharedSecrets)
        {
            var noise = SecureNoiseGenerator.GenerateNoise(
                sharedSecret,
                country,
                monthStart,
                maxNoise: 100_000_000
            );
            
            var sign = SecureNoiseGenerator.GetNoiseSign(_producerId, otherProducerId);
            var appliedNoise = noise * sign;
            
            maskedMAU += appliedNoise;
            noiseBreakdown[otherProducerId] = appliedNoise;
            
            Console.WriteLine($"    With {otherProducerId}: {appliedNoise:+#,0;-#,0}");
        }
        Console.WriteLine($"    ─────────────────────────────────────────");
        Console.WriteLine($"    Total noise: {(maskedMAU - actualValue):+#,0;-#,0}");
        Console.WriteLine($"    Masked MAU: {maskedMAU:N0}");
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
            Signature = "secure-demo",
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
        _keyExchange?.Dispose();
        _httpClient?.Dispose();
    }
}

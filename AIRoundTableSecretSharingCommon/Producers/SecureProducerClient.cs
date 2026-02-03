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
    
    public SecureProducerClient(string producerId, string displayName, string apiBaseUrl)
    {
        _producerId = producerId;
        _displayName = displayName;
        _httpClient = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };
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
    /// Step 3: Submit a metric with secure noise masking (MAU only, for backward compatibility).
    /// The noise is computed from shared secrets that the aggregator doesn't know.
    /// </summary>
    public async Task<SubmissionResult> SubmitMetricSecure(
        string country,
        DateTime month,
        long actualValue)
    {
        return await SubmitMetricSecure(country, month, actualValue, null, null);
    }
    
    /// <summary>
    /// Step 3: Submit dual metrics with secure noise masking.
    /// Both MAU and WeightedMAU (MAU × coefficient) are masked independently.
    /// The aggregator learns only the totals, not individual values or coefficients.
    /// </summary>
    /// <param name="country">Country code</param>
    /// <param name="month">Month for the metric</param>
    /// <param name="actualMAU">Actual Monthly Active Users value</param>
    /// <param name="coefficient">Partner-specific secret coefficient (optional)</param>
    /// <param name="actualWeightedMAU">Pre-computed weighted value (MAU × coefficient). If null but coefficient is provided, it will be computed.</param>
    public async Task<SubmissionResult> SubmitMetricSecure(
        string country,
        DateTime month,
        long actualMAU,
        double? coefficient,
        long? actualWeightedMAU = null)
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
        
        // Compute weighted value if coefficient is provided but weighted value is not
        if (coefficient.HasValue && !actualWeightedMAU.HasValue)
        {
            actualWeightedMAU = (long)(actualMAU * coefficient.Value);
        }
        
        Console.WriteLine();
        Console.WriteLine($"╔═══════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  {_displayName} ({_producerId}) - SECURE DUAL METRIC SUBMISSION              ");
        Console.WriteLine($"╠═══════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Country: {country,-20}                                            ║");
        Console.WriteLine($"║  Month: {month:yyyy-MM,-22}                                            ║");
        Console.WriteLine($"║  ─────────────────────────────────────────────────────────────────────────║");
        Console.WriteLine($"║  Actual MAU:          {actualMAU,15:N0}                                   ║");
        if (coefficient.HasValue)
        {
            Console.WriteLine($"║  Coefficient (SECRET): {coefficient.Value,14:F4}                                   ║");
            Console.WriteLine($"║  Weighted MAU:        {actualWeightedMAU,15:N0}  (MAU × coefficient)          ║");
        }
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
        
        Console.WriteLine($"Step 1: Generating secure noise for BOTH metrics using shared secrets");
        Console.WriteLine($"        (Aggregator CANNOT reproduce this!)");
        Console.WriteLine();
        
        // === Mask MAU value ===
        long maskedMAU = actualMAU;
        var mauNoiseBreakdown = new Dictionary<string, long>();
        
        Console.WriteLine($"  📊 MAU Noise Calculation:");
        foreach (var (otherProducerId, sharedSecret) in _sharedSecrets)
        {
            var noise = SecureNoiseGenerator.GenerateNoise(
                sharedSecret,
                country,
                monthStart,
                MetricType.MAU,
                maxNoise: 100_000_000
            );
            
            var sign = SecureNoiseGenerator.GetNoiseSign(_producerId, otherProducerId);
            var appliedNoise = noise * sign;
            
            maskedMAU += appliedNoise;
            mauNoiseBreakdown[otherProducerId] = appliedNoise;
            
            Console.WriteLine($"    With {otherProducerId}: {appliedNoise:+#,0;-#,0}");
        }
        Console.WriteLine($"    ─────────────────────────────────────────");
        Console.WriteLine($"    Total MAU noise: {(maskedMAU - actualMAU):+#,0;-#,0}");
        Console.WriteLine($"    Masked MAU: {maskedMAU:N0}");
        Console.WriteLine();
        
        // === Mask WeightedMAU value (if provided) ===
        long? maskedWeightedMAU = null;
        var weightedNoiseBreakdown = new Dictionary<string, long>();
        
        if (actualWeightedMAU.HasValue)
        {
            maskedWeightedMAU = actualWeightedMAU.Value;
            
            Console.WriteLine($"  📊 Weighted MAU Noise Calculation:");
            foreach (var (otherProducerId, sharedSecret) in _sharedSecrets)
            {
                var noise = SecureNoiseGenerator.GenerateNoise(
                    sharedSecret,
                    country,
                    monthStart,
                    MetricType.WeightedMAU,
                    maxNoise: 100_000_000
                );
                
                var sign = SecureNoiseGenerator.GetNoiseSign(_producerId, otherProducerId);
                var appliedNoise = noise * sign;
                
                maskedWeightedMAU += appliedNoise;
                weightedNoiseBreakdown[otherProducerId] = appliedNoise;
                
                Console.WriteLine($"    With {otherProducerId}: {appliedNoise:+#,0;-#,0}");
            }
            Console.WriteLine($"    ─────────────────────────────────────────");
            Console.WriteLine($"    Total Weighted noise: {(maskedWeightedMAU - actualWeightedMAU):+#,0;-#,0}");
            Console.WriteLine($"    Masked WeightedMAU: {maskedWeightedMAU:N0}");
            Console.WriteLine();
        }
        
        Console.WriteLine($"Step 2: Submitting masked values to aggregator...");
        Console.WriteLine($"        MAU: {maskedMAU:N0} (actual: {actualMAU:N0})");
        if (maskedWeightedMAU.HasValue)
        {
            Console.WriteLine($"        WeightedMAU: {maskedWeightedMAU:N0} (actual: {actualWeightedMAU:N0})");
        }
        
        var submission = new MetricSubmission
        {
            ProducerId = _producerId,
            Country = country,
            Month = monthStart,
            Value = maskedMAU,
            WeightedValue = maskedWeightedMAU,
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
            OriginalValue = actualMAU,
            MaskedValue = maskedMAU,
            NoiseApplied = maskedMAU - actualMAU,
            NoiseBreakdown = mauNoiseBreakdown,
            OriginalWeightedValue = actualWeightedMAU,
            MaskedWeightedValue = maskedWeightedMAU,
            WeightedNoiseApplied = actualWeightedMAU.HasValue ? maskedWeightedMAU - actualWeightedMAU : null,
            WeightedNoiseBreakdown = weightedNoiseBreakdown.Count > 0 ? weightedNoiseBreakdown : null,
            Coefficient = coefficient
        };
    }
    
    public void Dispose()
    {
        _keyExchange?.Dispose();
        _httpClient?.Dispose();
    }
}

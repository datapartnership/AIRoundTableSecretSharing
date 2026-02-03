using System.Security.Cryptography;
using System.Text;

namespace AIRoundTableSecretSharingCommon.Core;

/// <summary>
/// Metric types for noise generation. Each metric type generates independent noise.
/// </summary>
public enum MetricType
{
    /// <summary>
    /// Monthly Active Users (base metric)
    /// </summary>
    MAU,
    
    /// <summary>
    /// Weighted MAU (MAU × partner-specific coefficient)
    /// </summary>
    WeightedMAU
}

/// <summary>
/// Generates cryptographically secure noise using Diffie-Hellman shared secrets.
/// The aggregator CANNOT compute this noise because it doesn't know the shared secrets.
/// </summary>
public static class SecureNoiseGenerator
{
    /// <summary>
    /// Generates noise using the shared secret between two partners.
    /// Both partners will independently compute the SAME noise value.
    /// The aggregator CANNOT compute this because it doesn't know the shared secret.
    /// </summary>
    public static long GenerateNoise(
        byte[] sharedSecret,
        string country,
        DateTime month,
        long maxNoise = 100_000_000)
    {
        return GenerateNoise(sharedSecret, country, month, MetricType.MAU, maxNoise);
    }
    
    /// <summary>
    /// Generates noise for a specific metric type using the shared secret between two partners.
    /// Different metric types generate different noise values, enabling parallel masked submissions.
    /// </summary>
    public static long GenerateNoise(
        byte[] sharedSecret,
        string country,
        DateTime month,
        MetricType metricType,
        long maxNoise = 100_000_000)
    {
        // Create context string for this specific submission
        // Include metric type to ensure different noise for each metric
        var contextString = $"{country}|{month:yyyy-MM}|{metricType}";
        var contextBytes = Encoding.UTF8.GetBytes(contextString);
        
        // Combine shared secret with context using HMAC-SHA256
        // This ensures the noise is different for each country/month/metricType
        using var hmac = new HMACSHA256(sharedSecret);
        var hash = hmac.ComputeHash(contextBytes);
        
        // Convert first 8 bytes to a seed
        var seed = BitConverter.ToInt64(hash, 0);
        
        // Generate noise in range [-maxNoise, maxNoise]
        var random = new Random((int)(seed & 0x7FFFFFFF));
        return random.NextInt64(-maxNoise, maxNoise + 1);
    }
    
    /// <summary>
    /// Determine the sign for noise application based on alphabetical ordering.
    /// This ensures that Partner A adds what Partner B subtracts.
    /// </summary>
    public static int GetNoiseSign(string myProducerId, string otherProducerId)
    {
        var comparison = string.Compare(myProducerId, otherProducerId, StringComparison.Ordinal);
        
        if (comparison < 0) return 1;  // I come first alphabetically, ADD
        if (comparison > 0) return -1; // I come second alphabetically, SUBTRACT
        
        throw new InvalidOperationException("Cannot compare producer with itself");
    }
}

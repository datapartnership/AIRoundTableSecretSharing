using System.Security.Cryptography;
using System.Text;

namespace AIRoundTableSecretSharingCommon.Core;

/// <summary>
/// Generates cryptographically secure noise using ML-KEM shared secrets.
/// The aggregator CANNOT compute this noise because it doesn't know the shared secrets.
/// Compatible with the Python submit.py noise formula.
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
        // Create context string for this specific submission
        var contextString = $"{country}|{month:yyyy-MM}";
        var contextBytes = Encoding.UTF8.GetBytes(contextString);
        
        // Combine shared secret with context using HMAC-SHA256
        // This ensures the noise is different for each country/month
        using var hmac = new HMACSHA256(sharedSecret);
        var hash = hmac.ComputeHash(contextBytes);
        
        // Signed little-endian int64 from first 8 bytes — matches Python
        // struct.unpack("<q", h[:8])[0]. Use Python-style modulo (always
        // non-negative) so both runtimes produce identical noise values.
        var seed = BitConverter.ToInt64(hash, 0);
        var noiseRange = 2 * maxNoise + 1;
        var mod = ((seed % noiseRange) + noiseRange) % noiseRange;
        return mod - maxNoise;
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

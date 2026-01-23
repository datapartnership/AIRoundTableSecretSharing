using System.Security.Cryptography;
using System.Text;

namespace AIRoundTableSecretSharingCommon.Core;

public static class DeterministicNoiseGenerator
{
    /// <summary>
    /// Generates deterministic noise between two producers for a given context.
    /// Both producers will generate the SAME noise value independently.
    /// </summary>
    public static long GenerateNoise(
        string producerId1, 
        string producerId2,
        string country,
        DateTime month,
        long maxNoise = 100_000_000)
    {
        if (string.IsNullOrEmpty(producerId1) || string.IsNullOrEmpty(producerId2))
            throw new ArgumentException("Producer IDs cannot be null or empty");
        
        if (producerId1 == producerId2)
            throw new ArgumentException("Cannot generate noise with self");
        
        // Create deterministic seed from inputs
        // Order doesn't matter - hash will be same either way for the pair
        var sortedIds = new[] { producerId1, producerId2 }.OrderBy(x => x).ToArray();
        var seedString = $"{sortedIds[0]}|{sortedIds[1]}|{country}|{month:yyyy-MM}";
        
        // Hash to create deterministic seed
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(seedString));
        
        // Convert first 8 bytes to long
        var seed = BitConverter.ToInt64(hashBytes, 0);
        
        // Create random generator with deterministic seed
        var random = new Random((int)(seed & 0x7FFFFFFF));
        
        // Generate noise in range [-maxNoise, maxNoise]
        var noise = random.NextInt64(-maxNoise, maxNoise + 1);
        
        return noise;
    }
    
    /// <summary>
    /// Determine the sign for noise application based on alphabetical ordering.
    /// This ensures that Producer A adds what Producer B subtracts.
    /// </summary>
    public static int GetNoiseSign(string myProducerId, string otherProducerId)
    {
        var comparison = string.Compare(myProducerId, otherProducerId, StringComparison.Ordinal);
        
        if (comparison < 0) return 1;  // I come first alphabetically, ADD
        if (comparison > 0) return -1; // I come second alphabetically, SUBTRACT
        
        throw new InvalidOperationException("Cannot compare producer with itself");
    }
}

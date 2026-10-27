using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AIRoundTableSecretSharingCommon.Core;

/// <summary>
/// Generates cryptographically secure noise using ML-KEM shared secrets.
/// The aggregator CANNOT compute this noise because it doesn't know the shared secrets.
/// Compatible with the web UI HMAC-SHA256 noise formula.
/// </summary>
public static class SecureNoiseGenerator
{
    /// <summary>
    /// Generates noise using the shared secret between two partners.
    /// Both partners will independently compute the SAME noise value.
    /// The aggregator CANNOT compute this because it doesn't know the shared secret.
    /// Noise spans the full int64 range; masking and aggregation use wrap-around
    /// (mod 2^64) arithmetic, so a masked value reveals nothing about the actual one.
    /// </summary>
    public static long GenerateNoise(
        byte[] sharedSecret,
        string country,
        string month,
        string indicator,
        string segment)
    {
        var contextString = $"{country}|{month}|{indicator}|{segment}";
        var contextBytes = Encoding.UTF8.GetBytes(contextString);

        using var hmac = new HMACSHA256(sharedSecret);
        var hash = hmac.ComputeHash(contextBytes);

        // Signed little-endian int64 from first 8 bytes — matches JS/Python
        // struct.unpack("<q", h[:8])[0].
        return BitConverter.ToInt64(hash, 0);
    }

    /// <summary>
    /// 128-bit variant of <see cref="GenerateNoise"/> for metric values: the first 16 bytes
    /// of the same HMAC as a little-endian signed Int128. Its low 64 bits equal the 64-bit noise.
    /// </summary>
    public static Int128 GenerateNoise128(
        byte[] sharedSecret,
        string country,
        string month,
        string indicator,
        string segment)
    {
        var contextBytes = Encoding.UTF8.GetBytes($"{country}|{month}|{indicator}|{segment}");

        using var hmac = new HMACSHA256(sharedSecret);
        var hash = hmac.ComputeHash(contextBytes);

        return BinaryPrimitives.ReadInt128LittleEndian(hash);
    }

    /// <summary>
    /// Applies signed noise to a value with wrap-around (mod 2^64) arithmetic.
    /// </summary>
    public static long ApplyNoise(long value, long noise, int sign) =>
        unchecked(value + noise * sign);

    /// <summary>
    /// Sums masked values with wrap-around (mod 2^64) arithmetic. Pairwise noise
    /// cancels exactly, so the result is the true total whenever it fits in int64.
    /// </summary>
    public static long SumMasked(IEnumerable<long> values)
    {
        long total = 0;
        foreach (var v in values) total = unchecked(total + v);
        return total;
    }

    /// <summary>
    /// Applies signed noise to a value with wrap-around (mod 2^128) arithmetic.
    /// </summary>
    public static Int128 ApplyNoise(Int128 value, Int128 noise, int sign) =>
        unchecked(value + noise * sign);

    /// <summary>
    /// Sums masked metric values with wrap-around arithmetic at the epoch's mask width.
    /// Values masked mod 2^64 are stored sign-extended, so their 128-bit sum truncated
    /// to 64 bits is the mod 2^64 sum.
    /// </summary>
    public static Int128 SumMasked(IEnumerable<Int128> values, int maskBits)
    {
        Int128 total = 0;
        foreach (var v in values) total = unchecked(total + v);
        return maskBits == 64 ? unchecked((long)total) : total;
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

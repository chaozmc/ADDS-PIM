using System.Globalization;
using System.Security.Cryptography;

namespace ADDS.PIM.Application.Mfa;

/// <summary>RFC 6238 TOTP calculation. Persistence, encryption and replay policy remain outside this primitive.</summary>
public static class Totp
{
    public const int DefaultPeriodSeconds = 30;
    public const int DefaultDigits = 6;

    public static string Generate(ReadOnlySpan<byte> secret, DateTimeOffset timestamp, int digits = DefaultDigits, int periodSeconds = DefaultPeriodSeconds)
    {
        if (secret.Length < 20) throw new ArgumentException("A TOTP secret must contain at least 160 bits.", nameof(secret));
        if (digits is < 6 or > 8 || periodSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(digits));
        var counter = checked((ulong)(timestamp.ToUnixTimeSeconds() / periodSeconds));
        Span<byte> counterBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(counterBytes, counter);
        var hash = HMACSHA1.HashData(secret, counterBytes);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        var modulus = (int)Math.Pow(10, digits);
        return (binary % modulus).ToString($"D{digits}", CultureInfo.InvariantCulture);
    }

    public static bool TryValidate(ReadOnlySpan<byte> secret, string? code, DateTimeOffset timestamp, out long matchedTimeStep)
    {
        matchedTimeStep = default;
        if (code is null || code.Length != DefaultDigits || !code.All(char.IsAsciiDigit)) return false;
        for (var offset = -1; offset <= 1; offset++)
        {
            var candidateTime = timestamp.AddSeconds(offset * DefaultPeriodSeconds);
            var candidate = Generate(secret, candidateTime);
            if (CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(candidate), System.Text.Encoding.ASCII.GetBytes(code)))
            {
                matchedTimeStep = candidateTime.ToUnixTimeSeconds() / DefaultPeriodSeconds;
                return true;
            }
        }
        return false;
    }
}

using System.Text;
using ADDS.PIM.Application.Mfa;

namespace ADDS.PIM.Application.Tests.Mfa;

public sealed class TotpTests
{
    [Theory]
    [InlineData(59L, "94287082")]
    [InlineData(1111111109L, "07081804")]
    [InlineData(1111111111L, "14050471")]
    public void Generate_MatchesRfc6238Sha1Vectors(long unixSeconds, string expected)
    {
        var secret = Encoding.ASCII.GetBytes("12345678901234567890");
        Assert.Equal(expected, Totp.Generate(secret, DateTimeOffset.FromUnixTimeSeconds(unixSeconds), digits: 8));
    }

    [Fact]
    public void TryValidate_AcceptsOneAdjacentStepAndReturnsItsStep()
    {
        var secret = Encoding.ASCII.GetBytes("12345678901234567890");
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var previous = Totp.Generate(secret, now.AddSeconds(-30));

        Assert.True(Totp.TryValidate(secret, previous, now, out var step));
        Assert.Equal(now.AddSeconds(-30).ToUnixTimeSeconds() / 30, step);
    }
}

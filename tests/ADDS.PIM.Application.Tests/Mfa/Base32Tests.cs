using System.Text;
using ADDS.PIM.Application.Mfa;

namespace ADDS.PIM.Application.Tests.Mfa;

public sealed class Base32Tests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Encode_MatchesRfc4648TestVectors(string input, string expected)
        => Assert.Equal(expected, Base32.Encode(Encoding.ASCII.GetBytes(input)));
}

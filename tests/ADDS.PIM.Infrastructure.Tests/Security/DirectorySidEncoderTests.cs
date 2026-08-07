using ADDS.PIM.Infrastructure.Security;

namespace ADDS.PIM.Infrastructure.Tests.Security;

public sealed class DirectorySidEncoderTests
{
    [Fact]
    public void ToBinarySid_EncodesWindowsSidForLdapObjectSidFilter()
    {
        var bytes = DirectorySidEncoder.ToBinarySid("S-1-5-21-1-2-3-1001");

        Assert.Equal(new byte[] { 1, 5, 0, 0, 0, 0, 0, 5, 21, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 233, 3, 0, 0 }, bytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("S-2-5-21")]
    [InlineData("S-1-281474976710656-21")]
    [InlineData("S-1-5-invalid")]
    public void ToBinarySid_RejectsInvalidSid(string sid)
    {
        Assert.Throws<ArgumentException>(() => DirectorySidEncoder.ToBinarySid(sid));
    }
}

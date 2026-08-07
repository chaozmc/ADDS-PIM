using ADDS.PIM.Application.Mfa;

namespace ADDS.PIM.Application.Tests.Mfa;

public sealed class Fido2RelyingPartyConfigurationTests
{
    [Fact]
    public void Validate_AcceptsTheConfiguredMvpOrigin()
        => new Fido2RelyingPartyConfiguration("pim.example.org", "https://pim.example.org").Validate();

    [Theory]
    [InlineData("http://pim.example.org")]
    [InlineData("https://other.example.org")]
    [InlineData("https://pim.example.org/registration")]
    public void Validate_RejectsAnOriginThatIsNotTheExactHttpsRelyingPartyOrigin(string origin)
    {
        var configuration = new Fido2RelyingPartyConfiguration("pim.example.org", origin);

        Assert.Throws<InvalidOperationException>(configuration.Validate);
    }
}

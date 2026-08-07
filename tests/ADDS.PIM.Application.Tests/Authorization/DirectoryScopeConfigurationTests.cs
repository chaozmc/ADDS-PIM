using ADDS.PIM.Application.Authorization;

namespace ADDS.PIM.Application.Tests.Authorization;

public sealed class DirectoryScopeConfigurationTests
{
    [Fact]
    public void Validate_AcceptsAnExplicitScopeAndDnsNames()
        => new DirectoryScopeConfiguration(Guid.NewGuid(), "example.org", "example.org").Validate();

    [Theory]
    [InlineData("", "example.org")]
    [InlineData("example.org", "not a dns name")]
    public void Validate_RejectsInvalidDnsNames(string domain, string forest)
        => Assert.Throws<InvalidOperationException>(() => new DirectoryScopeConfiguration(Guid.NewGuid(), domain, forest).Validate());
}

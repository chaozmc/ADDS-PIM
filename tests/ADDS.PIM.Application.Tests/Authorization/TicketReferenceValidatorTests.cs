using ADDS.PIM.Application.Authorization;

namespace ADDS.PIM.Application.Tests.Authorization;

public sealed class TicketReferenceValidatorTests
{
    [Theory]
    [InlineData(null, TicketReferenceValidationResult.Missing)]
    [InlineData("INC-12", TicketReferenceValidationResult.DoesNotMatch)]
    [InlineData(" CHG-12345 ", TicketReferenceValidationResult.Valid)]
    public async Task ValidateAsync_EnforcesRequiredPattern(string? reference, TicketReferenceValidationResult expected)
    {
        var validator = new TicketReferenceValidator(new FakeSource(new(true, [new(Guid.NewGuid(), "Change", "^CHG-[0-9]{5}$")] )));
        Assert.Equal(expected, await validator.ValidateAsync(Guid.NewGuid(), reference, CancellationToken.None));
    }

    [Fact]
    public async Task ValidateAsync_FailsClosed_WhenRequiredPolicyHasNoPattern()
    {
        var validator = new TicketReferenceValidator(new FakeSource(new(true, [])));
        Assert.Equal(TicketReferenceValidationResult.InvalidConfiguration, await validator.ValidateAsync(Guid.NewGuid(), "CHG-12345", CancellationToken.None));
    }

    [Fact]
    public async Task ValidateAsync_FailsClosed_WhenStoredPatternIsInvalid()
    {
        var validator = new TicketReferenceValidator(new FakeSource(new(true, [new(Guid.NewGuid(), "Broken", "[")])));
        Assert.Equal(TicketReferenceValidationResult.InvalidConfiguration, await validator.ValidateAsync(Guid.NewGuid(), "CHG-12345", CancellationToken.None));
    }

    [Fact]
    public async Task ValidateAsync_IgnoresTicket_WhenPolicyDoesNotRequireIt()
    {
        var validator = new TicketReferenceValidator(new FakeSource(new(false, [])));
        Assert.Equal(TicketReferenceValidationResult.Valid, await validator.ValidateAsync(Guid.NewGuid(), null, CancellationToken.None));
    }

    private sealed class FakeSource(TicketReferencePolicy policy) : ITicketReferencePolicySource
    {
        public Task<TicketReferencePolicy?> GetCurrentPolicyAsync(Guid targetGroupId, CancellationToken cancellationToken) => Task.FromResult<TicketReferencePolicy?>(policy);
    }
}

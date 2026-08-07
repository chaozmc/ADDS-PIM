using ADDS.PIM.Domain.MembershipRequests;

namespace ADDS.PIM.Domain.Tests.MembershipRequests;

public sealed class MembershipRequestStateMachineTests
{
    [Theory]
    [InlineData(MembershipRequestStatus.Created, MembershipRequestStatus.AwaitingSecondFactor)]
    [InlineData(MembershipRequestStatus.Created, MembershipRequestStatus.Validated)]
    [InlineData(MembershipRequestStatus.AwaitingSecondFactor, MembershipRequestStatus.SecondFactorValidated)]
    [InlineData(MembershipRequestStatus.SecondFactorValidated, MembershipRequestStatus.Validated)]
    [InlineData(MembershipRequestStatus.Validated, MembershipRequestStatus.Queued)]
    [InlineData(MembershipRequestStatus.Validated, MembershipRequestStatus.AwaitingApproval)]
    [InlineData(MembershipRequestStatus.AwaitingApproval, MembershipRequestStatus.Queued)]
    [InlineData(MembershipRequestStatus.Queued, MembershipRequestStatus.Executing)]
    [InlineData(MembershipRequestStatus.Executing, MembershipRequestStatus.VerificationPending)]
    [InlineData(MembershipRequestStatus.VerificationPending, MembershipRequestStatus.Succeeded)]
    public void CanTransition_AllowsDocumentedNormalPaths(
        MembershipRequestStatus currentStatus,
        MembershipRequestStatus requestedStatus)
    {
        Assert.True(MembershipRequestStateMachine.CanTransition(currentStatus, requestedStatus));
    }

    [Theory]
    [InlineData(MembershipRequestStatus.Created, MembershipRequestStatus.Succeeded)]
    [InlineData(MembershipRequestStatus.Validated, MembershipRequestStatus.Executing)]
    [InlineData(MembershipRequestStatus.AwaitingApproval, MembershipRequestStatus.Executing)]
    [InlineData(MembershipRequestStatus.Executing, MembershipRequestStatus.Succeeded)]
    [InlineData(MembershipRequestStatus.Succeeded, MembershipRequestStatus.Failed)]
    public void CanTransition_RejectsSkippedAndTerminalTransitions(
        MembershipRequestStatus currentStatus,
        MembershipRequestStatus requestedStatus)
    {
        Assert.False(MembershipRequestStateMachine.CanTransition(currentStatus, requestedStatus));
    }

    [Theory]
    [InlineData(MembershipRequestStatus.Created)]
    [InlineData(MembershipRequestStatus.Validated)]
    [InlineData(MembershipRequestStatus.AwaitingApproval)]
    [InlineData(MembershipRequestStatus.Executing)]
    public void CanTransition_AllowsExplicitTerminalOutcomesFromNonterminalStates(
        MembershipRequestStatus currentStatus)
    {
        Assert.True(MembershipRequestStateMachine.CanTransition(currentStatus, MembershipRequestStatus.Rejected));
        Assert.True(MembershipRequestStateMachine.CanTransition(currentStatus, MembershipRequestStatus.Failed));
        Assert.True(MembershipRequestStateMachine.CanTransition(currentStatus, MembershipRequestStatus.Expired));
        Assert.True(MembershipRequestStateMachine.CanTransition(currentStatus, MembershipRequestStatus.Cancelled));
    }

    [Fact]
    public void TransitionTo_RejectsSuccessBeforeVerification()
    {
        var request = CreateRequest();

        var action = () => request.TransitionTo(MembershipRequestStatus.Succeeded);

        Assert.Throws<MembershipRequestTransitionException>(action);
        Assert.Equal(MembershipRequestStatus.Created, request.Status);
    }

    [Fact]
    public void Constructor_RejectsEmptyRequestId()
    {
        var action = () => new MembershipRequest(
            Guid.Empty,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            900,
            "Test reason",
            null,
            DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void Constructor_RejectsMissingExplicitIdentityRole()
    {
        var action = () => new MembershipRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.Empty,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            900,
            "Test reason",
            null,
            DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(action);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsMissingReason(string reason)
    {
        var action = () => new MembershipRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            900,
            reason,
            null,
            DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void Constructor_TrimsReasonBeforePersistingIt()
    {
        var request = new MembershipRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            900,
            "  Emergency maintenance  ",
            null,
            DateTimeOffset.UtcNow);

        Assert.Equal("Emergency maintenance", request.Reason);
    }

    private static MembershipRequest CreateRequest() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        900,
        "Test reason",
        null,
        DateTimeOffset.UtcNow);
}

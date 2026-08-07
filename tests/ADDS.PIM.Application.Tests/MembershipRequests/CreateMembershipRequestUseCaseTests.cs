using ADDS.PIM.Application.MembershipRequests;
using ADDS.PIM.Domain.MembershipRequests;

namespace ADDS.PIM.Application.Tests.MembershipRequests;

public sealed class CreateMembershipRequestUseCaseTests
{
    private static readonly DateTimeOffset FixedUtc = new(2026, 8, 1, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_PersistsCreatedRequestStatusHistoryAndAuditEvent()
    {
        var store = new CapturingStore();
        var useCase = new CreateMembershipRequestUseCase(store, new FixedTimeProvider(FixedUtc));
        var command = CreateCommand();

        var request = await useCase.ExecuteAsync(command, CancellationToken.None);

        Assert.Equal(MembershipRequestStatus.Created, request.Status);
        Assert.Equal(FixedUtc, request.CreatedUtc);
        Assert.Same(request, store.Request);
        Assert.NotNull(store.StatusHistory);
        Assert.Null(store.StatusHistory.PreviousStatus);
        Assert.Equal(MembershipRequestStatus.Created, store.StatusHistory.NewStatus);
        Assert.Equal(command.ActorAccountId.ToString("D"), store.StatusHistory.ActorId);
        Assert.NotNull(store.AuditEvent);
        Assert.Equal(command.CorrelationId, store.AuditEvent.CorrelationId);
        Assert.Equal(command.RequestId, store.AuditEvent.RequestId);
        Assert.Equal(command.FrontendClientId, store.AuditEvent.FrontendClientId);
        Assert.Equal(command.RequestedTtlSeconds, store.AuditEvent.RequestedTtlSeconds);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsMissingTrustedCallerContext()
    {
        var store = new CapturingStore();
        var useCase = new CreateMembershipRequestUseCase(store, new FixedTimeProvider(FixedUtc));
        var command = CreateCommand() with { FrontendClientId = "" };

        await Assert.ThrowsAsync<ArgumentException>(() => useCase.ExecuteAsync(command, CancellationToken.None));
        Assert.Null(store.Request);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsMissingTargetAccount()
    {
        var store = new CapturingStore();
        var useCase = new CreateMembershipRequestUseCase(store, new FixedTimeProvider(FixedUtc));
        var command = CreateCommand() with { TargetAccountId = Guid.Empty };

        await Assert.ThrowsAsync<ArgumentException>(() => useCase.ExecuteAsync(command, CancellationToken.None));
        Assert.Null(store.Request);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsNonPositiveTtl()
    {
        var store = new CapturingStore();
        var useCase = new CreateMembershipRequestUseCase(store, new FixedTimeProvider(FixedUtc));
        var command = CreateCommand() with { RequestedTtlSeconds = 0 };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => useCase.ExecuteAsync(command, CancellationToken.None));
        Assert.Null(store.Request);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsMissingReason()
    {
        var store = new CapturingStore();
        var useCase = new CreateMembershipRequestUseCase(store, new FixedTimeProvider(FixedUtc));
        var command = CreateCommand() with { Reason = "  " };

        await Assert.ThrowsAsync<ArgumentException>(() => useCase.ExecuteAsync(command, CancellationToken.None));
        Assert.Null(store.Request);
    }

    private static CreateMembershipRequestCommand CreateCommand() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        "pim-web-prod-01",
        "Kerberos",
        "Standard",
        "192.0.2.10",
        Guid.NewGuid(),
        "Alex Example",
        "HOME\\alex",
        "HOME\\alex-admin",
        "HOME\\PIM-Test-Operators",
        900,
        "Emergency maintenance",
        "INC-12345");

    private sealed class CapturingStore : IMembershipRequestCreationStore
    {
        public MembershipRequest? Request { get; private set; }

        public MembershipRequestStatusHistoryEntry? StatusHistory { get; private set; }

        public MembershipRequestCreatedAuditEvent? AuditEvent { get; private set; }

        public Task CreateAsync(
            MembershipRequest request,
            MembershipRequestStatusHistoryEntry statusHistory,
            MembershipRequestCreatedAuditEvent auditEvent,
            CancellationToken cancellationToken)
        {
            Request = request;
            StatusHistory = statusHistory;
            AuditEvent = auditEvent;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

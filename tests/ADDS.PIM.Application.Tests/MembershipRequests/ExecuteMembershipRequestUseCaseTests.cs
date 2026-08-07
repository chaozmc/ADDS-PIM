using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Application.MembershipRequests;
using ADDS.PIM.Application.Worker;
using ADDS.PIM.Domain.MembershipRequests;
using ADDS.PIM.Domain.Security;

namespace ADDS.PIM.Application.Tests.MembershipRequests;

public sealed class ExecuteMembershipRequestUseCaseTests
{
    private static readonly Guid RequestId = Guid.NewGuid();
    private static readonly Guid PersonId = Guid.NewGuid();
    private static readonly Guid ActorAccountId = Guid.NewGuid();
    private static readonly Guid TargetAccountId = Guid.NewGuid();
    private static readonly Guid TargetGroupId = Guid.NewGuid();
    private static readonly Guid EntitlementId = Guid.NewGuid();
    private static readonly Guid DirectoryScopeId = Guid.NewGuid();
    private static readonly MembershipRequestTransitionAuditContext AuditContext = new(Guid.NewGuid(), "Web", "Windows", "policy-summary", "127.0.0.1");

    [Fact]
    public async Task ExecuteAsync_NoSecondFactorRequired_ReachesSucceededFromCreated()
    {
        var stateStore = new FakeStateStore();
        var useCase = NewUseCase(requiresSecondFactor: false, stateStore, out _);

        var result = await useCase.ExecuteAsync(Command(), CancellationToken.None);

        Assert.Equal(MembershipRequestStatus.Succeeded, result.Status);
        Assert.Contains(stateStore.Transitions, t => t is (MembershipRequestStatus.Created, MembershipRequestStatus.Validated));
    }

    [Fact]
    public async Task ExecuteAsync_SecondFactorRequired_RejectsDirectlyFromCreated()
    {
        var stateStore = new FakeStateStore();
        var useCase = NewUseCase(requiresSecondFactor: true, stateStore, out _);

        var result = await useCase.ExecuteAsync(Command(), CancellationToken.None);

        Assert.Equal(MembershipRequestStatus.Rejected, result.Status);
        Assert.Contains(stateStore.Transitions, t => t is (MembershipRequestStatus.Created, MembershipRequestStatus.Rejected));
    }

    [Fact]
    public async Task ExecuteFromSecondFactorValidatedAsync_ReachesSucceededEvenThoughPolicyStillRequiresAFactor()
    {
        var stateStore = new FakeStateStore();
        var useCase = NewUseCase(requiresSecondFactor: true, stateStore, out _);

        var result = await useCase.ExecuteFromSecondFactorValidatedAsync(Command(), CancellationToken.None);

        Assert.Equal(MembershipRequestStatus.Succeeded, result.Status);
        Assert.Contains(stateStore.Transitions, t => t is (MembershipRequestStatus.SecondFactorValidated, MembershipRequestStatus.Validated));
    }

    [Fact]
    public async Task ExecuteFromSecondFactorValidatedAsync_ReAuthorizationEnforced_RejectsWhenNoLongerAuthorized()
    {
        var stateStore = new FakeStateStore();
        var useCase = NewUseCase(requiresSecondFactor: true, stateStore, out var dataSource);
        dataSource.Contexts = [];

        var result = await useCase.ExecuteFromSecondFactorValidatedAsync(Command(), CancellationToken.None);

        Assert.Equal(MembershipRequestStatus.Rejected, result.Status);
        Assert.Contains(stateStore.Transitions, t => t is (MembershipRequestStatus.SecondFactorValidated, MembershipRequestStatus.Rejected));
    }

    [Fact]
    public async Task ExecuteAsync_ApprovalRequired_StopsAtAwaitingApprovalInsteadOfQueueing()
    {
        var stateStore = new FakeStateStore();
        var useCase = NewUseCase(requiresSecondFactor: false, stateStore, out _, requiresApproval: true);

        var result = await useCase.ExecuteAsync(Command(), CancellationToken.None);

        Assert.Equal(MembershipRequestStatus.AwaitingApproval, result.Status);
        Assert.Contains(stateStore.Transitions, t => t is (MembershipRequestStatus.Validated, MembershipRequestStatus.AwaitingApproval));
        Assert.DoesNotContain(stateStore.Transitions, t => t.To == MembershipRequestStatus.Queued);
    }

    [Fact]
    public async Task ExecuteFromApprovedAsync_ApprovalRequired_ResumesDirectlyFromAwaitingApprovalToQueued()
    {
        var stateStore = new FakeStateStore();
        var useCase = NewUseCase(requiresSecondFactor: false, stateStore, out _, requiresApproval: true);

        var result = await useCase.ExecuteFromApprovedAsync(Command(), CancellationToken.None);

        Assert.Equal(MembershipRequestStatus.Succeeded, result.Status);
        Assert.Contains(stateStore.Transitions, t => t is (MembershipRequestStatus.AwaitingApproval, MembershipRequestStatus.Queued));
        Assert.DoesNotContain(stateStore.Transitions, t => t.To == MembershipRequestStatus.AwaitingApproval);
    }

    [Fact]
    public async Task ExecuteFromApprovedAsync_ReAuthorizationEnforced_RejectsWhenNoLongerAuthorized()
    {
        var stateStore = new FakeStateStore();
        var useCase = NewUseCase(requiresSecondFactor: false, stateStore, out var dataSource, requiresApproval: true);
        dataSource.Contexts = [];

        var result = await useCase.ExecuteFromApprovedAsync(Command(), CancellationToken.None);

        Assert.Equal(MembershipRequestStatus.Rejected, result.Status);
        Assert.Contains(stateStore.Transitions, t => t is (MembershipRequestStatus.AwaitingApproval, MembershipRequestStatus.Rejected));
    }

    private static ExecuteMembershipRequestCommand Command()
        => new(RequestId, AuditContext.CorrelationId, new AuthenticatedDirectoryAccount(DirectoryScopeId, ActorAccountId), TargetAccountId, TargetGroupId, 3600, null, AuditContext);

    private static ExecuteMembershipRequestUseCase NewUseCase(bool requiresSecondFactor, FakeStateStore stateStore, out FakeAuthorizationDataSource dataSource, bool requiresApproval = false)
    {
        dataSource = new FakeAuthorizationDataSource(requiresSecondFactor, requiresApproval);
        var authorize = new AuthorizeMembershipRequestUseCase(dataSource, TimeProvider.System);
        var ticketValidator = new TicketReferenceValidator(new FakeTicketPolicySource());
        return new ExecuteMembershipRequestUseCase(authorize, ticketValidator, stateStore, new FakeWorkerClient());
    }

    private sealed class FakeAuthorizationDataSource(bool requiresSecondFactor, bool requiresApproval = false) : IMembershipAuthorizationDataSource
    {
        public IReadOnlyList<MembershipAuthorizationContext> Contexts { get; set; } =
        [
            new(
                PersonId: PersonId, ActorAccountId: ActorAccountId, TargetAccountId: TargetAccountId, TargetGroupId: TargetGroupId, EntitlementId: EntitlementId,
                PersonDisplayName: "Person", ActorAccountDisplayName: "Actor", TargetAccountDisplayName: "Target", TargetGroupDisplayName: "Group",
                TargetAccountIsActive: true, TargetAccountIsEnabledInDirectory: true, TargetAccountIsWithinAllowedScope: true,
                TargetLinkIsActive: true, TargetMayReceivePrivileges: true,
                TargetLinkValidFromUtc: DateTimeOffset.UtcNow.AddDays(-1), TargetLinkValidUntilUtc: null,
                TargetGroupIsEnabledForRequests: true, TargetGroupIsWithinAllowedScope: true, GroupPolicyIsActive: true,
                PolicyMinimumTtlSeconds: 3600, PolicyMaximumTtlSeconds: 3600, PolicyDefaultTtlSeconds: 3600, PolicyTtlStepSeconds: 3600,
                PolicyRequiresSecondFactor: requiresSecondFactor, PolicyAllowedSecondFactorTypes: requiresSecondFactor ? SecondFactorType.Totp : SecondFactorType.None,
                PolicyRequiresTicket: false, PolicyRequiresApproval: requiresApproval,
                EntitlementIsActive: true, EntitlementValidFromUtc: DateTimeOffset.UtcNow.AddDays(-1), EntitlementValidUntilUtc: null,
                EntitlementMinimumTtlSeconds: null, EntitlementMaximumTtlSeconds: null, EntitlementTtlStepSeconds: null,
                EntitlementRequiresSecondFactor: null, EntitlementRequiresTicket: null, EntitlementRequiresApproval: null,
                DirectoryScopeId: DirectoryScopeId, TargetAccountObjectGuid: Guid.NewGuid(), TargetGroupObjectGuid: Guid.NewGuid())
        ];

        public Task<IReadOnlyList<ResolvedActorIdentity>> ResolveActorAsync(AuthenticatedDirectoryAccount actor, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ResolvedActorIdentity>>(
            [
                new(ActorAccountId, PersonId, true, true, true, true, DateTimeOffset.UtcNow.AddDays(-1), null, true, true, DateTimeOffset.UtcNow.AddDays(-1), null)
            ]);

        public Task<IReadOnlyList<MembershipAuthorizationContext>> FindContextsAsync(Guid personId, Guid actorAccountId, Guid targetAccountId, Guid targetGroupId, CancellationToken cancellationToken)
            => Task.FromResult(Contexts);

        public Task<IReadOnlyList<MembershipAuthorizationContext>> FindContextsForPersonAsync(Guid personId, Guid actorAccountId, CancellationToken cancellationToken)
            => Task.FromResult(Contexts);
    }

    private sealed class FakeTicketPolicySource : ITicketReferencePolicySource
    {
        public Task<TicketReferencePolicy?> GetCurrentPolicyAsync(Guid targetGroupId, CancellationToken cancellationToken)
            => Task.FromResult<TicketReferencePolicy?>(null);
    }

    private sealed class FakeWorkerClient : IWorkerMembershipClient
    {
        public Task<WorkerMembershipDispatchResult> DispatchAsync(DispatchTemporaryGroupMembershipCommand command, CancellationToken cancellationToken)
            => Task.FromResult(new WorkerMembershipDispatchResult(
                WorkerMembershipDispatchKind.Completed,
                new TemporaryGroupMembershipResult(TemporaryGroupMembershipResultKind.Verified, "dc01", command.RequestedTtlSeconds, null),
                200, null));
    }

    private sealed class FakeStateStore : IMembershipRequestStateStore
    {
        public List<(MembershipRequestStatus From, MembershipRequestStatus To)> Transitions { get; } = [];
        public Task TransitionAsync(Guid requestId, MembershipRequestStatus expectedStatus, MembershipRequestStatus nextStatus, MembershipRequestTransitionAuditContext auditContext, string reason, CancellationToken cancellationToken)
        {
            Transitions.Add((expectedStatus, nextStatus));
            return Task.CompletedTask;
        }
    }
}

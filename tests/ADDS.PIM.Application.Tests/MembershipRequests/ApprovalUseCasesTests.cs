using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Application.MembershipRequests;
using ADDS.PIM.Application.Worker;
using ADDS.PIM.Contracts.MembershipRequests.V1;
using ADDS.PIM.Domain.MembershipRequests;
using ADDS.PIM.Domain.Security;

namespace ADDS.PIM.Application.Tests.MembershipRequests;

public sealed class ApprovalUseCasesTests
{
    private static readonly Guid RequestId = Guid.NewGuid();
    private static readonly Guid PersonId = Guid.NewGuid();
    private static readonly Guid ApproverPersonId = Guid.NewGuid();
    private static readonly Guid ApproverAccountId = Guid.NewGuid();
    private static readonly Guid ActorAccountId = Guid.NewGuid();
    private static readonly Guid TargetAccountId = Guid.NewGuid();
    private static readonly Guid TargetGroupId = Guid.NewGuid();
    private static readonly Guid DirectoryScopeId = Guid.NewGuid();
    private static readonly AuthenticatedDirectoryAccount ApproverActor = new(DirectoryScopeId, Guid.NewGuid());
    private static readonly MembershipRequestTransitionAuditContext AuditContext = new(Guid.NewGuid(), "Web", "Windows", "policy-summary", "127.0.0.1");

    [Fact]
    public async Task Approve_RejectsWhenActorIsNotAnApprover()
    {
        var authorizer = new FakeApprovalAuthorizer { Approver = null };
        var dataSource = new FakeApprovalDataSource();
        var useCase = new ApproveMembershipRequestUseCase(authorizer, dataSource, NewExecutor(new FakeStateStore()));

        var result = await useCase.ExecuteAsync(RequestId, ApproverActor, Guid.NewGuid(), AuditContext, CancellationToken.None);

        Assert.Equal(ApprovalDecisionOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task Approve_RejectsWhenRequestNotFound()
    {
        var authorizer = new FakeApprovalAuthorizer { Approver = new(ApproverPersonId, ApproverAccountId, "Approver"), IsApproverForGroup = true };
        var dataSource = new FakeApprovalDataSource { Request = null };
        var useCase = new ApproveMembershipRequestUseCase(authorizer, dataSource, NewExecutor(new FakeStateStore()));

        var result = await useCase.ExecuteAsync(RequestId, ApproverActor, Guid.NewGuid(), AuditContext, CancellationToken.None);

        Assert.Equal(ApprovalDecisionOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task Approve_RejectsWhenRequestIsNoLongerAwaitingApproval()
    {
        var authorizer = new FakeApprovalAuthorizer { Approver = new(ApproverPersonId, ApproverAccountId, "Approver"), IsApproverForGroup = true };
        var dataSource = new FakeApprovalDataSource { Request = PendingRequest(MembershipRequestStatus.Rejected) };
        var useCase = new ApproveMembershipRequestUseCase(authorizer, dataSource, NewExecutor(new FakeStateStore()));

        var result = await useCase.ExecuteAsync(RequestId, ApproverActor, Guid.NewGuid(), AuditContext, CancellationToken.None);

        Assert.Equal(ApprovalDecisionOutcome.NotEligible, result.Outcome);
    }

    [Fact]
    public async Task Approve_RechecksGroupAssignmentAndRejectsWhenRevoked()
    {
        var authorizer = new FakeApprovalAuthorizer { Approver = new(ApproverPersonId, ApproverAccountId, "Approver"), IsApproverForGroup = false };
        var dataSource = new FakeApprovalDataSource { Request = PendingRequest(MembershipRequestStatus.AwaitingApproval) };
        var useCase = new ApproveMembershipRequestUseCase(authorizer, dataSource, NewExecutor(new FakeStateStore()));

        var result = await useCase.ExecuteAsync(RequestId, ApproverActor, Guid.NewGuid(), AuditContext, CancellationToken.None);

        Assert.Equal(ApprovalDecisionOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task Approve_HappyPath_ResumesExecutionAndAttributesTheApprover()
    {
        var authorizer = new FakeApprovalAuthorizer { Approver = new(ApproverPersonId, ApproverAccountId, "Approver"), IsApproverForGroup = true };
        var dataSource = new FakeApprovalDataSource { Request = PendingRequest(MembershipRequestStatus.AwaitingApproval) };
        var stateStore = new FakeStateStore();
        var useCase = new ApproveMembershipRequestUseCase(authorizer, dataSource, NewExecutor(stateStore));

        var result = await useCase.ExecuteAsync(RequestId, ApproverActor, Guid.NewGuid(), AuditContext, CancellationToken.None);

        Assert.Equal(ApprovalDecisionOutcome.Accepted, result.Outcome);
        Assert.Equal(MembershipRequestStatus.Succeeded, result.Execution!.Status);
        Assert.Contains(stateStore.Transitions, t => t is (MembershipRequestStatus.AwaitingApproval, MembershipRequestStatus.Queued));
    }

    [Fact]
    public async Task Reject_RejectsEmptyReason()
    {
        var authorizer = new FakeApprovalAuthorizer { Approver = new(ApproverPersonId, ApproverAccountId, "Approver"), IsApproverForGroup = true };
        var dataSource = new FakeApprovalDataSource { Request = PendingRequest(MembershipRequestStatus.AwaitingApproval) };
        var stateStore = new FakeStateStore();
        var useCase = new RejectMembershipRequestUseCase(authorizer, dataSource, stateStore);

        var outcome = await useCase.ExecuteAsync(RequestId, ApproverActor, "  ", AuditContext, CancellationToken.None);

        Assert.Equal(ApprovalDecisionOutcome.Invalid, outcome);
        Assert.Empty(stateStore.Transitions);
    }

    [Fact]
    public async Task Reject_HappyPath_TransitionsToRejectedWithApproverRejectedCategory()
    {
        var authorizer = new FakeApprovalAuthorizer { Approver = new(ApproverPersonId, ApproverAccountId, "Approver"), IsApproverForGroup = true };
        var dataSource = new FakeApprovalDataSource { Request = PendingRequest(MembershipRequestStatus.AwaitingApproval) };
        var stateStore = new FakeStateStore();
        var useCase = new RejectMembershipRequestUseCase(authorizer, dataSource, stateStore);

        var outcome = await useCase.ExecuteAsync(RequestId, ApproverActor, "Not appropriate right now.", AuditContext, CancellationToken.None);

        Assert.Equal(ApprovalDecisionOutcome.Accepted, outcome);
        Assert.Contains(stateStore.Transitions, t => t is (MembershipRequestStatus.AwaitingApproval, MembershipRequestStatus.Rejected));
    }

    private static PendingApprovalRequestDetails PendingRequest(MembershipRequestStatus status)
        => new(RequestId, TargetAccountId, TargetGroupId, 3600, null, DirectoryScopeId, ActorAccountId, status);

    private static ExecuteMembershipRequestUseCase NewExecutor(FakeStateStore stateStore)
    {
        var dataSource = new FakeAuthorizationDataSource();
        var authorize = new AuthorizeMembershipRequestUseCase(dataSource, TimeProvider.System);
        var ticketValidator = new TicketReferenceValidator(new FakeTicketPolicySource());
        return new ExecuteMembershipRequestUseCase(authorize, ticketValidator, stateStore, new FakeWorkerClient());
    }

    private sealed class FakeApprovalAuthorizer : IGroupApprovalAuthorizer
    {
        public ApproverIdentity? Approver { get; set; }
        public bool IsApproverForGroup { get; set; }
        public Task<ApproverIdentity?> ResolveApproverAsync(AuthenticatedDirectoryAccount actor, CancellationToken cancellationToken) => Task.FromResult(Approver);
        public Task<bool> IsApproverForGroupAsync(Guid personId, Guid targetGroupId, CancellationToken cancellationToken) => Task.FromResult(IsApproverForGroup);
        public Task<IReadOnlyList<Guid>> ListApprovableGroupIdsAsync(Guid personId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>(IsApproverForGroup ? [TargetGroupId] : []);
    }

    private sealed class FakeApprovalDataSource : IApprovalDataSource
    {
        public PendingApprovalRequestDetails? Request { get; set; }
        public Task<PendingApprovalsPage> ListPendingApprovalsAsync(IReadOnlyList<Guid> approvableGroupIds, int pageNumber, int pageSize, CancellationToken cancellationToken) => Task.FromResult(new PendingApprovalsPage([], 0));
        public Task<PendingApprovalRequestDetails?> GetPendingRequestAsync(Guid requestId, CancellationToken cancellationToken) => Task.FromResult(Request);
    }

    private sealed class FakeAuthorizationDataSource : IMembershipAuthorizationDataSource
    {
        public Task<IReadOnlyList<ResolvedActorIdentity>> ResolveActorAsync(AuthenticatedDirectoryAccount actor, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ResolvedActorIdentity>>(
            [
                new(ActorAccountId, PersonId, true, true, true, true, DateTimeOffset.UtcNow.AddDays(-1), null, true, true, DateTimeOffset.UtcNow.AddDays(-1), null)
            ]);

        public Task<IReadOnlyList<MembershipAuthorizationContext>> FindContextsAsync(Guid personId, Guid actorAccountId, Guid targetAccountId, Guid targetGroupId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MembershipAuthorizationContext>>(
            [
                new(
                    PersonId: PersonId, ActorAccountId: ActorAccountId, TargetAccountId: TargetAccountId, TargetGroupId: TargetGroupId, EntitlementId: Guid.NewGuid(),
                    PersonDisplayName: "Person", ActorAccountDisplayName: "Actor", TargetAccountDisplayName: "Target", TargetGroupDisplayName: "Group",
                    TargetAccountIsActive: true, TargetAccountIsEnabledInDirectory: true, TargetAccountIsWithinAllowedScope: true,
                    TargetLinkIsActive: true, TargetMayReceivePrivileges: true,
                    TargetLinkValidFromUtc: DateTimeOffset.UtcNow.AddDays(-1), TargetLinkValidUntilUtc: null,
                    TargetGroupIsEnabledForRequests: true, TargetGroupIsWithinAllowedScope: true, GroupPolicyIsActive: true,
                    PolicyMinimumTtlSeconds: 3600, PolicyMaximumTtlSeconds: 3600, PolicyDefaultTtlSeconds: 3600, PolicyTtlStepSeconds: 3600,
                    PolicyRequiresSecondFactor: false, PolicyAllowedSecondFactorTypes: SecondFactorType.None,
                    PolicyRequiresTicket: false, PolicyRequiresApproval: true,
                    EntitlementIsActive: true, EntitlementValidFromUtc: DateTimeOffset.UtcNow.AddDays(-1), EntitlementValidUntilUtc: null,
                    EntitlementMinimumTtlSeconds: null, EntitlementMaximumTtlSeconds: null, EntitlementTtlStepSeconds: null,
                    EntitlementRequiresSecondFactor: null, EntitlementRequiresTicket: null, EntitlementRequiresApproval: null,
                    DirectoryScopeId: DirectoryScopeId, TargetAccountObjectGuid: Guid.NewGuid(), TargetGroupObjectGuid: Guid.NewGuid())
            ]);

        public Task<IReadOnlyList<MembershipAuthorizationContext>> FindContextsForPersonAsync(Guid personId, Guid actorAccountId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MembershipAuthorizationContext>>([]);
    }

    private sealed class FakeTicketPolicySource : ITicketReferencePolicySource
    {
        public Task<TicketReferencePolicy?> GetCurrentPolicyAsync(Guid targetGroupId, CancellationToken cancellationToken) => Task.FromResult<TicketReferencePolicy?>(null);
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

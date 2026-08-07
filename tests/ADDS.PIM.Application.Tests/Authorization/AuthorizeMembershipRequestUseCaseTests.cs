using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Domain.Security;

namespace ADDS.PIM.Application.Tests.Authorization;

public sealed class AuthorizeMembershipRequestUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_AuthorizesAnExactActiveDirectEntitlement()
    {
        var dataSource = new FakeDataSource([CreateActor()], [CreateContext()]);
        var useCase = new AuthorizeMembershipRequestUseCase(dataSource, new FixedTimeProvider(Now));

        var result = await useCase.ExecuteAsync(CreateCommand(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(dataSource.Contexts[0].EntitlementId, result.EntitlementId);
        Assert.Equal(dataSource.Contexts[0].TargetAccountId, result.TargetAccountId);
    }

    [Fact]
    public async Task ExecuteAsync_DeniesAmbiguousActorOwnership()
    {
        var actor = CreateActor();
        var dataSource = new FakeDataSource([actor, actor with { PersonId = Guid.NewGuid() }], [CreateContext()]);
        var useCase = new AuthorizeMembershipRequestUseCase(dataSource, new FixedTimeProvider(Now));

        var result = await useCase.ExecuteAsync(CreateCommand(), CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, dataSource.FindContextsCalls);
    }

    [Fact]
    public async Task ExecuteAsync_DeniesAConstraintThatWidensTheGroupPolicy()
    {
        var context = CreateContext() with { EntitlementMaximumTtlSeconds = 1_800 };
        var dataSource = new FakeDataSource([CreateActor()], [context]);
        var useCase = new AuthorizeMembershipRequestUseCase(dataSource, new FixedTimeProvider(Now));

        var result = await useCase.ExecuteAsync(CreateCommand(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ExecuteAsync_DeniesTtlOutsideTheEffectiveEntitlementRange()
    {
        var context = CreateContext() with { EntitlementMaximumTtlSeconds = 600 };
        var dataSource = new FakeDataSource([CreateActor()], [context]);
        var useCase = new AuthorizeMembershipRequestUseCase(dataSource, new FixedTimeProvider(Now));

        var result = await useCase.ExecuteAsync(CreateCommand(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ExecuteAsync_DeniesARequiredSecondFactorPolicyWithoutAnAllowedFactor()
    {
        var context = CreateContext() with
        {
            PolicyRequiresSecondFactor = true,
            PolicyAllowedSecondFactorTypes = SecondFactorType.None
        };
        var dataSource = new FakeDataSource([CreateActor()], [context]);
        var useCase = new AuthorizeMembershipRequestUseCase(dataSource, new FixedTimeProvider(Now));

        var result = await useCase.ExecuteAsync(CreateCommand(), CancellationToken.None);

        Assert.Null(result);
    }

    private static AuthorizeMembershipRequestCommand CreateCommand() => new(
        new AuthenticatedDirectoryAccount(Guid.NewGuid(), Guid.NewGuid()),
        TargetAccountId,
        TargetGroupId,
        900);

    private static readonly Guid PersonId = Guid.NewGuid();
    private static readonly Guid ActorAccountId = Guid.NewGuid();
    private static readonly Guid TargetAccountId = Guid.NewGuid();
    private static readonly Guid TargetGroupId = Guid.NewGuid();

    private static ResolvedActorIdentity CreateActor() => new(
        ActorAccountId, PersonId, true, true, true, true,
        Now.AddDays(-1), null, true, true, Now.AddDays(-1), null);

    private static MembershipAuthorizationContext CreateContext() => new(
        PersonId, ActorAccountId, TargetAccountId, TargetGroupId, Guid.NewGuid(),
        "Alex Example", "HOME\\alex", "HOME\\alex-admin", "HOME\\PIM-Test-Operators",
        true, true, true, true, true, Now.AddDays(-1), null,
        true, true, true, 300, 900, 600, 300, false, SecondFactorType.None, false, false,
        true, Now.AddDays(-1), null, null, null, null, null, null, null);

    private sealed class FakeDataSource(
        IReadOnlyList<ResolvedActorIdentity> actors,
        IReadOnlyList<MembershipAuthorizationContext> contexts) : IMembershipAuthorizationDataSource
    {
        public IReadOnlyList<MembershipAuthorizationContext> Contexts { get; } = contexts;
        public int FindContextsCalls { get; private set; }

        public Task<IReadOnlyList<ResolvedActorIdentity>> ResolveActorAsync(
            AuthenticatedDirectoryAccount actor,
            CancellationToken cancellationToken) => Task.FromResult(actors);

        public Task<IReadOnlyList<MembershipAuthorizationContext>> FindContextsAsync(
            Guid personId, Guid actorAccountId, Guid targetAccountId, Guid targetGroupId, CancellationToken cancellationToken)
        {
            FindContextsCalls++;
            return Task.FromResult(Contexts);
        }

        public Task<IReadOnlyList<MembershipAuthorizationContext>> FindContextsForPersonAsync(Guid personId, Guid actorAccountId, CancellationToken cancellationToken)
            => Task.FromResult(Contexts);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

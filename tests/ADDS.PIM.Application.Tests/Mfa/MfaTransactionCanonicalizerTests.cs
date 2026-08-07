using ADDS.PIM.Application.Mfa;

namespace ADDS.PIM.Application.Tests.Mfa;

public sealed class MfaTransactionCanonicalizerTests
{
    private static readonly Guid RequestId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PersonId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ActorAccountId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TargetAccountId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid TargetGroupId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private const long RequestedTtlSeconds = 3600;

    [Fact]
    public void ComputeHash_IsDeterministicForIdenticalTuples()
    {
        var first = MfaTransactionCanonicalizer.ComputeHash(RequestId, PersonId, ActorAccountId, TargetAccountId, TargetGroupId, RequestedTtlSeconds);
        var second = MfaTransactionCanonicalizer.ComputeHash(RequestId, PersonId, ActorAccountId, TargetAccountId, TargetGroupId, RequestedTtlSeconds);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ComputeHash_IsLowercaseHexSha256()
    {
        var hash = MfaTransactionCanonicalizer.ComputeHash(RequestId, PersonId, ActorAccountId, TargetAccountId, TargetGroupId, RequestedTtlSeconds);

        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void ComputeHash_ChangesWhenRequestIdChanges()
    {
        var baseline = MfaTransactionCanonicalizer.ComputeHash(RequestId, PersonId, ActorAccountId, TargetAccountId, TargetGroupId, RequestedTtlSeconds);
        var changed = MfaTransactionCanonicalizer.ComputeHash(Guid.NewGuid(), PersonId, ActorAccountId, TargetAccountId, TargetGroupId, RequestedTtlSeconds);

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void ComputeHash_ChangesWhenPersonIdChanges()
    {
        var baseline = MfaTransactionCanonicalizer.ComputeHash(RequestId, PersonId, ActorAccountId, TargetAccountId, TargetGroupId, RequestedTtlSeconds);
        var changed = MfaTransactionCanonicalizer.ComputeHash(RequestId, Guid.NewGuid(), ActorAccountId, TargetAccountId, TargetGroupId, RequestedTtlSeconds);

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void ComputeHash_ChangesWhenActorAccountIdChanges()
    {
        var baseline = MfaTransactionCanonicalizer.ComputeHash(RequestId, PersonId, ActorAccountId, TargetAccountId, TargetGroupId, RequestedTtlSeconds);
        var changed = MfaTransactionCanonicalizer.ComputeHash(RequestId, PersonId, Guid.NewGuid(), TargetAccountId, TargetGroupId, RequestedTtlSeconds);

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void ComputeHash_ChangesWhenTargetAccountIdChanges()
    {
        var baseline = MfaTransactionCanonicalizer.ComputeHash(RequestId, PersonId, ActorAccountId, TargetAccountId, TargetGroupId, RequestedTtlSeconds);
        var changed = MfaTransactionCanonicalizer.ComputeHash(RequestId, PersonId, ActorAccountId, Guid.NewGuid(), TargetGroupId, RequestedTtlSeconds);

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void ComputeHash_ChangesWhenTargetGroupIdChanges()
    {
        var baseline = MfaTransactionCanonicalizer.ComputeHash(RequestId, PersonId, ActorAccountId, TargetAccountId, TargetGroupId, RequestedTtlSeconds);
        var changed = MfaTransactionCanonicalizer.ComputeHash(RequestId, PersonId, ActorAccountId, TargetAccountId, Guid.NewGuid(), RequestedTtlSeconds);

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void ComputeHash_ChangesWhenRequestedTtlSecondsChanges()
    {
        var baseline = MfaTransactionCanonicalizer.ComputeHash(RequestId, PersonId, ActorAccountId, TargetAccountId, TargetGroupId, RequestedTtlSeconds);
        var changed = MfaTransactionCanonicalizer.ComputeHash(RequestId, PersonId, ActorAccountId, TargetAccountId, TargetGroupId, RequestedTtlSeconds + 1);

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void CreateCanonicalRepresentation_UsesVersionedNewlineJoinedKeyValuePairs()
    {
        var canonical = MfaTransactionCanonicalizer.CreateCanonicalRepresentation(RequestId, PersonId, ActorAccountId, TargetAccountId, TargetGroupId, RequestedTtlSeconds);

        var expected = string.Join('\n',
        [
            "version=mfa-transaction-v1",
            $"requestId={RequestId:D}",
            $"personId={PersonId:D}",
            $"actorAccountId={ActorAccountId:D}",
            $"targetAccountId={TargetAccountId:D}",
            $"targetGroupId={TargetGroupId:D}",
            $"requestedTtlSeconds={RequestedTtlSeconds}"
        ]);
        Assert.Equal(expected, canonical);
    }
}

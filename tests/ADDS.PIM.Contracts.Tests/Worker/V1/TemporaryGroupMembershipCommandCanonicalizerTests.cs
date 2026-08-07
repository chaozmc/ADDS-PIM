using ADDS.PIM.Contracts.Worker.V1;

namespace ADDS.PIM.Contracts.Tests.Worker.V1;

public sealed class TemporaryGroupMembershipCommandCanonicalizerTests
{
    [Fact]
    public void ComputeHash_IsDeterministicAndBindsTargetAccount()
    {
        var command = CreateCommand();
        var hash = TemporaryGroupMembershipCommandCanonicalizer.ComputeHash(command);

        Assert.True(TemporaryGroupMembershipCommandCanonicalizer.HasValidHash(command with { CommandHash = hash }));
        Assert.False(TemporaryGroupMembershipCommandCanonicalizer.HasValidHash(command with { CommandHash = hash, TargetAccountObjectGuid = Guid.NewGuid() }));
    }

    [Fact]
    public void CreateCanonicalRepresentation_RejectsInvalidNonce()
    {
        var command = CreateCommand() with { Nonce = "not a nonce" };

        Assert.Throws<ArgumentException>(() => TemporaryGroupMembershipCommandCanonicalizer.CreateCanonicalRepresentation(command));
    }

    [Fact]
    public void HasValidHash_RejectsMissingHash()
        => Assert.False(TemporaryGroupMembershipCommandCanonicalizer.HasValidHash(CreateCommand()));

    [Fact]
    public void CreateCanonicalRepresentation_UsesLfWithoutTrailingNewline()
    {
        var canonical = TemporaryGroupMembershipCommandCanonicalizer.CreateCanonicalRepresentation(CreateCommand());

        Assert.DoesNotContain('\r', canonical);
        Assert.False(canonical.EndsWith('\n'));
    }

    private static TemporaryGroupMembershipCommand CreateCommand() => new(
        TemporaryGroupMembershipCommand.CurrentVersion,
        Guid.Parse("2a6903bc-fbf3-4e90-875b-77a1a3d7e310"),
        Guid.Parse("0c8eaa6d-d09a-4102-9bd2-1ccaa61c7db9"),
        Guid.Parse("a2d265ed-9823-4689-b5df-28bde2c2b1c0"),
        new DateTimeOffset(2026, 8, 1, 16, 0, 0, TimeSpan.Zero),
        "WnESzI0BxxusF38H-kKwLA",
        Guid.Parse("1f263c5a-c68e-42f0-9a04-c9b92828f359"),
        Guid.Parse("ff59f1e2-7cfc-44e3-9d39-6aa9090e770b"),
        Guid.Parse("72855596-942b-41cb-8fa5-d4c346810bcf"),
        900,
        string.Empty);
}

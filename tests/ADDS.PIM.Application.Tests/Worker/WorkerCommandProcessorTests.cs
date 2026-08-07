using ADDS.PIM.Application.Worker;
using ADDS.PIM.Contracts.Worker.V1;

namespace ADDS.PIM.Application.Tests.Worker;

public sealed class WorkerCommandProcessorTests
{
    [Fact]
    public async Task ExecuteAsync_IdenticalTerminalRetryReturnsDurableResultWithoutAnotherAdCall()
    {
        var store = new FakeStore
        {
            Registration = new(WorkerCommandRegistrationKind.Existing, WorkerCommandStatus.Succeeded,
                new(TemporaryGroupMembershipResultKind.Verified, "gdc.example.org", 890, null))
        };
        var service = new FakeMembershipService();
        var processor = new WorkerCommandProcessor(store, service, TimeProvider.System);

        var result = await processor.ExecuteAsync(CreateCommand(), "AABB", CancellationToken.None);

        Assert.Equal(TemporaryGroupMembershipResultKind.Verified, result?.Kind);
        Assert.Equal(0, service.Calls);
        Assert.Equal(0, store.SetStatusCalls);
        Assert.Equal(0, store.CompleteCalls);
    }

    [Fact]
    public async Task ExecuteAsync_AcceptedCommandPersistsTerminalResultBeforeReturning()
    {
        var store = new FakeStore { Registration = new(WorkerCommandRegistrationKind.Accepted, null, null) };
        var service = new FakeMembershipService();
        var processor = new WorkerCommandProcessor(store, service, TimeProvider.System);

        var result = await processor.ExecuteAsync(CreateCommand(), "AABB", CancellationToken.None);

        Assert.Equal(TemporaryGroupMembershipResultKind.Verified, result?.Kind);
        Assert.Equal(1, service.Calls);
        Assert.Equal(1, store.SetStatusCalls);
        Assert.Equal(1, store.CompleteCalls);
        Assert.Equal(WorkerCommandStatus.Succeeded, store.CompletedStatus);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidCommandDoesNotReachPersistenceOrActiveDirectory()
    {
        var store = new FakeStore { Registration = new(WorkerCommandRegistrationKind.Accepted, null, null) };
        var service = new FakeMembershipService();
        var processor = new WorkerCommandProcessor(store, service, TimeProvider.System);
        var command = CreateCommand() with { CommandHash = "tampered" };

        var result = await processor.ExecuteAsync(command, "AABB", CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, store.RegisterCalls);
        Assert.Equal(0, service.Calls);
    }

    private static TemporaryGroupMembershipCommand CreateCommand()
    {
        var unsigned = new TemporaryGroupMembershipCommand(TemporaryGroupMembershipCommand.CurrentVersion, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, "a_valid_nonce", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 900, "");
        return unsigned with { CommandHash = TemporaryGroupMembershipCommandCanonicalizer.ComputeHash(unsigned) };
    }

    private sealed class FakeStore : IWorkerCommandStore
    {
        public required WorkerCommandRegistration Registration { get; init; }
        public int RegisterCalls { get; private set; }
        public int SetStatusCalls { get; private set; }
        public int CompleteCalls { get; private set; }
        public WorkerCommandStatus? CompletedStatus { get; private set; }

        public Task<WorkerCommandRegistration> RegisterAsync(TemporaryGroupMembershipCommand command, string callerCertificateThumbprint, DateTimeOffset receivedUtc, CancellationToken cancellationToken)
        {
            RegisterCalls++;
            return Task.FromResult(Registration);
        }

        public Task SetStatusAsync(Guid commandId, WorkerCommandStatus status, CancellationToken cancellationToken)
        {
            SetStatusCalls++;
            return Task.CompletedTask;
        }

        public Task CompleteAsync(Guid commandId, WorkerCommandStatus status, TemporaryGroupMembershipResult result, CancellationToken cancellationToken)
        {
            CompleteCalls++;
            CompletedStatus = status;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMembershipService : ITemporaryGroupMembershipService
    {
        public int Calls { get; private set; }

        public Task<TemporaryGroupMembershipResult> AddAndVerifyAsync(TemporaryGroupMembershipOperation operation, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new TemporaryGroupMembershipResult(TemporaryGroupMembershipResultKind.Verified, "gdc.example.org", 890, null));
        }
    }
}

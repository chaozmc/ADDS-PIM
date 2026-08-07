using ADDS.PIM.Contracts.Worker.V1;

namespace ADDS.PIM.Application.Worker;

public sealed class WorkerCommandProcessor(
    IWorkerCommandStore store,
    ITemporaryGroupMembershipService membershipService,
    TimeProvider timeProvider)
{
    public async Task<TemporaryGroupMembershipResult?> ExecuteAsync(TemporaryGroupMembershipCommand command, string callerCertificateThumbprint, CancellationToken cancellationToken)
    {
        try
        {
            if (!TemporaryGroupMembershipCommandCanonicalizer.HasValidHash(command)) return null;
        }
        catch (ArgumentException)
        {
            return null;
        }

        var registration = await store.RegisterAsync(command, callerCertificateThumbprint, timeProvider.GetUtcNow(), cancellationToken);
        if (registration.Kind == WorkerCommandRegistrationKind.ReplayConflict) return null;
        if (registration.Kind == WorkerCommandRegistrationKind.Existing)
        {
            return registration.ExistingStatus is WorkerCommandStatus.Succeeded or WorkerCommandStatus.Failed
                ? registration.ExistingResult
                : null;
        }

        await store.SetStatusAsync(command.CommandId, WorkerCommandStatus.Executing, cancellationToken);
        var result = await membershipService.AddAndVerifyAsync(new(command.TargetAccountObjectGuid, command.TargetGroupObjectGuid, command.RequestedTtlSeconds), cancellationToken);
        var status = result.Kind == TemporaryGroupMembershipResultKind.Verified ? WorkerCommandStatus.Succeeded : WorkerCommandStatus.Failed;
        await store.CompleteAsync(command.CommandId, status, result, cancellationToken);
        return result;
    }
}

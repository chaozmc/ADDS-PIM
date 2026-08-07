using ADDS.PIM.Application.Worker;
using ADDS.PIM.Contracts.Worker.V1;
using ADDS.PIM.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ADDS.PIM.Infrastructure.Persistence;

public sealed class EfWorkerCommandStore(PimDbContext dbContext) : IWorkerCommandStore
{
    public async Task CompleteAsync(
        Guid commandId,
        WorkerCommandStatus status,
        TemporaryGroupMembershipResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (status is not WorkerCommandStatus.Succeeded and not WorkerCommandStatus.Failed)
        {
            throw new ArgumentOutOfRangeException(nameof(status), "A worker command can only be completed with a terminal status.");
        }

        var entity = await dbContext.WorkerCommands.SingleOrDefaultAsync(x => x.CommandId == commandId, cancellationToken)
            ?? throw new InvalidOperationException("Worker command does not exist.");
        entity.Status = status;
        entity.ResultKind = result.Kind.ToString();
        entity.ResultDomainController = result.DomainController;
        entity.ResultRemainingTtlSeconds = result.RemainingTtlSeconds;
        entity.ResultErrorCode = result.ErrorCode;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SetStatusAsync(Guid commandId, WorkerCommandStatus status, CancellationToken cancellationToken)
    {
        var entity = await dbContext.WorkerCommands.SingleOrDefaultAsync(x => x.CommandId == commandId, cancellationToken)
            ?? throw new InvalidOperationException("Worker command does not exist.");
        entity.Status = status;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<WorkerCommandRegistration> RegisterAsync(
        TemporaryGroupMembershipCommand command,
        string callerCertificateThumbprint,
        DateTimeOffset receivedUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(callerCertificateThumbprint);

        var existing = await FindExistingAsync(command, cancellationToken);
        if (existing is not null)
        {
            return ToRegistration(existing, command, callerCertificateThumbprint);
        }

        dbContext.WorkerCommands.Add(new WorkerCommandEntity
        {
            CommandId = command.CommandId,
            RequestId = command.RequestId,
            CorrelationId = command.CorrelationId,
            Nonce = command.Nonce,
            CommandHash = command.CommandHash,
            CallerCertificateThumbprint = callerCertificateThumbprint,
            DirectoryScopeId = command.DirectoryScopeId,
            TargetAccountObjectGuid = command.TargetAccountObjectGuid,
            TargetGroupObjectGuid = command.TargetGroupObjectGuid,
            RequestedTtlSeconds = command.RequestedTtlSeconds,
            IssuedUtc = command.IssuedUtc,
            ReceivedUtc = receivedUtc,
            Status = WorkerCommandStatus.Received
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new WorkerCommandRegistration(WorkerCommandRegistrationKind.Accepted, null, null);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            existing = await FindExistingAsync(command, cancellationToken);
            return existing is null
                ? throw new InvalidOperationException("Worker command registration failed without a recoverable replay record.")
                : ToRegistration(existing, command, callerCertificateThumbprint);
        }
    }

    private Task<WorkerCommandEntity?> FindExistingAsync(TemporaryGroupMembershipCommand command, CancellationToken cancellationToken)
        => dbContext.WorkerCommands.SingleOrDefaultAsync(
            x => x.CommandId == command.CommandId || x.RequestId == command.RequestId || x.Nonce == command.Nonce,
            cancellationToken);

    private static WorkerCommandRegistration ToRegistration(
        WorkerCommandEntity existing,
        TemporaryGroupMembershipCommand command,
        string callerCertificateThumbprint)
        => existing.CommandId == command.CommandId
           && existing.RequestId == command.RequestId
           && StringComparer.Ordinal.Equals(existing.Nonce, command.Nonce)
           && StringComparer.Ordinal.Equals(existing.CommandHash, command.CommandHash)
           && StringComparer.OrdinalIgnoreCase.Equals(existing.CallerCertificateThumbprint, callerCertificateThumbprint)
            ? new WorkerCommandRegistration(
                WorkerCommandRegistrationKind.Existing,
                existing.Status,
                ToResult(existing))
            : new WorkerCommandRegistration(WorkerCommandRegistrationKind.ReplayConflict, existing.Status, null);

    private static TemporaryGroupMembershipResult? ToResult(WorkerCommandEntity existing)
        => existing.ResultKind is not null
           && Enum.TryParse<TemporaryGroupMembershipResultKind>(existing.ResultKind, ignoreCase: false, out var kind)
            ? new TemporaryGroupMembershipResult(kind, existing.ResultDomainController, existing.ResultRemainingTtlSeconds, existing.ResultErrorCode)
            : null;
}

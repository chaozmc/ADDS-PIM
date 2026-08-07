using ADDS.PIM.Application.Security;
using ADDS.PIM.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ADDS.PIM.Infrastructure.Persistence;

public sealed class EfApiRequestReplayStore(PimDbContext dbContext) : IApiRequestReplayStore
{
    public async Task<ApiRequestReplayRegistration> RegisterAsync(ApiRequestSignature signature, string canonicalRequestHash, DateTimeOffset receivedUtc, CancellationToken cancellationToken)
    {
        var existing = await FindExistingAsync(signature, cancellationToken);
        if (existing.Count > 0) return Classify(existing, signature, canonicalRequestHash);

        dbContext.ApiRequestReplays.Add(new ApiRequestReplayEntity
        {
            ReplayId = Guid.NewGuid(), KeyId = signature.KeyId, RequestId = signature.RequestId, Nonce = signature.Nonce,
            CanonicalRequestHash = canonicalRequestHash, IssuedUtc = signature.IssuedUtc, ReceivedUtc = receivedUtc
        });
        try { await dbContext.SaveChangesAsync(cancellationToken); return new(ApiRequestReplayRegistrationKind.Accepted); }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            existing = await FindExistingAsync(signature, cancellationToken);
            return existing.Count == 0
                ? throw new InvalidOperationException("API request replay registration failed without a recoverable replay record.")
                : Classify(existing, signature, canonicalRequestHash);
        }
    }

    private Task<List<ApiRequestReplayEntity>> FindExistingAsync(ApiRequestSignature signature, CancellationToken cancellationToken)
        => dbContext.ApiRequestReplays.Where(x => x.RequestId == signature.RequestId || x.Nonce == signature.Nonce).ToListAsync(cancellationToken);

    private static ApiRequestReplayRegistration Classify(IReadOnlyCollection<ApiRequestReplayEntity> records, ApiRequestSignature signature, string canonicalRequestHash)
        => records.Count == 1 && records.Single().RequestId == signature.RequestId
            && StringComparer.Ordinal.Equals(records.Single().Nonce, signature.Nonce)
            && StringComparer.Ordinal.Equals(records.Single().KeyId, signature.KeyId)
            && StringComparer.Ordinal.Equals(records.Single().CanonicalRequestHash, canonicalRequestHash)
                ? new(ApiRequestReplayRegistrationKind.Existing)
                : new(ApiRequestReplayRegistrationKind.Conflict);
}

using ADDS.PIM.Application.Mfa;
using Microsoft.EntityFrameworkCore;

namespace ADDS.PIM.Infrastructure.Persistence;

public sealed class EfMfaStatusStore(PimDbContext dbContext) : IMfaStatusStore
{
    public async Task<TotpStatus> FindTotpStatusAsync(Guid personId, CancellationToken cancellationToken)
    {
        var confirmedUtc = await dbContext.TotpFactors.AsNoTracking()
            .Where(x => x.PersonId == personId && x.IsActive && x.RevokedUtc == null)
            .Select(x => x.ConfirmedUtc)
            .SingleOrDefaultAsync(cancellationToken);
        return confirmedUtc is null ? new TotpStatus(false, null) : new TotpStatus(true, confirmedUtc);
    }

    public async Task<Fido2Status> FindFido2StatusAsync(Guid personId, CancellationToken cancellationToken)
    {
        var credentials = await dbContext.Fido2Credentials.AsNoTracking()
            .Where(x => x.PersonId == personId && x.RevokedUtc == null)
            .Select(x => new Fido2CredentialDisplay(x.Fido2CredentialId, x.Label, x.CreatedUtc))
            .ToArrayAsync(cancellationToken);
        return new Fido2Status(credentials.Length > 0, credentials);
    }
}

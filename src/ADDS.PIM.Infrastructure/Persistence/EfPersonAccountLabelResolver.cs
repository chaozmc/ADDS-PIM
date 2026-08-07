using ADDS.PIM.Application.Mfa;
using Microsoft.EntityFrameworkCore;

namespace ADDS.PIM.Infrastructure.Persistence;

public sealed class EfPersonAccountLabelResolver(PimDbContext dbContext) : IPersonAccountLabelResolver
{
    public Task<PersonAccountLabel?> ResolveAsync(Guid personId, CancellationToken cancellationToken)
        => (from link in dbContext.PersonAccountLinks.AsNoTracking()
            join account in dbContext.DirectoryAccounts.AsNoTracking() on link.AccountId equals account.AccountId
            join person in dbContext.Persons.AsNoTracking() on link.PersonId equals person.PersonId
            where link.PersonId == personId && link.IsActive && link.MayAuthenticate
            select new PersonAccountLabel(account.DomainQualifiedName, person.DisplayName))
            .SingleOrDefaultAsync(cancellationToken);
}

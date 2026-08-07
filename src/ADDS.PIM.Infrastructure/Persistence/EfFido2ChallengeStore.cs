using System.Text;
using ADDS.PIM.Application.Mfa;
using ADDS.PIM.Domain.Security;
using ADDS.PIM.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ADDS.PIM.Infrastructure.Persistence;

public sealed class EfFido2ChallengeStore(PimDbContext dbContext) : IFido2ChallengeStore
{
    public async Task<Guid> CreateAsync(Guid personId, Fido2ChallengePurpose purpose, string optionsJson, DateTimeOffset createdUtc, DateTimeOffset expiresUtc, CancellationToken cancellationToken)
    {
        var challengeId = Guid.NewGuid();
        dbContext.Fido2Challenges.Add(new Fido2ChallengeEntity
        {
            ChallengeId = challengeId,
            PersonId = personId,
            Purpose = purpose.ToString(),
            Challenge = Encoding.UTF8.GetBytes(optionsJson),
            CreatedUtc = createdUtc,
            ExpiresUtc = expiresUtc
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return challengeId;
    }

    public async Task<PendingFido2Challenge?> FindPendingAsync(Guid challengeId, Guid personId, Fido2ChallengePurpose purpose, CancellationToken cancellationToken)
    {
        var purposeText = purpose.ToString();
        var row = await dbContext.Fido2Challenges.AsNoTracking()
            .Where(x => x.ChallengeId == challengeId && x.PersonId == personId && x.Purpose == purposeText && x.ConsumedUtc == null)
            .Select(x => new { x.ChallengeId, x.PersonId, x.Challenge, x.ExpiresUtc })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : new PendingFido2Challenge(row.ChallengeId, row.PersonId, purpose, Encoding.UTF8.GetString(row.Challenge), row.ExpiresUtc);
    }

    public async Task<bool> TryConsumeAsync(Guid challengeId, SecondFactorType satisfiedBy, DateTimeOffset consumedUtc, CancellationToken cancellationToken)
        => await dbContext.Fido2Challenges
            .Where(x => x.ChallengeId == challengeId && x.ConsumedUtc == null && x.ExpiresUtc > consumedUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ConsumedUtc, consumedUtc)
                .SetProperty(x => x.SatisfiedBy, satisfiedBy), cancellationToken) == 1;
}

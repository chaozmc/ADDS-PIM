using ADDS.PIM.Infrastructure.Persistence;
using ADDS.PIM.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ADDS.PIM.Infrastructure.Tests.Persistence;

public sealed class MfaTransactionModelTests
{
    private static PimDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<PimDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ADDS_PIM_ModelTest;Integrated Security=True;TrustServerCertificate=True")
            .Options;
        return new PimDbContext(options);
    }

    [Fact]
    public void Model_EnforcesOnePendingTransactionPerRequest()
    {
        using var context = NewContext();
        var entityType = context.Model.FindEntityType(typeof(MfaTransactionEntity))!;
        var index = entityType.GetIndexes().Single(candidate => candidate.Properties.Select(property => property.Name).SequenceEqual([nameof(MfaTransactionEntity.RequestId)]));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void Model_TotpUsedTimeStepsHasCompositeKeyPerFactorAndTimeStep()
    {
        using var context = NewContext();
        var entityType = context.Model.FindEntityType(typeof(TotpUsedTimeStepEntity))!;
        var primaryKey = entityType.FindPrimaryKey()!;

        Assert.Equal([nameof(TotpUsedTimeStepEntity.TotpFactorId), nameof(TotpUsedTimeStepEntity.TimeStep)], primaryKey.Properties.Select(property => property.Name));
    }

    [Fact]
    public void Model_TotpFactorsEnforcesOneActiveFactorPerPerson()
    {
        using var context = NewContext();
        var entityType = context.Model.FindEntityType(typeof(TotpFactorEntity))!;
        var index = entityType.GetIndexes().Single(candidate => candidate.Properties.Select(property => property.Name).SequenceEqual([nameof(TotpFactorEntity.PersonId)]));

        Assert.True(index.IsUnique);
        Assert.Equal("[IsActive] = 1", index.GetFilter());
    }
}

using ADDS.PIM.Infrastructure.Persistence;
using ADDS.PIM.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ADDS.PIM.Infrastructure.Tests.Persistence;

public sealed class TechnicalErrorLogModelTests
{
    [Fact]
    public void Model_IndexesRequestIdAndCorrelationIdForLookup()
    {
        var options = new DbContextOptionsBuilder<PimDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ADDS_PIM_ModelTest;Integrated Security=True;TrustServerCertificate=True")
            .Options;
        using var context = new PimDbContext(options);
        var entityType = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(TechnicalErrorLogEntryEntity))!;

        Assert.Contains(entityType.GetIndexes(), index => index.Properties.Select(property => property.Name).SequenceEqual([nameof(TechnicalErrorLogEntryEntity.RequestId)]));
        Assert.Contains(entityType.GetIndexes(), index => index.Properties.Select(property => property.Name).SequenceEqual([nameof(TechnicalErrorLogEntryEntity.CorrelationId)]));
        Assert.False(entityType.FindProperty(nameof(TechnicalErrorLogEntryEntity.Message))!.IsNullable);
    }
}

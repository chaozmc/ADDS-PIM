using ADDS.PIM.Infrastructure.Persistence;
using ADDS.PIM.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ADDS.PIM.Infrastructure.Tests.Persistence;

public sealed class PurgeEventOutboxModelTests
{
    [Fact]
    public void Model_RetainsUndeliveredEntriesAndProtectsConcurrentDeliveryUpdates()
    {
        var options = new DbContextOptionsBuilder<PimDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ADDS_PIM_ModelTest;Integrated Security=True;TrustServerCertificate=True")
            .Options;
        using var context = new PimDbContext(options);
        var entityType = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PurgeEventOutboxEntity))!;

        Assert.True(entityType.FindProperty(nameof(PurgeEventOutboxEntity.RowVersion))!.IsConcurrencyToken);
        Assert.Contains(entityType.GetIndexes(), index => index.Properties.Select(property => property.Name).SequenceEqual([nameof(PurgeEventOutboxEntity.DeliveredUtc), nameof(PurgeEventOutboxEntity.CreatedUtc)]));
        Assert.Contains(entityType.GetCheckConstraints(), constraint => constraint.Name == "CK_PurgeEventOutbox_DeliveryAttempts");
    }
}

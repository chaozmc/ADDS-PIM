using ADDS.PIM.Infrastructure.Persistence;
using ADDS.PIM.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ADDS.PIM.Infrastructure.Tests.Persistence;

public sealed class PersonAccountLinkModelTests
{
    [Fact]
    public void Model_EnforcesOneActiveAuthenticationAccountPerPerson()
    {
        var options = new DbContextOptionsBuilder<PimDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ADDS_PIM_ModelTest;Integrated Security=True;TrustServerCertificate=True")
            .Options;
        using var context = new PimDbContext(options);
        var entityType = context.Model.FindEntityType(typeof(PersonAccountLinkEntity))!;
        var index = entityType.GetIndexes().Single(candidate => candidate.Properties.Select(property => property.Name).SequenceEqual([nameof(PersonAccountLinkEntity.PersonId)]));

        Assert.True(index.IsUnique);
        Assert.Equal("[MayAuthenticate] = 1 AND [IsActive] = 1", index.GetFilter());
    }

    [Fact]
    public void Model_EnforcesMayApproveRequiresMayAuthenticateOnTheSameLink()
    {
        var options = new DbContextOptionsBuilder<PimDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ADDS_PIM_ModelTest;Integrated Security=True;TrustServerCertificate=True")
            .Options;
        using var context = new PimDbContext(options);
        var entityType = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PersonAccountLinkEntity))!;
        var checkConstraint = entityType.GetCheckConstraints().Single(candidate => candidate.Name == "CK_PersonAccountLinks_MayApproveRequiresMayAuthenticate");

        Assert.Equal("[MayApprove] = 0 OR [MayAuthenticate] = 1", checkConstraint.Sql);
    }

    [Fact]
    public void Model_EnforcesAtMostOneActiveApproverAssignmentPerGroupAndPerson()
    {
        var options = new DbContextOptionsBuilder<PimDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ADDS_PIM_ModelTest;Integrated Security=True;TrustServerCertificate=True")
            .Options;
        using var context = new PimDbContext(options);
        var entityType = context.Model.FindEntityType(typeof(GroupApproverEntity))!;
        var index = entityType.GetIndexes().Single(candidate => candidate.Properties.Select(property => property.Name).SequenceEqual([nameof(GroupApproverEntity.TargetGroupId), nameof(GroupApproverEntity.PersonId)]));

        Assert.True(index.IsUnique);
        Assert.Equal("[IsActive] = 1", index.GetFilter());
    }

    [Fact]
    public void Model_GroupApproverNotifyByEmailDefaultsToTrue()
    {
        var options = new DbContextOptionsBuilder<PimDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ADDS_PIM_ModelTest;Integrated Security=True;TrustServerCertificate=True")
            .Options;
        using var context = new PimDbContext(options);
        var entityType = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(GroupApproverEntity))!;
        var property = entityType.FindProperty(nameof(GroupApproverEntity.NotifyByEmail))!;

        Assert.Equal(true, property.GetDefaultValue());
    }
}

using ADDS.PIM.Infrastructure.Persistence;
using ADDS.PIM.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ADDS.PIM.Infrastructure.Tests.Persistence;

public sealed class NotificationModelTests
{
    private static IModel Model()
    {
        var options = new DbContextOptionsBuilder<PimDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ADDS_PIM_ModelTest;Integrated Security=True;TrustServerCertificate=True")
            .Options;
        using var context = new PimDbContext(options);
        return context.GetService<IDesignTimeModel>().Model;
    }

    [Fact]
    public void GroupNotificationRecipients_HasUniqueActiveEmailPerGroupAndConcurrencyToken()
    {
        var entityType = Model().FindEntityType(typeof(GroupNotificationRecipientEntity))!;

        Assert.True(entityType.FindProperty(nameof(GroupNotificationRecipientEntity.RowVersion))!.IsConcurrencyToken);
        Assert.Contains(entityType.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual([nameof(GroupNotificationRecipientEntity.TargetGroupId), nameof(GroupNotificationRecipientEntity.EmailAddress)]));
        Assert.Contains(entityType.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(TargetGroupEntity));
        Assert.Contains(entityType.GetCheckConstraints(), constraint => constraint.Name == "CK_GroupNotificationRecipients_RecipientType");
    }

    [Fact]
    public void NotificationTemplates_HasUniqueTemplateKeyAndConcurrencyToken()
    {
        var entityType = Model().FindEntityType(typeof(NotificationTemplateEntity))!;

        Assert.True(entityType.FindProperty(nameof(NotificationTemplateEntity.RowVersion))!.IsConcurrencyToken);
        Assert.Contains(entityType.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual([nameof(NotificationTemplateEntity.TemplateKey)]));
    }

    [Fact]
    public void MailNotificationOutbox_RetainsUndeliveredEntriesAndProtectsConcurrentDeliveryUpdates()
    {
        var entityType = Model().FindEntityType(typeof(MailNotificationOutboxEntity))!;

        Assert.True(entityType.FindProperty(nameof(MailNotificationOutboxEntity.RowVersion))!.IsConcurrencyToken);
        Assert.Contains(entityType.GetIndexes(), index => index.Properties.Select(property => property.Name)
            .SequenceEqual([nameof(MailNotificationOutboxEntity.DeliveredUtc), nameof(MailNotificationOutboxEntity.CreatedUtc)]));
        Assert.Contains(entityType.GetCheckConstraints(), constraint => constraint.Name == "CK_MailNotificationOutbox_DeliveryAttempts");
        Assert.Contains(entityType.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(MembershipRequestEntity));
    }

    [Fact]
    public void RequesterNotificationSettings_IsConcurrencyProtectedSingletonRow()
    {
        var entityType = Model().FindEntityType(typeof(RequesterNotificationSettingsEntity))!;

        Assert.True(entityType.FindProperty(nameof(RequesterNotificationSettingsEntity.RowVersion))!.IsConcurrencyToken);
    }

    [Fact]
    public void Persons_NotificationEmailOverrideHasNonEmptyCheckConstraint()
    {
        var entityType = Model().FindEntityType(typeof(PersonEntity))!;

        Assert.Contains(entityType.GetCheckConstraints(), constraint => constraint.Name == "CK_Persons_NotificationEmailOverride");
    }
}

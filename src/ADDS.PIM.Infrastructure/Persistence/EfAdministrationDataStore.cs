using ADDS.PIM.Application.Administration;
using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Application.Security;
using ADDS.PIM.Application.Worker;
using ADDS.PIM.Contracts.Administration.V1;
using ADDS.PIM.Infrastructure.Mail;
using ADDS.PIM.Infrastructure.Mfa;
using ADDS.PIM.Infrastructure.Persistence.Entities;
using ADDS.PIM.Infrastructure.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Data;
using System.Security.Cryptography;

namespace ADDS.PIM.Infrastructure.Persistence;

public sealed class EfAdministrationDataStore(
    PimDbContext dbContext,
    TimeProvider timeProvider,
    IDirectoryAccountResolver directoryAccountResolver,
    ICertificateSecretProtector certificateSecretProtector,
    ICertificateSecretProtectorFactory totpSecretProtectorFactory,
    IOptions<TotpSecretProtectionOptions> totpSecretProtectionOptions,
    IOptions<WorkerClientOptions> workerClientOptions,
    IWorkerServerCertificateObservationStore workerServerCertificateObservationStore,
    IMailSender mailSender,
    IResolvedMailSettingsProvider resolvedMailSettingsProvider,
    ILogger<EfAdministrationDataStore> logger) : IAdministrationDataStore
{
    /// <summary>
    /// Resolves the display identity of the administrator performing the current
    /// action, for the ActorAccountId/ActorAccountDisplayNameSnapshot audit fields.
    /// Prefers an existing local DirectoryAccounts row (also yields a real
    /// ActorAccountId foreign key); falls back to a live AD lookup, since many
    /// administrators are never onboarded as a PIM Person/DirectoryAccount.
    /// </summary>
    private async Task<(Guid? AccountId, string? DisplayName)> ResolveAdministratorAsync(AdministrationActor actor, CancellationToken cancellationToken)
    {
        var local = await dbContext.DirectoryAccounts.AsNoTracking()
            .SingleOrDefaultAsync(account => account.DirectoryScopeId == actor.DirectoryScopeId && account.ObjectGuid == actor.ObjectGuid, cancellationToken);
        if (local is not null) return (local.AccountId, local.DomainQualifiedName);
        var resolved = await directoryAccountResolver.ResolveAccountAsync(actor.ObjectGuid, cancellationToken);
        return (null, resolved?.DomainQualifiedName ?? resolved?.DisplayName);
    }

    /// <summary>Display-name snapshots for a direct entitlement's Person/TargetAccount/TargetGroup, for audit events that only load the entitlement's raw entity.</summary>
    private async Task<(string PersonDisplayName, string TargetAccountDisplayName, string TargetGroupDisplayName)> ResolveEntitlementNamesAsync(Guid personId, Guid targetAccountId, Guid targetGroupId, CancellationToken cancellationToken)
    {
        var personDisplayName = await dbContext.Persons.AsNoTracking().Where(p => p.PersonId == personId).Select(p => p.DisplayName).SingleAsync(cancellationToken);
        var targetAccountDisplayName = await dbContext.DirectoryAccounts.AsNoTracking().Where(a => a.AccountId == targetAccountId).Select(a => a.DomainQualifiedName).SingleAsync(cancellationToken);
        var targetGroupDisplayName = await dbContext.TargetGroups.AsNoTracking().Where(g => g.TargetGroupId == targetGroupId).Select(g => g.DomainQualifiedName).SingleAsync(cancellationToken);
        return (personDisplayName, targetAccountDisplayName, targetGroupDisplayName);
    }

    public async Task<IReadOnlyList<ManagedTicketReferencePattern>> ListTicketReferencePatternsAsync(CancellationToken cancellationToken)
        => await (from pattern in dbContext.TicketReferencePatterns.AsNoTracking()
                  join policy in dbContext.GroupPolicies.AsNoTracking() on pattern.GroupPolicyId equals policy.GroupPolicyId
                  join targetGroup in dbContext.TargetGroups.AsNoTracking() on policy.GroupPolicyId equals targetGroup.GroupPolicyId
                  orderby targetGroup.DomainQualifiedName, pattern.Label
                  select new ManagedTicketReferencePattern(pattern.TicketReferencePatternId, targetGroup.TargetGroupId, targetGroup.DomainQualifiedName, pattern.Label, pattern.Expression, pattern.IsActive, Convert.ToBase64String(pattern.RowVersion))).ToListAsync(cancellationToken);

    public async Task<AdministrationUpdateResult> UpsertTicketReferencePatternAsync(UpsertTicketReferencePatternRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        try { _ = TicketReferencePatternFormat.Create(request.Expression); }
        catch (ArgumentException) { return AdministrationUpdateResult.Invalid; }
        var group = await dbContext.TargetGroups.SingleOrDefaultAsync(item => item.TargetGroupId == request.TargetGroupId, cancellationToken);
        if (group is null) return AdministrationUpdateResult.NotFound;
        var now = timeProvider.GetUtcNow();
        TicketReferencePatternEntity pattern;
        string before;
        if (request.TicketReferencePatternId == Guid.Empty)
        {
            if (await dbContext.TicketReferencePatterns.AnyAsync(item => item.GroupPolicyId == group.GroupPolicyId && item.Label == request.Label, cancellationToken)) return AdministrationUpdateResult.Conflict;
            pattern = new TicketReferencePatternEntity { TicketReferencePatternId = Guid.NewGuid(), GroupPolicyId = group.GroupPolicyId, Label = request.Label, Expression = request.Expression, IsActive = request.IsActive, CreatedUtc = now, ModifiedUtc = now };
            dbContext.TicketReferencePatterns.Add(pattern); before = "new";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.RowVersion)) return AdministrationUpdateResult.Invalid;
            byte[] rowVersion; try { rowVersion = Convert.FromBase64String(request.RowVersion); } catch (FormatException) { return AdministrationUpdateResult.Invalid; }
            var existing = await dbContext.TicketReferencePatterns.SingleOrDefaultAsync(item => item.TicketReferencePatternId == request.TicketReferencePatternId && item.GroupPolicyId == group.GroupPolicyId, cancellationToken);
            if (existing is null) return AdministrationUpdateResult.NotFound;
            pattern = existing;
            if (!pattern.RowVersion.SequenceEqual(rowVersion)) return AdministrationUpdateResult.Conflict;
            before = $"label={pattern.Label};expression={pattern.Expression};active={pattern.IsActive}";
            pattern.Label = request.Label; pattern.Expression = request.Expression; pattern.IsActive = request.IsActive; pattern.ModifiedUtc = now;
        }
        var (administratorAccountId, administratorDisplayName) = await ResolveAdministratorAsync(request.Actor, cancellationToken);
        dbContext.AuditEvents.Add(new AuditEventEntity { EventId = Guid.NewGuid(), EventType = "TicketReferencePatternUpdated", OccurredUtc = now, FrontendClientId = auditContext.FrontendClientId, SourceIpAddress = auditContext.SourceIpAddress, ClientSourceIpAddress = auditContext.ClientSourceIpAddress, SourceComponent = "Api", CorrelationId = auditContext.CorrelationId, ActorAccountId = administratorAccountId, ActorAccountDisplayNameSnapshot = administratorDisplayName, TargetGroupId = group.TargetGroupId, TargetGroupDisplayNameSnapshot = group.DomainQualifiedName, Result = "Succeeded", AuthenticationMethod = "Windows", PolicyRequirementsSummary = $"before:{before};after:label={request.Label};expression={request.Expression};active={request.IsActive}" });
        try { await dbContext.SaveChangesAsync(cancellationToken); return AdministrationUpdateResult.Updated; } catch (DbUpdateConcurrencyException ex) { logger.LogWarning(ex, "SaveChangesAsync hit a concurrent update; returning Conflict."); return AdministrationUpdateResult.Conflict; }
    }

    public async Task<AdministrationUpdateResult> DeleteTicketReferencePatternAsync(DeleteTicketReferencePatternRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        byte[] rowVersion; try { rowVersion = Convert.FromBase64String(request.RowVersion); } catch (FormatException) { return AdministrationUpdateResult.Invalid; }
        var pattern = await dbContext.TicketReferencePatterns.SingleOrDefaultAsync(item => item.TicketReferencePatternId == request.TicketReferencePatternId, cancellationToken);
        if (pattern is null) return AdministrationUpdateResult.NotFound;
        if (!pattern.RowVersion.SequenceEqual(rowVersion)) return AdministrationUpdateResult.Conflict;
        var group = await dbContext.TargetGroups.SingleAsync(item => item.GroupPolicyId == pattern.GroupPolicyId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var (administratorAccountId, administratorDisplayName) = await ResolveAdministratorAsync(request.Actor, cancellationToken);
        dbContext.AuditEvents.Add(new AuditEventEntity { EventId = Guid.NewGuid(), EventType = "TicketReferencePatternDeleted", OccurredUtc = now, FrontendClientId = auditContext.FrontendClientId, SourceIpAddress = auditContext.SourceIpAddress, ClientSourceIpAddress = auditContext.ClientSourceIpAddress, SourceComponent = "Api", CorrelationId = auditContext.CorrelationId, ActorAccountId = administratorAccountId, ActorAccountDisplayNameSnapshot = administratorDisplayName, TargetGroupId = group.TargetGroupId, TargetGroupDisplayNameSnapshot = group.DomainQualifiedName, Result = "Succeeded", AuthenticationMethod = "Windows", PolicyRequirementsSummary = $"patternId={pattern.TicketReferencePatternId:D};label={pattern.Label};expression={pattern.Expression};active={pattern.IsActive}" });
        dbContext.TicketReferencePatterns.Remove(pattern);
        try { await dbContext.SaveChangesAsync(cancellationToken); return AdministrationUpdateResult.Updated; } catch (DbUpdateConcurrencyException ex) { logger.LogWarning(ex, "SaveChangesAsync hit a concurrent update; returning Conflict."); return AdministrationUpdateResult.Conflict; }
    }

    public async Task<IReadOnlyList<ManagedGroupNotificationRecipient>> ListGroupNotificationRecipientsAsync(CancellationToken cancellationToken)
        => await (from recipient in dbContext.GroupNotificationRecipients.AsNoTracking()
                  join targetGroup in dbContext.TargetGroups.AsNoTracking() on recipient.TargetGroupId equals targetGroup.TargetGroupId
                  orderby targetGroup.DomainQualifiedName, recipient.EmailAddress
                  select new ManagedGroupNotificationRecipient(recipient.GroupNotificationRecipientId, targetGroup.TargetGroupId, targetGroup.DomainQualifiedName, recipient.EmailAddress, (MailRecipientType)recipient.RecipientType, recipient.IsActive, Convert.ToBase64String(recipient.RowVersion))).ToListAsync(cancellationToken);

    public async Task<AdministrationUpdateResult> UpsertGroupNotificationRecipientAsync(UpsertGroupNotificationRecipientRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        var group = await dbContext.TargetGroups.SingleOrDefaultAsync(item => item.TargetGroupId == request.TargetGroupId, cancellationToken);
        if (group is null) return AdministrationUpdateResult.NotFound;
        var now = timeProvider.GetUtcNow();
        GroupNotificationRecipientEntity recipient;
        string before;
        if (request.GroupNotificationRecipientId == Guid.Empty)
        {
            if (await dbContext.GroupNotificationRecipients.AnyAsync(item => item.TargetGroupId == request.TargetGroupId && item.EmailAddress == request.EmailAddress && item.IsActive, cancellationToken)) return AdministrationUpdateResult.Conflict;
            recipient = new GroupNotificationRecipientEntity { GroupNotificationRecipientId = Guid.NewGuid(), TargetGroupId = request.TargetGroupId, EmailAddress = request.EmailAddress, RecipientType = (int)request.RecipientType, IsActive = request.IsActive, CreatedUtc = now, ModifiedUtc = now };
            dbContext.GroupNotificationRecipients.Add(recipient); before = "new";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.RowVersion)) return AdministrationUpdateResult.Invalid;
            byte[] rowVersion; try { rowVersion = Convert.FromBase64String(request.RowVersion); } catch (FormatException) { return AdministrationUpdateResult.Invalid; }
            var existing = await dbContext.GroupNotificationRecipients.SingleOrDefaultAsync(item => item.GroupNotificationRecipientId == request.GroupNotificationRecipientId && item.TargetGroupId == request.TargetGroupId, cancellationToken);
            if (existing is null) return AdministrationUpdateResult.NotFound;
            recipient = existing;
            if (!recipient.RowVersion.SequenceEqual(rowVersion)) return AdministrationUpdateResult.Conflict;
            before = $"emailAddress={recipient.EmailAddress};recipientType={(MailRecipientType)recipient.RecipientType};active={recipient.IsActive}";
            recipient.EmailAddress = request.EmailAddress; recipient.RecipientType = (int)request.RecipientType; recipient.IsActive = request.IsActive; recipient.ModifiedUtc = now;
        }
        var (administratorAccountId, administratorDisplayName) = await ResolveAdministratorAsync(request.Actor, cancellationToken);
        dbContext.AuditEvents.Add(new AuditEventEntity { EventId = Guid.NewGuid(), EventType = "GroupNotificationRecipientUpdated", OccurredUtc = now, FrontendClientId = auditContext.FrontendClientId, SourceIpAddress = auditContext.SourceIpAddress, ClientSourceIpAddress = auditContext.ClientSourceIpAddress, SourceComponent = "Api", CorrelationId = auditContext.CorrelationId, ActorAccountId = administratorAccountId, ActorAccountDisplayNameSnapshot = administratorDisplayName, TargetGroupId = group.TargetGroupId, TargetGroupDisplayNameSnapshot = group.DomainQualifiedName, Result = "Succeeded", AuthenticationMethod = "Windows", PolicyRequirementsSummary = $"before:{before};after:emailAddress={request.EmailAddress};recipientType={request.RecipientType};active={request.IsActive}" });
        try { await dbContext.SaveChangesAsync(cancellationToken); return AdministrationUpdateResult.Updated; } catch (DbUpdateConcurrencyException ex) { logger.LogWarning(ex, "SaveChangesAsync hit a concurrent update; returning Conflict."); return AdministrationUpdateResult.Conflict; }
    }

    public async Task<AdministrationUpdateResult> DeleteGroupNotificationRecipientAsync(DeleteGroupNotificationRecipientRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        byte[] rowVersion; try { rowVersion = Convert.FromBase64String(request.RowVersion); } catch (FormatException) { return AdministrationUpdateResult.Invalid; }
        var recipient = await dbContext.GroupNotificationRecipients.SingleOrDefaultAsync(item => item.GroupNotificationRecipientId == request.GroupNotificationRecipientId, cancellationToken);
        if (recipient is null) return AdministrationUpdateResult.NotFound;
        if (!recipient.RowVersion.SequenceEqual(rowVersion)) return AdministrationUpdateResult.Conflict;
        var group = await dbContext.TargetGroups.SingleAsync(item => item.TargetGroupId == recipient.TargetGroupId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var (administratorAccountId, administratorDisplayName) = await ResolveAdministratorAsync(request.Actor, cancellationToken);
        dbContext.AuditEvents.Add(new AuditEventEntity { EventId = Guid.NewGuid(), EventType = "GroupNotificationRecipientDeleted", OccurredUtc = now, FrontendClientId = auditContext.FrontendClientId, SourceIpAddress = auditContext.SourceIpAddress, ClientSourceIpAddress = auditContext.ClientSourceIpAddress, SourceComponent = "Api", CorrelationId = auditContext.CorrelationId, ActorAccountId = administratorAccountId, ActorAccountDisplayNameSnapshot = administratorDisplayName, TargetGroupId = group.TargetGroupId, TargetGroupDisplayNameSnapshot = group.DomainQualifiedName, Result = "Succeeded", AuthenticationMethod = "Windows", PolicyRequirementsSummary = $"recipientId={recipient.GroupNotificationRecipientId:D};emailAddress={recipient.EmailAddress};active={recipient.IsActive}" });
        dbContext.GroupNotificationRecipients.Remove(recipient);
        try { await dbContext.SaveChangesAsync(cancellationToken); return AdministrationUpdateResult.Updated; } catch (DbUpdateConcurrencyException ex) { logger.LogWarning(ex, "SaveChangesAsync hit a concurrent update; returning Conflict."); return AdministrationUpdateResult.Conflict; }
    }

    public async Task<NotificationTemplateSnapshot> GetNotificationTemplateAsync(string templateKey, CancellationToken cancellationToken)
    {
        var template = await dbContext.NotificationTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.TemplateKey == templateKey, cancellationToken);
        return template is null
            ? new NotificationTemplateSnapshot(templateKey, string.Empty, string.Empty, null)
            : new NotificationTemplateSnapshot(templateKey, template.Subject, template.Body, Convert.ToBase64String(template.RowVersion));
    }

    public async Task<AdministrationUpdateResult> UpsertNotificationTemplateAsync(UpsertNotificationTemplateRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var existing = await dbContext.NotificationTemplates.SingleOrDefaultAsync(x => x.TemplateKey == request.TemplateKey, cancellationToken);
        string before;
        NotificationTemplateEntity template;
        if (existing is null)
        {
            if (!string.IsNullOrWhiteSpace(request.RowVersion)) return AdministrationUpdateResult.Conflict;
            template = new NotificationTemplateEntity { NotificationTemplateId = Guid.NewGuid(), TemplateKey = request.TemplateKey, Subject = request.Subject, Body = request.Body, UpdatedUtc = now, UpdatedBy = string.Empty };
            dbContext.NotificationTemplates.Add(template);
            before = "new";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.RowVersion)) return AdministrationUpdateResult.Invalid;
            byte[] rowVersion; try { rowVersion = Convert.FromBase64String(request.RowVersion); } catch (FormatException) { return AdministrationUpdateResult.Invalid; }
            if (!existing.RowVersion.SequenceEqual(rowVersion)) return AdministrationUpdateResult.Conflict;
            template = existing;
            before = $"subjectLength={template.Subject.Length};bodyLength={template.Body.Length}";
        }

        template.Subject = request.Subject;
        template.Body = request.Body;
        template.UpdatedUtc = now;

        var (administratorAccountId, administratorDisplayName) = await ResolveAdministratorAsync(request.Actor, cancellationToken);
        template.UpdatedBy = administratorDisplayName ?? administratorAccountId?.ToString() ?? "unknown";
        // Subject/Body are admin-entered free text up to 4000 chars each - embedding the full content (as the
        // group/ticket-pattern audit summaries do for their short, bounded fields) would overflow
        // PolicyRequirementsSummary's 512-char column. The actual content is already durably recoverable from
        // NotificationTemplateEntity itself, so the audit summary only needs bounded facts about the change.
        dbContext.AuditEvents.Add(new AuditEventEntity { EventId = Guid.NewGuid(), EventType = "NotificationTemplateUpdated", OccurredUtc = now, FrontendClientId = auditContext.FrontendClientId, SourceIpAddress = auditContext.SourceIpAddress, ClientSourceIpAddress = auditContext.ClientSourceIpAddress, SourceComponent = "Api", CorrelationId = auditContext.CorrelationId, ActorAccountId = administratorAccountId, ActorAccountDisplayNameSnapshot = administratorDisplayName, Result = "Succeeded", AuthenticationMethod = "Windows", PolicyRequirementsSummary = $"templateKey={request.TemplateKey};before:{before};after:subjectLength={request.Subject.Length};bodyLength={request.Body.Length}" });
        try { await dbContext.SaveChangesAsync(cancellationToken); return AdministrationUpdateResult.Updated; } catch (DbUpdateConcurrencyException ex) { logger.LogWarning(ex, "SaveChangesAsync hit a concurrent update while saving notification template; returning Conflict."); return AdministrationUpdateResult.Conflict; }
    }

    public async Task<IReadOnlyList<ManagedPerson>> ListPersonsAsync(CancellationToken cancellationToken)
        => await dbContext.Persons.AsNoTracking().OrderBy(person => person.DisplayName).Select(person => new ManagedPerson(person.PersonId, person.DisplayName, person.IsActive, person.ValidFromUtc, person.ValidUntilUtc, person.NotificationEmailOverride, Convert.ToBase64String(person.RowVersion))).ToListAsync(cancellationToken);

    public async Task<AdministrationUpdateResult> CreatePersonFromDirectoryAccountAsync(CreatePersonFromDirectoryAccountRequest request, ResolvedDirectoryAccount resolvedAccount, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow(); await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var existingAccount = await dbContext.DirectoryAccounts.SingleOrDefaultAsync(account => account.DirectoryScopeId == request.Actor.DirectoryScopeId && account.ObjectGuid == resolvedAccount.ObjectGuid, cancellationToken);
        if (existingAccount is not null && await dbContext.PersonAccountLinks.AnyAsync(link => link.AccountId == existingAccount.AccountId, cancellationToken)) return AdministrationUpdateResult.Conflict;
        var person = new PersonEntity { PersonId=Guid.NewGuid(), DisplayName=resolvedAccount.DisplayName, IsActive=true, ValidFromUtc=now, CreatedUtc=now, ModifiedUtc=now };
        var account = existingAccount ?? new DirectoryAccountEntity { AccountId=Guid.NewGuid(), DirectoryScopeId=request.Actor.DirectoryScopeId, ObjectGuid=resolvedAccount.ObjectGuid, SamAccountName=resolvedAccount.SamAccountName, DistinguishedName=resolvedAccount.DistinguishedName, DomainQualifiedName=resolvedAccount.DomainQualifiedName, DisplayName=resolvedAccount.DisplayName, CreatedUtc=now };
        account.ObjectSid=resolvedAccount.ObjectSid; account.SamAccountName=resolvedAccount.SamAccountName; account.UserPrincipalName=resolvedAccount.UserPrincipalName; account.EmailAddress=resolvedAccount.EmailAddress; account.DistinguishedName=resolvedAccount.DistinguishedName; account.DomainQualifiedName=resolvedAccount.DomainQualifiedName; account.DisplayName=resolvedAccount.DisplayName; account.IsEnabledInDirectory=resolvedAccount.IsEnabled; account.IsWithinAllowedScope=true; account.LastVerifiedUtc=now; account.IsActive=true; account.ModifiedUtc=now;
        dbContext.Persons.Add(person); if (existingAccount is null) dbContext.DirectoryAccounts.Add(account);
        dbContext.PersonAccountLinks.Add(new PersonAccountLinkEntity { PersonAccountLinkId=Guid.NewGuid(), PersonId=person.PersonId, AccountId=account.AccountId, MayAuthenticate=true, MayReceivePrivileges=false, IsActive=true, ValidFromUtc=now, CreatedBy=auditContext.FrontendClientId, ModifiedBy=auditContext.FrontendClientId, CreatedUtc=now, ModifiedUtc=now });
        var (administratorAccountId, administratorDisplayName) = await ResolveAdministratorAsync(request.Actor, cancellationToken);
        dbContext.AuditEvents.Add(new AuditEventEntity { EventId=Guid.NewGuid(), EventType="PersonOnboardedFromDirectoryAccount", OccurredUtc=now, FrontendClientId=auditContext.FrontendClientId, SourceIpAddress=auditContext.SourceIpAddress, ClientSourceIpAddress=auditContext.ClientSourceIpAddress, SourceComponent="Api", CorrelationId=auditContext.CorrelationId, ActorAccountId=administratorAccountId, ActorAccountDisplayNameSnapshot=administratorDisplayName, PersonId=person.PersonId, PersonDisplayNameSnapshot=person.DisplayName, TargetAccountId=account.AccountId, TargetAccountDisplayNameSnapshot=resolvedAccount.DomainQualifiedName, Result="Succeeded", AuthenticationMethod="Windows", PolicyRequirementsSummary="person-created;initial-actor-account-linked" });
        try { await dbContext.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return AdministrationUpdateResult.Updated; }
        catch (DbUpdateException ex) { logger.LogError(ex, "CreatePersonFromDirectoryAccountAsync failed to save changes for ObjectGuid {ObjectGuid}.", resolvedAccount.ObjectGuid); return AdministrationUpdateResult.Conflict; }
    }

    public Task<AdministrationUpdateResult> DeactivatePersonAsync(DeactivatePersonRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
        => SetPersonActiveAsync(request.Actor, request.PersonId, request.RowVersion, false, "PersonDeactivated", auditContext, cancellationToken);

    public Task<AdministrationUpdateResult> ReactivatePersonAsync(ReactivatePersonRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
        => SetPersonActiveAsync(request.Actor, request.PersonId, request.RowVersion, true, "PersonReactivated", auditContext, cancellationToken);

    private async Task<AdministrationUpdateResult> SetPersonActiveAsync(AdministrationActor actor, Guid personId, string encodedRowVersion, bool active, string eventType, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        byte[] rowVersion; try { rowVersion = Convert.FromBase64String(encodedRowVersion); } catch (FormatException) { return AdministrationUpdateResult.Invalid; }
        var person = await dbContext.Persons.SingleOrDefaultAsync(x => x.PersonId == personId, cancellationToken); if (person is null) return AdministrationUpdateResult.NotFound;
        if (!person.RowVersion.SequenceEqual(rowVersion) || person.IsActive == active)
        {
            logger.LogWarning("SetPersonActiveAsync conflict for PersonId {PersonId}: RowVersionMismatch={RowVersionMismatch}, AlreadyInTargetState={AlreadyInTargetState}.",
                personId, !person.RowVersion.SequenceEqual(rowVersion), person.IsActive == active);
            return AdministrationUpdateResult.Conflict;
        }
        var now = timeProvider.GetUtcNow(); person.IsActive = active; person.ModifiedUtc = now;
        var (administratorAccountId, administratorDisplayName) = await ResolveAdministratorAsync(actor, cancellationToken);
        dbContext.AuditEvents.Add(new AuditEventEntity { EventId=Guid.NewGuid(), EventType=eventType, OccurredUtc=now, FrontendClientId=auditContext.FrontendClientId, SourceIpAddress=auditContext.SourceIpAddress, ClientSourceIpAddress=auditContext.ClientSourceIpAddress, SourceComponent="Api", CorrelationId=auditContext.CorrelationId, ActorAccountId=administratorAccountId, ActorAccountDisplayNameSnapshot=administratorDisplayName, PersonId=person.PersonId, PersonDisplayNameSnapshot=person.DisplayName, Result="Succeeded", AuthenticationMethod="Windows", PolicyRequirementsSummary=active ? "person-reactivated" : "person-deactivated" });

        if (!active)
        {
            // Deactivating a person must not leave stale "active" account links or
            // entitlements behind. Authorization already denies both independently
            // once the person is inactive, but leaving them marked active is
            // misleading administrative state and blocks a clean reactivation story
            // (reactivating a link/entitlement requires deliberate, explicit review,
            // not an implicit revival of whatever was active before deactivation).
            var links = await (from link in dbContext.PersonAccountLinks
                                join account in dbContext.DirectoryAccounts on link.AccountId equals account.AccountId
                                where link.PersonId == personId && link.IsActive
                                select new { link, account }).ToListAsync(cancellationToken);
            foreach (var entry in links)
            {
                entry.link.IsActive = false; entry.link.ModifiedUtc = now; entry.link.ModifiedBy = auditContext.FrontendClientId;
                dbContext.AuditEvents.Add(AccountLinkAudit("PersonAccountLinkDeactivated", now, person, entry.account, auditContext, administratorAccountId, administratorDisplayName, "person-account-link-deactivated;cascaded-from-person-deactivation"));
            }

            var entitlements = await (from entitlement in dbContext.DirectEntitlements
                                       join account in dbContext.DirectoryAccounts on entitlement.TargetAccountId equals account.AccountId
                                       join targetGroup in dbContext.TargetGroups on entitlement.TargetGroupId equals targetGroup.TargetGroupId
                                       where entitlement.PersonId == personId && entitlement.IsActive
                                       select new { entitlement, account, targetGroup }).ToListAsync(cancellationToken);
            foreach (var entry in entitlements)
            {
                entry.entitlement.IsActive = false; entry.entitlement.ModifiedUtc = now; entry.entitlement.ModifiedBy = auditContext.FrontendClientId;
                dbContext.AuditEvents.Add(new AuditEventEntity { EventId=Guid.NewGuid(), EventType="DirectEntitlementDeactivated", OccurredUtc=now, FrontendClientId=auditContext.FrontendClientId, SourceIpAddress=auditContext.SourceIpAddress, ClientSourceIpAddress=auditContext.ClientSourceIpAddress, SourceComponent="Api", CorrelationId=auditContext.CorrelationId, ActorAccountId=administratorAccountId, ActorAccountDisplayNameSnapshot=administratorDisplayName, PersonId=personId, PersonDisplayNameSnapshot=person.DisplayName, TargetAccountId=entry.entitlement.TargetAccountId, TargetAccountDisplayNameSnapshot=entry.account.DomainQualifiedName, TargetGroupId=entry.entitlement.TargetGroupId, TargetGroupDisplayNameSnapshot=entry.targetGroup.DomainQualifiedName, Result="Succeeded", AuthenticationMethod="Windows", PolicyRequirementsSummary="direct-entitlement-deactivated;cascaded-from-person-deactivation" });
            }
        }

        try { await dbContext.SaveChangesAsync(cancellationToken); return AdministrationUpdateResult.Updated; } catch (DbUpdateConcurrencyException ex) { logger.LogWarning(ex, "SaveChangesAsync hit a concurrent update; returning Conflict."); return AdministrationUpdateResult.Conflict; }
    }

    public async Task<ManagedPersonDetail?> GetPersonDetailAsync(Guid personId, CancellationToken cancellationToken)
    {
        var person = await dbContext.Persons.AsNoTracking().Where(x => x.PersonId == personId)
            .Select(x => new ManagedPerson(x.PersonId, x.DisplayName, x.IsActive, x.ValidFromUtc, x.ValidUntilUtc, x.NotificationEmailOverride, Convert.ToBase64String(x.RowVersion))).SingleOrDefaultAsync(cancellationToken);
        if (person is null) return null;
        var accounts = await (from link in dbContext.PersonAccountLinks.AsNoTracking()
                              join account in dbContext.DirectoryAccounts.AsNoTracking() on link.AccountId equals account.AccountId
                              where link.PersonId == personId
                              orderby link.IsActive descending, account.DomainQualifiedName
                              select new ManagedPersonAccountLink(link.PersonAccountLinkId, account.AccountId, account.DisplayName, account.DomainQualifiedName, account.UserPrincipalName, account.EmailAddress, account.IsEnabledInDirectory, link.IsActive, link.MayAuthenticate, link.MayReceivePrivileges, link.MayApprove, link.ValidFromUtc, link.ValidUntilUtc, Convert.ToBase64String(link.RowVersion))).ToListAsync(cancellationToken);
        return new ManagedPersonDetail(person, accounts);
    }

    public async Task<AdministrationUpdateResult> CreatePersonAccountLinkAsync(CreatePersonAccountLinkRequest request, ResolvedDirectoryAccount resolvedAccount, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var person = await dbContext.Persons.SingleOrDefaultAsync(x => x.PersonId == request.PersonId, cancellationToken);
        if (person is null) return AdministrationUpdateResult.NotFound;
        if (!person.IsActive || person.ValidFromUtc > now || (person.ValidUntilUtc is not null && person.ValidUntilUtc <= now))
        {
            logger.LogWarning("CreatePersonAccountLinkAsync invalid for PersonId {PersonId}: PersonInactive={PersonInactive}, NotYetValid={NotYetValid}, NoLongerValid={NoLongerValid}.",
                request.PersonId, !person.IsActive, person.ValidFromUtc > now, person.ValidUntilUtc is not null && person.ValidUntilUtc <= now);
            return AdministrationUpdateResult.Invalid;
        }
        if (request.MayApprove && !request.MayAuthenticate) return AdministrationUpdateResult.Invalid;
        if (request.MayAuthenticate && await dbContext.PersonAccountLinks.AnyAsync(link => link.PersonId == person.PersonId && link.IsActive && link.MayAuthenticate, cancellationToken)) return AdministrationUpdateResult.Conflict;
        var account = await dbContext.DirectoryAccounts.SingleOrDefaultAsync(x => x.DirectoryScopeId == request.Actor.DirectoryScopeId && x.ObjectGuid == resolvedAccount.ObjectGuid, cancellationToken);
        if (account is not null && await dbContext.PersonAccountLinks.AnyAsync(link => link.AccountId == account.AccountId, cancellationToken)) return AdministrationUpdateResult.Conflict;
        account ??= new DirectoryAccountEntity { AccountId=Guid.NewGuid(), DirectoryScopeId=request.Actor.DirectoryScopeId, ObjectGuid=resolvedAccount.ObjectGuid, SamAccountName=resolvedAccount.SamAccountName, DistinguishedName=resolvedAccount.DistinguishedName, DomainQualifiedName=resolvedAccount.DomainQualifiedName, DisplayName=resolvedAccount.DisplayName, CreatedUtc=now };
        ApplyResolvedAccount(account, resolvedAccount, now);
        if (dbContext.Entry(account).State == EntityState.Detached) dbContext.DirectoryAccounts.Add(account);
        var link = new PersonAccountLinkEntity { PersonAccountLinkId=Guid.NewGuid(), PersonId=person.PersonId, AccountId=account.AccountId, MayAuthenticate=request.MayAuthenticate, MayReceivePrivileges=request.MayReceivePrivileges, MayApprove=request.MayApprove, IsActive=true, ValidFromUtc=now, CreatedBy=auditContext.FrontendClientId, ModifiedBy=auditContext.FrontendClientId, CreatedUtc=now, ModifiedUtc=now };
        dbContext.PersonAccountLinks.Add(link);
        var (linkAdministratorAccountId, linkAdministratorDisplayName) = await ResolveAdministratorAsync(request.Actor, cancellationToken);
        dbContext.AuditEvents.Add(AccountLinkAudit("PersonAccountLinked", now, person, account, auditContext, linkAdministratorAccountId, linkAdministratorDisplayName, $"authenticate:{request.MayAuthenticate};receive-privileges:{request.MayReceivePrivileges};approve:{request.MayApprove}"));
        try { await dbContext.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return AdministrationUpdateResult.Updated; }
        catch (DbUpdateException ex) { logger.LogError(ex, "CreatePersonAccountLinkAsync failed to save changes for PersonId {PersonId}.", request.PersonId); return AdministrationUpdateResult.Conflict; }
    }

    public async Task<AdministrationUpdateResult> UpdatePersonAccountLinkPurposesAsync(UpdatePersonAccountLinkPurposesRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        if (!TryRowVersion(request.RowVersion, out var rowVersion)) return AdministrationUpdateResult.Invalid;
        var link = await dbContext.PersonAccountLinks.SingleOrDefaultAsync(x => x.PersonAccountLinkId == request.PersonAccountLinkId && x.PersonId == request.PersonId, cancellationToken);
        if (link is null) return AdministrationUpdateResult.NotFound;
        var person = await dbContext.Persons.SingleAsync(x => x.PersonId == link.PersonId, cancellationToken);
        var account = await dbContext.DirectoryAccounts.SingleAsync(x => x.AccountId == link.AccountId, cancellationToken);
        if (request.MayApprove && !request.MayAuthenticate) return AdministrationUpdateResult.Invalid;
        var noPurposeChange = link.MayAuthenticate == request.MayAuthenticate && link.MayReceivePrivileges == request.MayReceivePrivileges && link.MayApprove == request.MayApprove;
        if (!link.RowVersion.SequenceEqual(rowVersion) || !link.IsActive || !person.IsActive || noPurposeChange)
        {
            logger.LogWarning("UpdatePersonAccountLinkPurposesAsync conflict for PersonAccountLinkId {PersonAccountLinkId}: RowVersionMismatch={RowVersionMismatch}, LinkInactive={LinkInactive}, PersonInactive={PersonInactive}, NoPurposeChange={NoPurposeChange}.",
                request.PersonAccountLinkId, !link.RowVersion.SequenceEqual(rowVersion), !link.IsActive, !person.IsActive, noPurposeChange);
            return AdministrationUpdateResult.Conflict;
        }
        if (request.MayAuthenticate && !link.MayAuthenticate && await dbContext.PersonAccountLinks.AnyAsync(candidate => candidate.PersonId == link.PersonId && candidate.PersonAccountLinkId != link.PersonAccountLinkId && candidate.IsActive && candidate.MayAuthenticate, cancellationToken))
        {
            logger.LogWarning("UpdatePersonAccountLinkPurposesAsync conflict for PersonAccountLinkId {PersonAccountLinkId}: another active link for PersonId {PersonId} already has MayAuthenticate.", request.PersonAccountLinkId, link.PersonId);
            return AdministrationUpdateResult.Conflict;
        }
        var before = $"authenticate:{link.MayAuthenticate};receive-privileges:{link.MayReceivePrivileges};approve:{link.MayApprove}";
        var now = timeProvider.GetUtcNow(); link.MayAuthenticate=request.MayAuthenticate; link.MayReceivePrivileges=request.MayReceivePrivileges; link.MayApprove=request.MayApprove; link.ModifiedBy=auditContext.FrontendClientId; link.ModifiedUtc=now;
        var (purposesAdministratorAccountId, purposesAdministratorDisplayName) = await ResolveAdministratorAsync(request.Actor, cancellationToken);
        dbContext.AuditEvents.Add(AccountLinkAudit("PersonAccountLinkPurposesUpdated", now, person, account, auditContext, purposesAdministratorAccountId, purposesAdministratorDisplayName, $"before:{before};after:authenticate:{link.MayAuthenticate};receive-privileges:{link.MayReceivePrivileges};approve:{link.MayApprove}"));
        try { await dbContext.SaveChangesAsync(cancellationToken); return AdministrationUpdateResult.Updated; } catch (DbUpdateConcurrencyException ex) { logger.LogWarning(ex, "SaveChangesAsync hit a concurrent update; returning Conflict."); return AdministrationUpdateResult.Conflict; }
    }

    public Task<AdministrationUpdateResult> DeactivatePersonAccountLinkAsync(DeactivatePersonAccountLinkRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
        => SetPersonAccountLinkActiveAsync(request.Actor, request.PersonId, request.PersonAccountLinkId, request.RowVersion, false, null, auditContext, cancellationToken);

    public Task<Guid?> GetPersonAccountLinkObjectGuidAsync(Guid personId, Guid personAccountLinkId, CancellationToken cancellationToken)
        => (from link in dbContext.PersonAccountLinks.AsNoTracking() join account in dbContext.DirectoryAccounts.AsNoTracking() on link.AccountId equals account.AccountId where link.PersonId == personId && link.PersonAccountLinkId == personAccountLinkId select (Guid?)account.ObjectGuid).SingleOrDefaultAsync(cancellationToken);

    public Task<AdministrationUpdateResult> ReactivatePersonAccountLinkAsync(ReactivatePersonAccountLinkRequest request, ResolvedDirectoryAccount resolvedAccount, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
        => SetPersonAccountLinkActiveAsync(request.Actor, request.PersonId, request.PersonAccountLinkId, request.RowVersion, true, resolvedAccount, auditContext, cancellationToken);

    public async Task<IReadOnlyList<ManagedTotpFactor>> ListTotpFactorsAsync(Guid personId, CancellationToken cancellationToken)
        => await dbContext.TotpFactors.AsNoTracking().Where(x => x.PersonId == personId)
            .OrderByDescending(x => x.EnrolledUtc)
            .Select(x => new ManagedTotpFactor(x.TotpFactorId, x.PersonId, x.EnrolledUtc, x.ConfirmedUtc, x.IsActive, x.LockedUntilUtc, x.RevokedUtc, x.RevokedBy, Convert.ToBase64String(x.RowVersion)))
            .ToListAsync(cancellationToken);

    /// <summary>the only authorized path to reset a TOTP factor. Soft-delete only - never a hard DELETE, so EnrolledUtc/ConfirmedUtc history survives for audit.</summary>
    public async Task<AdministrationUpdateResult> RevokeTotpFactorAsync(RevokeTotpFactorRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        if (!TryRowVersion(request.RowVersion, out var rowVersion)) return AdministrationUpdateResult.Invalid;
        var factor = await dbContext.TotpFactors.SingleOrDefaultAsync(x => x.TotpFactorId == request.TotpFactorId && x.PersonId == request.PersonId, cancellationToken);
        if (factor is null) return AdministrationUpdateResult.NotFound;
        if (!factor.RowVersion.SequenceEqual(rowVersion) || factor.RevokedUtc is not null)
        {
            logger.LogWarning("RevokeTotpFactorAsync conflict for TotpFactorId {TotpFactorId}: RowVersionMismatch={RowVersionMismatch}, AlreadyRevoked={AlreadyRevoked}.",
                request.TotpFactorId, !factor.RowVersion.SequenceEqual(rowVersion), factor.RevokedUtc is not null);
            return AdministrationUpdateResult.Conflict;
        }
        var person = await dbContext.Persons.SingleAsync(x => x.PersonId == request.PersonId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        // CK_TotpFactors_State requires IsActive = 0 whenever RevokedUtc is set - both must change together.
        factor.IsActive = false; factor.RevokedUtc = now; factor.RevokedBy = auditContext.FrontendClientId;
        var (administratorAccountId, administratorDisplayName) = await ResolveAdministratorAsync(request.Actor, cancellationToken);
        dbContext.AuditEvents.Add(new AuditEventEntity { EventId = Guid.NewGuid(), EventType = "TotpFactorRevoked", OccurredUtc = now, FrontendClientId = auditContext.FrontendClientId, SourceIpAddress = auditContext.SourceIpAddress, ClientSourceIpAddress = auditContext.ClientSourceIpAddress, SourceComponent = "Api", CorrelationId = auditContext.CorrelationId, ActorAccountId = administratorAccountId, ActorAccountDisplayNameSnapshot = administratorDisplayName, PersonId = person.PersonId, PersonDisplayNameSnapshot = person.DisplayName, Result = "Succeeded", AuthenticationMethod = "Windows", PolicyRequirementsSummary = $"totpFactorId={factor.TotpFactorId:D};enrolledUtc={factor.EnrolledUtc:O};confirmedUtc={factor.ConfirmedUtc:O}" });
        try { await dbContext.SaveChangesAsync(cancellationToken); return AdministrationUpdateResult.Updated; } catch (DbUpdateConcurrencyException ex) { logger.LogWarning(ex, "SaveChangesAsync hit a concurrent update; returning Conflict."); return AdministrationUpdateResult.Conflict; }
    }

    public async Task<IReadOnlyList<ManagedFido2Credential>> ListFido2CredentialsAsync(Guid personId, CancellationToken cancellationToken)
        => await dbContext.Fido2Credentials.AsNoTracking().Where(x => x.PersonId == personId)
            .OrderByDescending(x => x.CreatedUtc)
            .Select(x => new ManagedFido2Credential(x.Fido2CredentialId, x.PersonId, x.Label, x.Aaguid, x.CreatedUtc, x.RevokedUtc, x.RevokedBy, Convert.ToBase64String(x.RowVersion)))
            .ToListAsync(cancellationToken);

    /// <summary>Matches TOTP's admin-only reset model (RevokeTotpFactorAsync). Soft-delete only - never a hard DELETE, so CreatedUtc history survives for audit.</summary>
    public async Task<AdministrationUpdateResult> RevokeFido2CredentialAsync(RevokeFido2CredentialRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        if (!TryRowVersion(request.RowVersion, out var rowVersion)) return AdministrationUpdateResult.Invalid;
        var credential = await dbContext.Fido2Credentials.SingleOrDefaultAsync(x => x.Fido2CredentialId == request.Fido2CredentialId && x.PersonId == request.PersonId, cancellationToken);
        if (credential is null) return AdministrationUpdateResult.NotFound;
        if (!credential.RowVersion.SequenceEqual(rowVersion) || credential.RevokedUtc is not null)
        {
            logger.LogWarning("RevokeFido2CredentialAsync conflict for Fido2CredentialId {Fido2CredentialId}: RowVersionMismatch={RowVersionMismatch}, AlreadyRevoked={AlreadyRevoked}.",
                request.Fido2CredentialId, !credential.RowVersion.SequenceEqual(rowVersion), credential.RevokedUtc is not null);
            return AdministrationUpdateResult.Conflict;
        }
        var person = await dbContext.Persons.SingleAsync(x => x.PersonId == request.PersonId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        credential.RevokedUtc = now; credential.RevokedBy = auditContext.FrontendClientId;
        var (administratorAccountId, administratorDisplayName) = await ResolveAdministratorAsync(request.Actor, cancellationToken);
        dbContext.AuditEvents.Add(new AuditEventEntity { EventId = Guid.NewGuid(), EventType = "Fido2CredentialRevoked", OccurredUtc = now, FrontendClientId = auditContext.FrontendClientId, SourceIpAddress = auditContext.SourceIpAddress, ClientSourceIpAddress = auditContext.ClientSourceIpAddress, SourceComponent = "Api", CorrelationId = auditContext.CorrelationId, ActorAccountId = administratorAccountId, ActorAccountDisplayNameSnapshot = administratorDisplayName, PersonId = person.PersonId, PersonDisplayNameSnapshot = person.DisplayName, Result = "Succeeded", AuthenticationMethod = "Windows", PolicyRequirementsSummary = $"fido2CredentialId={credential.Fido2CredentialId:D};createdUtc={credential.CreatedUtc:O}" });
        try { await dbContext.SaveChangesAsync(cancellationToken); return AdministrationUpdateResult.Updated; } catch (DbUpdateConcurrencyException ex) { logger.LogWarning(ex, "SaveChangesAsync hit a concurrent update; returning Conflict."); return AdministrationUpdateResult.Conflict; }
    }

    private async Task<AdministrationUpdateResult> SetPersonAccountLinkActiveAsync(AdministrationActor actor, Guid personId, Guid linkId, string encodedRowVersion, bool active, ResolvedDirectoryAccount? resolvedAccount, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        if (!TryRowVersion(encodedRowVersion, out var rowVersion)) return AdministrationUpdateResult.Invalid;
        var link = await dbContext.PersonAccountLinks.SingleOrDefaultAsync(x => x.PersonAccountLinkId == linkId && x.PersonId == personId, cancellationToken);
        if (link is null) return AdministrationUpdateResult.NotFound;
        var person = await dbContext.Persons.SingleAsync(x => x.PersonId == link.PersonId, cancellationToken);
        var account = await dbContext.DirectoryAccounts.SingleAsync(x => x.AccountId == link.AccountId, cancellationToken);
        if (!link.RowVersion.SequenceEqual(rowVersion) || link.IsActive == active || (active && (!person.IsActive || resolvedAccount is null || account.ObjectGuid != resolvedAccount.ObjectGuid)))
        {
            logger.LogWarning("SetPersonAccountLinkActiveAsync conflict for PersonAccountLinkId {PersonAccountLinkId}: RowVersionMismatch={RowVersionMismatch}, AlreadyInTargetState={AlreadyInTargetState}, PersonInactive={PersonInactive}, AccountNotResolved={AccountNotResolved}, ObjectGuidMismatch={ObjectGuidMismatch}.",
                linkId, !link.RowVersion.SequenceEqual(rowVersion), link.IsActive == active, active && !person.IsActive, active && resolvedAccount is null, active && resolvedAccount is not null && account.ObjectGuid != resolvedAccount.ObjectGuid);
            return AdministrationUpdateResult.Conflict;
        }
        if (active && link.MayAuthenticate && await dbContext.PersonAccountLinks.AnyAsync(candidate => candidate.PersonId == link.PersonId && candidate.PersonAccountLinkId != link.PersonAccountLinkId && candidate.IsActive && candidate.MayAuthenticate, cancellationToken))
        {
            logger.LogWarning("SetPersonAccountLinkActiveAsync conflict for PersonAccountLinkId {PersonAccountLinkId}: another active link for PersonId {PersonId} already has MayAuthenticate.", linkId, link.PersonId);
            return AdministrationUpdateResult.Conflict;
        }
        var now = timeProvider.GetUtcNow();
        if (resolvedAccount is not null) ApplyResolvedAccount(account, resolvedAccount, now);
        link.IsActive=active; link.ModifiedBy=auditContext.FrontendClientId; link.ModifiedUtc=now;
        var (linkActiveAdministratorAccountId, linkActiveAdministratorDisplayName) = await ResolveAdministratorAsync(actor, cancellationToken);
        dbContext.AuditEvents.Add(AccountLinkAudit(active ? "PersonAccountLinkReactivated" : "PersonAccountLinkDeactivated", now, person, account, auditContext, linkActiveAdministratorAccountId, linkActiveAdministratorDisplayName, active ? "person-account-link-reactivated" : "person-account-link-deactivated"));
        try { await dbContext.SaveChangesAsync(cancellationToken); return AdministrationUpdateResult.Updated; } catch (DbUpdateConcurrencyException ex) { logger.LogWarning(ex, "SaveChangesAsync hit a concurrent update; returning Conflict."); return AdministrationUpdateResult.Conflict; }
    }

    private static bool TryRowVersion(string encoded, out byte[] rowVersion) { try { rowVersion = Convert.FromBase64String(encoded); return true; } catch (FormatException) { rowVersion=[]; return false; } }
    private static void ApplyResolvedAccount(DirectoryAccountEntity account, ResolvedDirectoryAccount resolved, DateTimeOffset now) { account.ObjectSid=resolved.ObjectSid; account.SamAccountName=resolved.SamAccountName; account.UserPrincipalName=resolved.UserPrincipalName; account.EmailAddress=resolved.EmailAddress; account.DistinguishedName=resolved.DistinguishedName; account.DomainQualifiedName=resolved.DomainQualifiedName; account.DisplayName=resolved.DisplayName; account.IsEnabledInDirectory=resolved.IsEnabled; account.IsWithinAllowedScope=true; account.LastVerifiedUtc=now; account.IsActive=true; account.ModifiedUtc=now; }
    private static AuditEventEntity AccountLinkAudit(string eventType, DateTimeOffset now, PersonEntity person, DirectoryAccountEntity account, AdministrationAuditContext context, Guid? administratorAccountId, string? administratorDisplayName, string summary)
        => new() { EventId=Guid.NewGuid(), EventType=eventType, OccurredUtc=now, FrontendClientId=context.FrontendClientId, SourceIpAddress=context.SourceIpAddress, ClientSourceIpAddress=context.ClientSourceIpAddress, SourceComponent="Api", CorrelationId=context.CorrelationId, ActorAccountId=administratorAccountId, ActorAccountDisplayNameSnapshot=administratorDisplayName, PersonId=person.PersonId, PersonDisplayNameSnapshot=person.DisplayName, TargetAccountId=account.AccountId, TargetAccountDisplayNameSnapshot=account.DomainQualifiedName, Result="Succeeded", AuthenticationMethod="Windows", PolicyRequirementsSummary=summary };

    public Task<Guid?> GetTargetGroupObjectGuidAsync(Guid targetGroupId, CancellationToken cancellationToken)
        => dbContext.TargetGroups.AsNoTracking().Where(group => group.TargetGroupId == targetGroupId).Select(group => (Guid?)group.ObjectGuid).SingleOrDefaultAsync(cancellationToken);

    public async Task<AdministrationUpdateResult> CreateTargetGroupAsync(CreateTargetGroupRequest request, ResolvedDirectoryGroup resolvedGroup, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        if (await dbContext.TargetGroups.AnyAsync(x => x.DirectoryScopeId == request.Actor.DirectoryScopeId && x.ObjectGuid == resolvedGroup.ObjectGuid, cancellationToken)) return AdministrationUpdateResult.Conflict;
        if (!await dbContext.DirectoryScopes.AnyAsync(x => x.DirectoryScopeId == request.Actor.DirectoryScopeId && x.IsActive, cancellationToken)) return AdministrationUpdateResult.Invalid;
        var now = timeProvider.GetUtcNow(); var policyId = Guid.NewGuid(); var groupId = Guid.NewGuid();
        dbContext.GroupPolicies.Add(new GroupPolicyEntity { GroupPolicyId = policyId, MinimumTtlSeconds = request.MinimumTtlSeconds, MaximumTtlSeconds = request.MaximumTtlSeconds, DefaultTtlSeconds = request.DefaultTtlSeconds, AllowedTtlStepSeconds = request.AllowedTtlStepSeconds, RequiresSecondFactor = request.RequiresSecondFactor, AllowedSecondFactorTypes = (ADDS.PIM.Domain.Security.SecondFactorType)request.AllowedSecondFactorTypes, RequiresTicket = request.RequiresTicket, RequiresApproval = request.RequiresApproval, IsActive = true, CreatedUtc = now, ModifiedUtc = now });
        dbContext.TargetGroups.Add(new TargetGroupEntity { TargetGroupId = groupId, DirectoryScopeId = request.Actor.DirectoryScopeId, ObjectGuid = resolvedGroup.ObjectGuid, ObjectSid = resolvedGroup.ObjectSid, SamAccountName = resolvedGroup.SamAccountName, DistinguishedName = resolvedGroup.DistinguishedName, DomainQualifiedName = resolvedGroup.DomainQualifiedName, DisplayName = resolvedGroup.DisplayName, GroupPolicyId = policyId, IsEnabledForRequests = true, IsWithinAllowedScope = true, LastVerifiedUtc = now, CreatedUtc = now, ModifiedUtc = now });
        var (createGroupAdministratorAccountId, createGroupAdministratorDisplayName) = await ResolveAdministratorAsync(request.Actor, cancellationToken);
        dbContext.AuditEvents.Add(new AuditEventEntity { EventId=Guid.NewGuid(), EventType="TargetGroupRegistered", OccurredUtc=now, FrontendClientId=auditContext.FrontendClientId, SourceIpAddress=auditContext.SourceIpAddress, ClientSourceIpAddress=auditContext.ClientSourceIpAddress, SourceComponent="Api", CorrelationId=auditContext.CorrelationId, ActorAccountId=createGroupAdministratorAccountId, ActorAccountDisplayNameSnapshot=createGroupAdministratorDisplayName, TargetGroupId=groupId, TargetGroupDisplayNameSnapshot=resolvedGroup.DomainQualifiedName, Result="Succeeded", AuthenticationMethod="Windows", PolicyRequirementsSummary="target-group-registered" });
        try { await dbContext.SaveChangesAsync(cancellationToken); return AdministrationUpdateResult.Updated; }
        catch (DbUpdateException ex) { logger.LogError(ex, "CreateTargetGroupAsync failed to save changes for ObjectGuid {ObjectGuid}.", resolvedGroup.ObjectGuid); return AdministrationUpdateResult.Conflict; }
    }

    public async Task<AdministrationUpdateResult> DeactivateTargetGroupAsync(DeactivateTargetGroupRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        byte[] rowVersion; try { rowVersion = Convert.FromBase64String(request.RowVersion); } catch (FormatException) { return AdministrationUpdateResult.Invalid; }
        var group = await dbContext.TargetGroups.SingleOrDefaultAsync(x => x.TargetGroupId == request.TargetGroupId, cancellationToken); if (group is null) return AdministrationUpdateResult.NotFound;
        var policy = await dbContext.GroupPolicies.SingleAsync(x => x.GroupPolicyId == group.GroupPolicyId, cancellationToken);
        if (!policy.RowVersion.SequenceEqual(rowVersion) || !group.IsEnabledForRequests || !policy.IsActive)
        {
            logger.LogWarning("DeactivateTargetGroupAsync conflict for TargetGroupId {TargetGroupId}: RowVersionMismatch={RowVersionMismatch}, AlreadyDisabled={AlreadyDisabled}, PolicyAlreadyInactive={PolicyAlreadyInactive}.",
                request.TargetGroupId, !policy.RowVersion.SequenceEqual(rowVersion), !group.IsEnabledForRequests, !policy.IsActive);
            return AdministrationUpdateResult.Conflict;
        }
        group.IsEnabledForRequests = false; policy.IsActive = false; group.ModifiedUtc = policy.ModifiedUtc = timeProvider.GetUtcNow();
        var (deactivateGroupAdministratorAccountId, deactivateGroupAdministratorDisplayName) = await ResolveAdministratorAsync(request.Actor, cancellationToken);
        dbContext.AuditEvents.Add(new AuditEventEntity { EventId=Guid.NewGuid(), EventType="TargetGroupDeactivated", OccurredUtc=policy.ModifiedUtc, FrontendClientId=auditContext.FrontendClientId, SourceIpAddress=auditContext.SourceIpAddress, ClientSourceIpAddress=auditContext.ClientSourceIpAddress, SourceComponent="Api", CorrelationId=auditContext.CorrelationId, ActorAccountId=deactivateGroupAdministratorAccountId, ActorAccountDisplayNameSnapshot=deactivateGroupAdministratorDisplayName, TargetGroupId=group.TargetGroupId, TargetGroupDisplayNameSnapshot=group.DomainQualifiedName, Result="Succeeded", AuthenticationMethod="Windows", PolicyRequirementsSummary="target-group-deactivated" });
        try { await dbContext.SaveChangesAsync(cancellationToken); return AdministrationUpdateResult.Updated; } catch (DbUpdateConcurrencyException ex) { logger.LogWarning(ex, "SaveChangesAsync hit a concurrent update; returning Conflict."); return AdministrationUpdateResult.Conflict; }
    }

    public async Task<AdministrationUpdateResult> ReactivateTargetGroupAsync(ReactivateTargetGroupRequest request, ResolvedDirectoryGroup resolvedGroup, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        byte[] rowVersion; try { rowVersion = Convert.FromBase64String(request.RowVersion); } catch (FormatException) { return AdministrationUpdateResult.Invalid; }
        var group = await dbContext.TargetGroups.SingleOrDefaultAsync(x => x.TargetGroupId == request.TargetGroupId, cancellationToken); if (group is null) return AdministrationUpdateResult.NotFound;
        var policy = await dbContext.GroupPolicies.SingleAsync(x => x.GroupPolicyId == group.GroupPolicyId, cancellationToken);
        if (!policy.RowVersion.SequenceEqual(rowVersion) || group.IsEnabledForRequests || policy.IsActive || group.ObjectGuid != resolvedGroup.ObjectGuid)
        {
            logger.LogWarning("ReactivateTargetGroupAsync conflict for TargetGroupId {TargetGroupId}: RowVersionMismatch={RowVersionMismatch}, AlreadyEnabled={AlreadyEnabled}, PolicyAlreadyActive={PolicyAlreadyActive}, ObjectGuidMismatch={ObjectGuidMismatch}.",
                request.TargetGroupId, !policy.RowVersion.SequenceEqual(rowVersion), group.IsEnabledForRequests, policy.IsActive, group.ObjectGuid != resolvedGroup.ObjectGuid);
            return AdministrationUpdateResult.Conflict;
        }
        var now = timeProvider.GetUtcNow(); group.ObjectSid = resolvedGroup.ObjectSid; group.SamAccountName = resolvedGroup.SamAccountName; group.DistinguishedName = resolvedGroup.DistinguishedName; group.DomainQualifiedName = resolvedGroup.DomainQualifiedName; group.DisplayName = resolvedGroup.DisplayName; group.IsWithinAllowedScope = true; group.LastVerifiedUtc = now; group.ModifiedUtc = now; group.IsEnabledForRequests = true; policy.IsActive = true; policy.ModifiedUtc = now;
        var (reactivateGroupAdministratorAccountId, reactivateGroupAdministratorDisplayName) = await ResolveAdministratorAsync(request.Actor, cancellationToken);
        dbContext.AuditEvents.Add(new AuditEventEntity { EventId=Guid.NewGuid(), EventType="TargetGroupReactivated", OccurredUtc=now, FrontendClientId=auditContext.FrontendClientId, SourceIpAddress=auditContext.SourceIpAddress, ClientSourceIpAddress=auditContext.ClientSourceIpAddress, SourceComponent="Api", CorrelationId=auditContext.CorrelationId, ActorAccountId=reactivateGroupAdministratorAccountId, ActorAccountDisplayNameSnapshot=reactivateGroupAdministratorDisplayName, TargetGroupId=group.TargetGroupId, TargetGroupDisplayNameSnapshot=group.DomainQualifiedName, Result="Succeeded", AuthenticationMethod="Windows", PolicyRequirementsSummary="target-group-reactivated" });
        try { await dbContext.SaveChangesAsync(cancellationToken); return AdministrationUpdateResult.Updated; } catch (DbUpdateConcurrencyException ex) { logger.LogWarning(ex, "SaveChangesAsync hit a concurrent update; returning Conflict."); return AdministrationUpdateResult.Conflict; }
    }
    public async Task<IReadOnlyList<ManagedTargetGroup>> ListTargetGroupsAsync(CancellationToken cancellationToken)
        => await dbContext.TargetGroups.AsNoTracking().OrderBy(group => group.DomainQualifiedName).Join(dbContext.GroupPolicies.AsNoTracking(), group => group.GroupPolicyId, policy => policy.GroupPolicyId,
            (group, policy) => new ManagedTargetGroup(group.TargetGroupId, group.DisplayName, group.DomainQualifiedName, group.IsEnabledForRequests,
                policy.MinimumTtlSeconds, policy.MaximumTtlSeconds, policy.DefaultTtlSeconds, policy.AllowedTtlStepSeconds,
                policy.RequiresSecondFactor, (int)policy.AllowedSecondFactorTypes, policy.RequiresTicket, policy.RequiresApproval, policy.IsActive,
                Convert.ToBase64String(policy.RowVersion)))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ManagedDirectEntitlement>> ListDirectEntitlementsAsync(CancellationToken cancellationToken)
        => await (from entitlement in dbContext.DirectEntitlements.AsNoTracking()
            join person in dbContext.Persons.AsNoTracking() on entitlement.PersonId equals person.PersonId
            join account in dbContext.DirectoryAccounts.AsNoTracking() on entitlement.TargetAccountId equals account.AccountId
            join targetGroup in dbContext.TargetGroups.AsNoTracking() on entitlement.TargetGroupId equals targetGroup.TargetGroupId
            orderby person.DisplayName, targetGroup.DisplayName
            select new ManagedDirectEntitlement(entitlement.EntitlementId, person.PersonId, person.DisplayName, account.AccountId, account.DomainQualifiedName,
                targetGroup.TargetGroupId, targetGroup.DomainQualifiedName, entitlement.IsActive, entitlement.ValidFromUtc, entitlement.ValidUntilUtc,
                entitlement.MinimumTtlSeconds, entitlement.MaximumTtlSeconds, entitlement.AllowedTtlStepSeconds, entitlement.RequiresSecondFactor,
                entitlement.RequiresTicket, entitlement.RequiresApproval, Convert.ToBase64String(entitlement.RowVersion)))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<EntitlementSubject>> ListEligibleEntitlementSubjectsAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        return await (from link in dbContext.PersonAccountLinks.AsNoTracking()
                      join person in dbContext.Persons.AsNoTracking() on link.PersonId equals person.PersonId
                      join account in dbContext.DirectoryAccounts.AsNoTracking() on link.AccountId equals account.AccountId
                      where link.IsActive && link.MayReceivePrivileges && person.IsActive && account.IsActive && account.IsEnabledInDirectory && account.IsWithinAllowedScope
                          && link.ValidFromUtc <= now && (link.ValidUntilUtc == null || link.ValidUntilUtc > now)
                          && person.ValidFromUtc <= now && (person.ValidUntilUtc == null || person.ValidUntilUtc > now)
                      orderby person.DisplayName, account.DomainQualifiedName
                      select new EntitlementSubject(person.PersonId, person.DisplayName, account.AccountId, account.DomainQualifiedName)).ToListAsync(cancellationToken);
    }

    public async Task<AdministrationUpdateResult> CreateDirectEntitlementAsync(CreateDirectEntitlementRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var subject = await (from link in dbContext.PersonAccountLinks
                                  join person in dbContext.Persons on link.PersonId equals person.PersonId
                                  join account in dbContext.DirectoryAccounts on link.AccountId equals account.AccountId
                                  where link.PersonId == request.PersonId && link.AccountId == request.TargetAccountId
                                      && link.IsActive && link.MayReceivePrivileges && person.IsActive && account.IsActive && account.IsEnabledInDirectory && account.IsWithinAllowedScope
                                      && link.ValidFromUtc <= now && (link.ValidUntilUtc == null || link.ValidUntilUtc > now)
                                      && person.ValidFromUtc <= now && (person.ValidUntilUtc == null || person.ValidUntilUtc > now)
                                  select new { person.DisplayName, AccountDisplayName = account.DomainQualifiedName }).SingleOrDefaultAsync(cancellationToken);
        if (subject is null) return AdministrationUpdateResult.Invalid;
        var (createEntitlementAdministratorAccountId, createEntitlementAdministratorDisplayName) = await ResolveAdministratorAsync(request.Actor, cancellationToken);
        var targetGroup = await dbContext.TargetGroups.SingleOrDefaultAsync(group => group.TargetGroupId == request.TargetGroupId && group.IsWithinAllowedScope, cancellationToken);
        if (targetGroup is null) return AdministrationUpdateResult.NotFound;
        var overlaps = await dbContext.DirectEntitlements.AnyAsync(entitlement => entitlement.PersonId == request.PersonId && entitlement.TargetAccountId == request.TargetAccountId && entitlement.TargetGroupId == request.TargetGroupId && entitlement.IsActive
            && (entitlement.ValidUntilUtc == null || entitlement.ValidUntilUtc > request.ValidFromUtc)
            && (request.ValidUntilUtc == null || entitlement.ValidFromUtc < request.ValidUntilUtc), cancellationToken);
        if (overlaps) return AdministrationUpdateResult.Conflict;

        var previouslyDeactivated = await dbContext.DirectEntitlements
            .Where(entitlement => entitlement.PersonId == request.PersonId && entitlement.TargetAccountId == request.TargetAccountId && entitlement.TargetGroupId == request.TargetGroupId && !entitlement.IsActive)
            .OrderByDescending(entitlement => entitlement.ModifiedUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (previouslyDeactivated is not null)
        {
            previouslyDeactivated.IsActive = true;
            previouslyDeactivated.ValidFromUtc = request.ValidFromUtc;
            previouslyDeactivated.ValidUntilUtc = request.ValidUntilUtc;
            previouslyDeactivated.ModifiedUtc = now;
            previouslyDeactivated.ModifiedBy = auditContext.FrontendClientId;
            dbContext.AuditEvents.Add(new AuditEventEntity { EventId=Guid.NewGuid(), EventType="DirectEntitlementReactivated", OccurredUtc=now, FrontendClientId=auditContext.FrontendClientId, SourceIpAddress=auditContext.SourceIpAddress, ClientSourceIpAddress=auditContext.ClientSourceIpAddress, SourceComponent="Api", CorrelationId=auditContext.CorrelationId, ActorAccountId=createEntitlementAdministratorAccountId, ActorAccountDisplayNameSnapshot=createEntitlementAdministratorDisplayName, PersonId=request.PersonId, PersonDisplayNameSnapshot=subject.DisplayName, TargetAccountId=request.TargetAccountId, TargetAccountDisplayNameSnapshot=subject.AccountDisplayName, TargetGroupId=request.TargetGroupId, TargetGroupDisplayNameSnapshot=targetGroup.DomainQualifiedName, Result="Succeeded", AuthenticationMethod="Windows", PolicyRequirementsSummary="direct-entitlement-reactivated" });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AdministrationUpdateResult.Updated;
        }
        var entitlement = new DirectEntitlementEntity { EntitlementId=Guid.NewGuid(), PersonId=request.PersonId, TargetAccountId=request.TargetAccountId, TargetGroupId=request.TargetGroupId, IsActive=true, ValidFromUtc=request.ValidFromUtc, ValidUntilUtc=request.ValidUntilUtc, CreatedBy=auditContext.FrontendClientId, ModifiedBy=auditContext.FrontendClientId, CreatedUtc=now, ModifiedUtc=now };
        dbContext.DirectEntitlements.Add(entitlement);
        dbContext.AuditEvents.Add(new AuditEventEntity { EventId=Guid.NewGuid(), EventType="DirectEntitlementCreated", OccurredUtc=now, FrontendClientId=auditContext.FrontendClientId, SourceIpAddress=auditContext.SourceIpAddress, ClientSourceIpAddress=auditContext.ClientSourceIpAddress, SourceComponent="Api", CorrelationId=auditContext.CorrelationId, ActorAccountId=createEntitlementAdministratorAccountId, ActorAccountDisplayNameSnapshot=createEntitlementAdministratorDisplayName, PersonId=request.PersonId, PersonDisplayNameSnapshot=subject.DisplayName, TargetAccountId=request.TargetAccountId, TargetAccountDisplayNameSnapshot=subject.AccountDisplayName, TargetGroupId=request.TargetGroupId, TargetGroupDisplayNameSnapshot=targetGroup.DomainQualifiedName, Result="Succeeded", AuthenticationMethod="Windows", PolicyRequirementsSummary="direct-entitlement-created" });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AdministrationUpdateResult.Updated;
    }

    public async Task<AdministrationUpdateResult> UpdateDirectEntitlementValidityAsync(UpdateDirectEntitlementValidityRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        byte[] rowVersion;
        try { rowVersion = Convert.FromBase64String(request.RowVersion); } catch (Exception exception) when (exception is FormatException or ArgumentNullException) { return AdministrationUpdateResult.Invalid; }
        var entitlement = await dbContext.DirectEntitlements.SingleOrDefaultAsync(x => x.EntitlementId == request.EntitlementId, cancellationToken);
        if (entitlement is null) return AdministrationUpdateResult.NotFound;
        if (!entitlement.RowVersion.SequenceEqual(rowVersion))
        {
            logger.LogWarning("UpdateDirectEntitlementValidityAsync conflict for EntitlementId {EntitlementId}: RowVersionMismatch.", request.EntitlementId);
            return AdministrationUpdateResult.Conflict;
        }
        if (!entitlement.IsActive || (request.ValidUntilUtc is not null && request.ValidUntilUtc <= entitlement.ValidFromUtc))
        {
            logger.LogWarning("UpdateDirectEntitlementValidityAsync invalid for EntitlementId {EntitlementId}: EntitlementInactive={EntitlementInactive}, ValidUntilBeforeValidFrom={ValidUntilBeforeValidFrom}.",
                request.EntitlementId, !entitlement.IsActive, request.ValidUntilUtc is not null && request.ValidUntilUtc <= entitlement.ValidFromUtc);
            return AdministrationUpdateResult.Invalid;
        }
        var before = entitlement.ValidUntilUtc?.ToString("O") ?? "unbounded";
        entitlement.ValidUntilUtc = request.ValidUntilUtc; entitlement.ModifiedUtc = timeProvider.GetUtcNow(); entitlement.ModifiedBy = auditContext.FrontendClientId;
        var (validityAdministratorAccountId, validityAdministratorDisplayName) = await ResolveAdministratorAsync(request.Actor, cancellationToken);
        var (validityPersonName, validityTargetAccountName, validityTargetGroupName) = await ResolveEntitlementNamesAsync(entitlement.PersonId, entitlement.TargetAccountId, entitlement.TargetGroupId, cancellationToken);
        dbContext.AuditEvents.Add(new AuditEventEntity { EventId=Guid.NewGuid(), EventType="DirectEntitlementValidityUpdated", OccurredUtc=entitlement.ModifiedUtc, FrontendClientId=auditContext.FrontendClientId, SourceIpAddress=auditContext.SourceIpAddress, ClientSourceIpAddress=auditContext.ClientSourceIpAddress, SourceComponent="Api", CorrelationId=auditContext.CorrelationId, ActorAccountId=validityAdministratorAccountId, ActorAccountDisplayNameSnapshot=validityAdministratorDisplayName, PersonId=entitlement.PersonId, PersonDisplayNameSnapshot=validityPersonName, TargetAccountId=entitlement.TargetAccountId, TargetAccountDisplayNameSnapshot=validityTargetAccountName, TargetGroupId=entitlement.TargetGroupId, TargetGroupDisplayNameSnapshot=validityTargetGroupName, Result="Succeeded", AuthenticationMethod="Windows", PolicyRequirementsSummary=$"before:{before};after:{request.ValidUntilUtc?.ToString("O") ?? "unbounded"}" });
        try { await dbContext.SaveChangesAsync(cancellationToken); return AdministrationUpdateResult.Updated; } catch (DbUpdateConcurrencyException ex) { logger.LogWarning(ex, "SaveChangesAsync hit a concurrent update; returning Conflict."); return AdministrationUpdateResult.Conflict; }
    }

    public async Task<AdministrationUpdateResult> UpdateDirectEntitlementConstraintsAsync(UpdateDirectEntitlementConstraintsRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        byte[] rowVersion;
        try { rowVersion = Convert.FromBase64String(request.RowVersion); } catch (Exception exception) when (exception is FormatException or ArgumentNullException) { return AdministrationUpdateResult.Invalid; }
        var entitlement = await dbContext.DirectEntitlements.SingleOrDefaultAsync(x => x.EntitlementId == request.EntitlementId, cancellationToken);
        if (entitlement is null) return AdministrationUpdateResult.NotFound;
        if (!entitlement.RowVersion.SequenceEqual(rowVersion))
        {
            logger.LogWarning("UpdateDirectEntitlementConstraintsAsync conflict for EntitlementId {EntitlementId}: RowVersionMismatch.", request.EntitlementId);
            return AdministrationUpdateResult.Conflict;
        }
        var policy = await (from targetGroup in dbContext.TargetGroups join groupPolicy in dbContext.GroupPolicies on targetGroup.GroupPolicyId equals groupPolicy.GroupPolicyId where targetGroup.TargetGroupId == entitlement.TargetGroupId select groupPolicy).SingleAsync(cancellationToken);
        var minimum = request.MinimumTtlSeconds ?? policy.MinimumTtlSeconds; var maximum = request.MaximumTtlSeconds ?? policy.MaximumTtlSeconds; var step = request.AllowedTtlStepSeconds ?? policy.AllowedTtlStepSeconds;
        if (minimum < policy.MinimumTtlSeconds || maximum > policy.MaximumTtlSeconds || maximum < minimum || step < policy.AllowedTtlStepSeconds || step % policy.AllowedTtlStepSeconds != 0
            || (policy.RequiresSecondFactor && request.RequiresSecondFactor is false) || (policy.RequiresTicket && request.RequiresTicket is false) || (policy.RequiresApproval && request.RequiresApproval is false))
        {
            logger.LogWarning("UpdateDirectEntitlementConstraintsAsync invalid for EntitlementId {EntitlementId}: MinimumBelowPolicy={MinimumBelowPolicy}, MaximumAbovePolicy={MaximumAbovePolicy}, MaximumBelowMinimum={MaximumBelowMinimum}, StepBelowPolicy={StepBelowPolicy}, StepNotMultiple={StepNotMultiple}, DowngradesSecondFactor={DowngradesSecondFactor}, DowngradesTicket={DowngradesTicket}, DowngradesApproval={DowngradesApproval}.",
                request.EntitlementId, minimum < policy.MinimumTtlSeconds, maximum > policy.MaximumTtlSeconds, maximum < minimum, step < policy.AllowedTtlStepSeconds, step % policy.AllowedTtlStepSeconds != 0,
                policy.RequiresSecondFactor && request.RequiresSecondFactor is false, policy.RequiresTicket && request.RequiresTicket is false, policy.RequiresApproval && request.RequiresApproval is false);
            return AdministrationUpdateResult.Invalid;
        }
        var before = $"ttl={entitlement.MinimumTtlSeconds}-{entitlement.MaximumTtlSeconds};step={entitlement.AllowedTtlStepSeconds};mfa={entitlement.RequiresSecondFactor};ticket={entitlement.RequiresTicket};approval={entitlement.RequiresApproval}";
        entitlement.MinimumTtlSeconds=request.MinimumTtlSeconds; entitlement.MaximumTtlSeconds=request.MaximumTtlSeconds; entitlement.AllowedTtlStepSeconds=request.AllowedTtlStepSeconds; entitlement.RequiresSecondFactor=request.RequiresSecondFactor; entitlement.RequiresTicket=request.RequiresTicket; entitlement.RequiresApproval=request.RequiresApproval; entitlement.ModifiedUtc=timeProvider.GetUtcNow(); entitlement.ModifiedBy=auditContext.FrontendClientId;
        var after = $"ttl={entitlement.MinimumTtlSeconds}-{entitlement.MaximumTtlSeconds};step={entitlement.AllowedTtlStepSeconds};mfa={entitlement.RequiresSecondFactor};ticket={entitlement.RequiresTicket};approval={entitlement.RequiresApproval}";
        var (constraintsAdministratorAccountId, constraintsAdministratorDisplayName) = await ResolveAdministratorAsync(request.Actor, cancellationToken);
        var (constraintsPersonName, constraintsTargetAccountName, constraintsTargetGroupName) = await ResolveEntitlementNamesAsync(entitlement.PersonId, entitlement.TargetAccountId, entitlement.TargetGroupId, cancellationToken);
        dbContext.AuditEvents.Add(new AuditEventEntity { EventId=Guid.NewGuid(), EventType="DirectEntitlementConstraintsUpdated", OccurredUtc=entitlement.ModifiedUtc, FrontendClientId=auditContext.FrontendClientId, SourceIpAddress=auditContext.SourceIpAddress, ClientSourceIpAddress=auditContext.ClientSourceIpAddress, SourceComponent="Api", CorrelationId=auditContext.CorrelationId, ActorAccountId=constraintsAdministratorAccountId, ActorAccountDisplayNameSnapshot=constraintsAdministratorDisplayName, PersonId=entitlement.PersonId, PersonDisplayNameSnapshot=constraintsPersonName, TargetAccountId=entitlement.TargetAccountId, TargetAccountDisplayNameSnapshot=constraintsTargetAccountName, TargetGroupId=entitlement.TargetGroupId, TargetGroupDisplayNameSnapshot=constraintsTargetGroupName, Result="Succeeded", AuthenticationMethod="Windows", PolicyRequirementsSummary=$"before:{before};after:{after}" });
        try { await dbContext.SaveChangesAsync(cancellationToken); return AdministrationUpdateResult.Updated; } catch (DbUpdateConcurrencyException ex) { logger.LogWarning(ex, "SaveChangesAsync hit a concurrent update; returning Conflict."); return AdministrationUpdateResult.Conflict; }
    }

    public async Task<AdministrationUpdateResult> UpdateTargetGroupPolicyAsync(UpdateTargetGroupPolicyRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        byte[] rowVersion;
        try { rowVersion = Convert.FromBase64String(request.RowVersion); } catch (Exception exception) when (exception is FormatException or ArgumentNullException) { return AdministrationUpdateResult.Invalid; }
        var group = await dbContext.TargetGroups.SingleOrDefaultAsync(x => x.TargetGroupId == request.TargetGroupId, cancellationToken);
        if (group is null) return AdministrationUpdateResult.NotFound;
        var policy = await dbContext.GroupPolicies.SingleAsync(x => x.GroupPolicyId == group.GroupPolicyId, cancellationToken);
        if (!policy.RowVersion.SequenceEqual(rowVersion)) return AdministrationUpdateResult.Conflict;
        if (request.RequiresTicket && !await dbContext.TicketReferencePatterns.AnyAsync(item => item.GroupPolicyId == policy.GroupPolicyId && item.IsActive, cancellationToken)) return AdministrationUpdateResult.Invalid;
        var before = $"ttl={policy.MinimumTtlSeconds}-{policy.MaximumTtlSeconds}s;step={policy.AllowedTtlStepSeconds}s;secondFactor={(int)policy.AllowedSecondFactorTypes};ticket={policy.RequiresTicket};approval={policy.RequiresApproval};enabled={group.IsEnabledForRequests}";
        policy.MinimumTtlSeconds=request.MinimumTtlSeconds; policy.MaximumTtlSeconds=request.MaximumTtlSeconds; policy.DefaultTtlSeconds=request.DefaultTtlSeconds; policy.AllowedTtlStepSeconds=request.AllowedTtlStepSeconds; policy.RequiresSecondFactor=request.RequiresSecondFactor; policy.AllowedSecondFactorTypes=(ADDS.PIM.Domain.Security.SecondFactorType)request.AllowedSecondFactorTypes; policy.RequiresTicket=request.RequiresTicket; policy.RequiresApproval=request.RequiresApproval; policy.IsActive=request.PolicyIsActive; policy.ModifiedUtc=timeProvider.GetUtcNow(); group.IsEnabledForRequests=request.IsEnabledForRequests; group.ModifiedUtc=policy.ModifiedUtc;
        var after = $"ttl={policy.MinimumTtlSeconds}-{policy.MaximumTtlSeconds}s;step={policy.AllowedTtlStepSeconds}s;secondFactor={(int)policy.AllowedSecondFactorTypes};ticket={policy.RequiresTicket};approval={policy.RequiresApproval};enabled={group.IsEnabledForRequests}";
        var (policyAdministratorAccountId, policyAdministratorDisplayName) = await ResolveAdministratorAsync(request.Actor, cancellationToken);
        dbContext.AuditEvents.Add(new AuditEventEntity { EventId=Guid.NewGuid(), EventType="TargetGroupPolicyUpdated", OccurredUtc=policy.ModifiedUtc, FrontendClientId=auditContext.FrontendClientId, SourceIpAddress=auditContext.SourceIpAddress, ClientSourceIpAddress=auditContext.ClientSourceIpAddress, SourceComponent="Api", CorrelationId=auditContext.CorrelationId, ActorAccountId=policyAdministratorAccountId, ActorAccountDisplayNameSnapshot=policyAdministratorDisplayName, TargetGroupId=group.TargetGroupId, TargetGroupDisplayNameSnapshot=group.DomainQualifiedName, Result="Succeeded", AuthenticationMethod="Windows", PolicyRequirementsSummary=$"before:{before};after:{after}" });
        try { await dbContext.SaveChangesAsync(cancellationToken); return AdministrationUpdateResult.Updated; } catch (DbUpdateConcurrencyException ex) { logger.LogWarning(ex, "SaveChangesAsync hit a concurrent update; returning Conflict."); return AdministrationUpdateResult.Conflict; }
    }

    public async Task<AdministrationUpdateResult> DeactivateDirectEntitlementAsync(DeactivateDirectEntitlementRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        byte[] rowVersion;
        try { rowVersion = Convert.FromBase64String(request.RowVersion); } catch (Exception exception) when (exception is FormatException or ArgumentNullException) { return AdministrationUpdateResult.Invalid; }
        var entitlement = await dbContext.DirectEntitlements.SingleOrDefaultAsync(x => x.EntitlementId == request.EntitlementId, cancellationToken);
        if (entitlement is null) return AdministrationUpdateResult.NotFound;
        if (!entitlement.RowVersion.SequenceEqual(rowVersion)) return AdministrationUpdateResult.Conflict;
        if (!entitlement.IsActive) return AdministrationUpdateResult.Conflict;
        entitlement.IsActive = false; entitlement.ModifiedUtc = timeProvider.GetUtcNow(); entitlement.ModifiedBy = auditContext.FrontendClientId;
        var (deactivateEntitlementAdministratorAccountId, deactivateEntitlementAdministratorDisplayName) = await ResolveAdministratorAsync(request.Actor, cancellationToken);
        var (deactivatePersonName, deactivateTargetAccountName, deactivateTargetGroupName) = await ResolveEntitlementNamesAsync(entitlement.PersonId, entitlement.TargetAccountId, entitlement.TargetGroupId, cancellationToken);
        dbContext.AuditEvents.Add(new AuditEventEntity { EventId=Guid.NewGuid(), EventType="DirectEntitlementDeactivated", OccurredUtc=entitlement.ModifiedUtc, FrontendClientId=auditContext.FrontendClientId, SourceIpAddress=auditContext.SourceIpAddress, ClientSourceIpAddress=auditContext.ClientSourceIpAddress, SourceComponent="Api", CorrelationId=auditContext.CorrelationId, ActorAccountId=deactivateEntitlementAdministratorAccountId, ActorAccountDisplayNameSnapshot=deactivateEntitlementAdministratorDisplayName, PersonId=entitlement.PersonId, PersonDisplayNameSnapshot=deactivatePersonName, TargetAccountId=entitlement.TargetAccountId, TargetAccountDisplayNameSnapshot=deactivateTargetAccountName, TargetGroupId=entitlement.TargetGroupId, TargetGroupDisplayNameSnapshot=deactivateTargetGroupName, Result="Succeeded", AuthenticationMethod="Windows", PolicyRequirementsSummary="direct-entitlement-deactivated" });
        try { await dbContext.SaveChangesAsync(cancellationToken); return AdministrationUpdateResult.Updated; } catch (DbUpdateConcurrencyException ex) { logger.LogWarning(ex, "SaveChangesAsync hit a concurrent update; returning Conflict."); return AdministrationUpdateResult.Conflict; }
    }

    public async Task<IReadOnlyList<ManagedGroupApprover>> ListGroupApproversAsync(Guid targetGroupId, CancellationToken cancellationToken)
        => await (from approver in dbContext.GroupApprovers.AsNoTracking()
                  join person in dbContext.Persons.AsNoTracking() on approver.PersonId equals person.PersonId
                  where approver.TargetGroupId == targetGroupId
                  orderby approver.IsActive descending, person.DisplayName
                  select new ManagedGroupApprover(approver.GroupApproverId, approver.TargetGroupId, approver.PersonId, person.DisplayName, approver.IsActive, approver.NotifyByEmail, approver.ValidFromUtc, approver.ValidUntilUtc, Convert.ToBase64String(approver.RowVersion))).ToListAsync(cancellationToken);

    public async Task<AdministrationUpdateResult> AddGroupApproverAsync(AddGroupApproverRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        var group = await dbContext.TargetGroups.SingleOrDefaultAsync(x => x.TargetGroupId == request.TargetGroupId, cancellationToken);
        if (group is null) return AdministrationUpdateResult.NotFound;
        var person = await dbContext.Persons.SingleOrDefaultAsync(x => x.PersonId == request.PersonId, cancellationToken);
        if (person is null) return AdministrationUpdateResult.NotFound;
        if (!person.IsActive) return AdministrationUpdateResult.Invalid;
        var now = timeProvider.GetUtcNow();
        if (!await dbContext.PersonAccountLinks.AnyAsync(link => link.PersonId == person.PersonId && link.IsActive && link.MayAuthenticate && link.MayApprove, cancellationToken)) return AdministrationUpdateResult.Invalid;
        if (await dbContext.GroupApprovers.AnyAsync(x => x.TargetGroupId == request.TargetGroupId && x.PersonId == request.PersonId && x.IsActive, cancellationToken)) return AdministrationUpdateResult.Conflict;
        var approver = new GroupApproverEntity { GroupApproverId = Guid.NewGuid(), TargetGroupId = request.TargetGroupId, PersonId = request.PersonId, IsActive = true, ValidFromUtc = now, CreatedBy = auditContext.FrontendClientId, ModifiedBy = auditContext.FrontendClientId, CreatedUtc = now, ModifiedUtc = now };
        dbContext.GroupApprovers.Add(approver);
        var (administratorAccountId, administratorDisplayName) = await ResolveAdministratorAsync(request.Actor, cancellationToken);
        dbContext.AuditEvents.Add(new AuditEventEntity { EventId = Guid.NewGuid(), EventType = "GroupApproverAdded", OccurredUtc = now, FrontendClientId = auditContext.FrontendClientId, SourceIpAddress = auditContext.SourceIpAddress, ClientSourceIpAddress = auditContext.ClientSourceIpAddress, SourceComponent = "Api", CorrelationId = auditContext.CorrelationId, ActorAccountId = administratorAccountId, ActorAccountDisplayNameSnapshot = administratorDisplayName, PersonId = person.PersonId, PersonDisplayNameSnapshot = person.DisplayName, TargetGroupId = group.TargetGroupId, TargetGroupDisplayNameSnapshot = group.DomainQualifiedName, Result = "Succeeded", AuthenticationMethod = "Windows", PolicyRequirementsSummary = "group-approver-added" });
        try { await dbContext.SaveChangesAsync(cancellationToken); return AdministrationUpdateResult.Updated; }
        catch (DbUpdateException ex) { logger.LogError(ex, "AddGroupApproverAsync failed to save changes for TargetGroupId {TargetGroupId} PersonId {PersonId}.", request.TargetGroupId, request.PersonId); return AdministrationUpdateResult.Conflict; }
    }

    public async Task<AdministrationUpdateResult> DeactivateGroupApproverAsync(DeactivateGroupApproverRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        if (!TryRowVersion(request.RowVersion, out var rowVersion)) return AdministrationUpdateResult.Invalid;
        var approver = await dbContext.GroupApprovers.SingleOrDefaultAsync(x => x.GroupApproverId == request.GroupApproverId && x.TargetGroupId == request.TargetGroupId, cancellationToken);
        if (approver is null) return AdministrationUpdateResult.NotFound;
        if (!approver.RowVersion.SequenceEqual(rowVersion) || !approver.IsActive) return AdministrationUpdateResult.Conflict;
        var group = await dbContext.TargetGroups.SingleAsync(x => x.TargetGroupId == approver.TargetGroupId, cancellationToken);
        var person = await dbContext.Persons.SingleAsync(x => x.PersonId == approver.PersonId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        approver.IsActive = false; approver.ModifiedBy = auditContext.FrontendClientId; approver.ModifiedUtc = now;
        var (administratorAccountId, administratorDisplayName) = await ResolveAdministratorAsync(request.Actor, cancellationToken);
        dbContext.AuditEvents.Add(new AuditEventEntity { EventId = Guid.NewGuid(), EventType = "GroupApproverDeactivated", OccurredUtc = now, FrontendClientId = auditContext.FrontendClientId, SourceIpAddress = auditContext.SourceIpAddress, ClientSourceIpAddress = auditContext.ClientSourceIpAddress, SourceComponent = "Api", CorrelationId = auditContext.CorrelationId, ActorAccountId = administratorAccountId, ActorAccountDisplayNameSnapshot = administratorDisplayName, PersonId = person.PersonId, PersonDisplayNameSnapshot = person.DisplayName, TargetGroupId = group.TargetGroupId, TargetGroupDisplayNameSnapshot = group.DomainQualifiedName, Result = "Succeeded", AuthenticationMethod = "Windows", PolicyRequirementsSummary = "group-approver-deactivated" });
        try { await dbContext.SaveChangesAsync(cancellationToken); return AdministrationUpdateResult.Updated; } catch (DbUpdateConcurrencyException ex) { logger.LogWarning(ex, "SaveChangesAsync hit a concurrent update; returning Conflict."); return AdministrationUpdateResult.Conflict; }
    }

    public async Task<AdministrationUpdateResult> UpdateGroupApproverNotificationPreferenceAsync(UpdateGroupApproverNotificationPreferenceRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        if (!TryRowVersion(request.RowVersion, out var rowVersion)) return AdministrationUpdateResult.Invalid;
        var approver = await dbContext.GroupApprovers.SingleOrDefaultAsync(x => x.GroupApproverId == request.GroupApproverId && x.TargetGroupId == request.TargetGroupId, cancellationToken);
        if (approver is null) return AdministrationUpdateResult.NotFound;
        if (!approver.RowVersion.SequenceEqual(rowVersion)) return AdministrationUpdateResult.Conflict;
        var group = await dbContext.TargetGroups.SingleAsync(x => x.TargetGroupId == approver.TargetGroupId, cancellationToken);
        var person = await dbContext.Persons.SingleAsync(x => x.PersonId == approver.PersonId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var before = approver.NotifyByEmail;
        approver.NotifyByEmail = request.NotifyByEmail; approver.ModifiedBy = auditContext.FrontendClientId; approver.ModifiedUtc = now;
        var (administratorAccountId, administratorDisplayName) = await ResolveAdministratorAsync(request.Actor, cancellationToken);
        dbContext.AuditEvents.Add(new AuditEventEntity { EventId = Guid.NewGuid(), EventType = "GroupApproverNotificationPreferenceUpdated", OccurredUtc = now, FrontendClientId = auditContext.FrontendClientId, SourceIpAddress = auditContext.SourceIpAddress, ClientSourceIpAddress = auditContext.ClientSourceIpAddress, SourceComponent = "Api", CorrelationId = auditContext.CorrelationId, ActorAccountId = administratorAccountId, ActorAccountDisplayNameSnapshot = administratorDisplayName, PersonId = person.PersonId, PersonDisplayNameSnapshot = person.DisplayName, TargetGroupId = group.TargetGroupId, TargetGroupDisplayNameSnapshot = group.DomainQualifiedName, Result = "Succeeded", AuthenticationMethod = "Windows", PolicyRequirementsSummary = $"before:notifyByEmail={before};after:notifyByEmail={request.NotifyByEmail}" });
        try { await dbContext.SaveChangesAsync(cancellationToken); return AdministrationUpdateResult.Updated; } catch (DbUpdateConcurrencyException ex) { logger.LogWarning(ex, "SaveChangesAsync hit a concurrent update; returning Conflict."); return AdministrationUpdateResult.Conflict; }
    }

    /// <summary>reports the currently configured TOTP protection certificate's validity window and private-key accessibility without throwing - an expired or inaccessible certificate is exactly what an operator needs to see here, not an exception.</summary>
    public async Task<TotpProtectionCertificateStatus> GetTotpProtectionCertificateStatusAsync(CancellationToken cancellationToken)
    {
        var thumbprint = totpSecretProtectionOptions.Value.CertificateThumbprint;
        using var certificate = TotpProtectionCertificateLoader.TryFind(thumbprint);
        var activeFactorsProtected = await dbContext.TotpFactors.CountAsync(x => x.IsActive, cancellationToken);
        var totalFactorsProtected = await dbContext.TotpFactors.CountAsync(x => x.RevokedUtc == null, cancellationToken);
        if (certificate is null) return new TotpProtectionCertificateStatus(false, thumbprint ?? string.Empty, null, null, false, activeFactorsProtected, totalFactorsProtected);
        var privateKeyAccessible = TotpProtectionCertificateLoader.IsUsableForProtection(certificate);
        return new TotpProtectionCertificateStatus(true, certificate.Thumbprint, certificate.NotBefore, certificate.NotAfter, privateKeyAccessible, activeFactorsProtected, totalFactorsProtected);
    }

    /// <summary>
    /// re-encrypts every persisted TOTP secret (active, pending confirmation, and pending enrollment -
    /// <see cref="Entities.TotpFactorEntity"/> covers all three), plus the mail-settings SMTP password if one is
    /// stored, from the currently configured (outgoing) certificate to
    /// <see cref="RotateTotpProtectionCertificateRequest.IncomingCertificateThumbprint"/>. Runs
    /// inside one database transaction: any failure (unusable certificate, unexpected key mismatch, concurrent
    /// row modification) rolls back everything, so nothing is ever left decryptable by neither key.
    /// </summary>
    public async Task<AdministrationUpdateResult> RotateTotpProtectionCertificateAsync(RotateTotpProtectionCertificateRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        ICertificateSecretProtector outgoingProtector;
        ICertificateSecretProtector incomingProtector;
        try
        {
            outgoingProtector = totpSecretProtectorFactory.CreateForThumbprint(totpSecretProtectionOptions.Value.CertificateThumbprint ?? string.Empty);
            incomingProtector = totpSecretProtectorFactory.CreateForThumbprint(request.IncomingCertificateThumbprint);
        }
        catch (InvalidOperationException)
        {
            return AdministrationUpdateResult.Invalid;
        }

        try
        {
            if (string.Equals(outgoingProtector.KeyId, incomingProtector.KeyId, StringComparison.OrdinalIgnoreCase)) return AdministrationUpdateResult.Invalid;

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var factors = await dbContext.TotpFactors.Where(x => x.RevokedUtc == null).ToListAsync(cancellationToken);
            foreach (var factor in factors)
            {
                if (!string.Equals(factor.ProtectionKeyId, outgoingProtector.KeyId, StringComparison.OrdinalIgnoreCase))
                {
                    // A factor already protected by neither the outgoing nor (yet) the incoming key is an
                    // unexpected state - abort rather than guess, so nothing is left half-migrated.
                    logger.LogError("RotateTotpProtectionCertificateAsync aborted: TotpFactorId {TotpFactorId} is protected by ProtectionKeyId {ProtectionKeyId}, which matches neither the outgoing KeyId {OutgoingKeyId} nor the incoming KeyId {IncomingKeyId}.",
                        factor.TotpFactorId, factor.ProtectionKeyId, outgoingProtector.KeyId, incomingProtector.KeyId);
                    await transaction.RollbackAsync(cancellationToken);
                    return AdministrationUpdateResult.Conflict;
                }

                var secret = outgoingProtector.Unprotect(factor.EncryptedSecret, factor.ProtectionKeyId);
                try
                {
                    factor.EncryptedSecret = incomingProtector.Protect(secret);
                    factor.ProtectionKeyId = incomingProtector.KeyId;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(secret);
                }
            }

            var mailSettings = await dbContext.MailSettings.Where(x => x.EncryptedPassword != null).ToListAsync(cancellationToken);
            foreach (var settings in mailSettings)
            {
                if (!string.Equals(settings.ProtectionKeyId, outgoingProtector.KeyId, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogError("RotateTotpProtectionCertificateAsync aborted: MailSettingsId {MailSettingsId} is protected by ProtectionKeyId {ProtectionKeyId}, which matches neither the outgoing KeyId {OutgoingKeyId} nor the incoming KeyId {IncomingKeyId}.",
                        settings.MailSettingsId, settings.ProtectionKeyId, outgoingProtector.KeyId, incomingProtector.KeyId);
                    await transaction.RollbackAsync(cancellationToken);
                    return AdministrationUpdateResult.Conflict;
                }

                var password = outgoingProtector.Unprotect(settings.EncryptedPassword!, settings.ProtectionKeyId!);
                try
                {
                    settings.EncryptedPassword = incomingProtector.Protect(password);
                    settings.ProtectionKeyId = incomingProtector.KeyId;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(password);
                }
            }

            var now = timeProvider.GetUtcNow();
            var (administratorAccountId, administratorDisplayName) = await ResolveAdministratorAsync(request.Actor, cancellationToken);
            dbContext.AuditEvents.Add(new AuditEventEntity { EventId = Guid.NewGuid(), EventType = "TotpProtectionCertificateRotated", OccurredUtc = now, FrontendClientId = auditContext.FrontendClientId, SourceIpAddress = auditContext.SourceIpAddress, ClientSourceIpAddress = auditContext.ClientSourceIpAddress, SourceComponent = "Api", CorrelationId = auditContext.CorrelationId, ActorAccountId = administratorAccountId, ActorAccountDisplayNameSnapshot = administratorDisplayName, Result = "Succeeded", AuthenticationMethod = "Windows", PolicyRequirementsSummary = $"outgoingKeyId={outgoingProtector.KeyId};incomingKeyId={incomingProtector.KeyId};factorsRotated={factors.Count};mailSettingsRotated={mailSettings.Count}" });
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                logger.LogError(ex, "RotateTotpProtectionCertificateAsync hit a concurrent update while rotating from KeyId {OutgoingKeyId} to {IncomingKeyId}; rolled back.", outgoingProtector.KeyId, incomingProtector.KeyId);
                await transaction.RollbackAsync(cancellationToken);
                return AdministrationUpdateResult.Conflict;
            }

            await transaction.CommitAsync(cancellationToken);
            return AdministrationUpdateResult.Updated;
        }
        finally
        {
            (outgoingProtector as IDisposable)?.Dispose();
            (incomingProtector as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// Admin certificate overview: the three certificates the API can check directly (TOTP protection, API-to-Worker
    /// mTLS client, Web signature - the latter from its already-stored public metadata, no store/network access
    /// needed) are checked live on every call; the Worker's TLS server certificate cannot be read locally when the
    /// Worker runs on a separate host, so it reflects the last actual mTLS handshake observed during a real
    /// membership dispatch (see <see cref="IWorkerServerCertificateObservationStore"/>) rather than a live check.
    /// </summary>
    public async Task<CertificateOverview> GetCertificateOverviewAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var totpStatus = await GetTotpProtectionCertificateStatusAsync(cancellationToken);
        var totpEntry = new MonitoredCertificateStatus("Secrets-Verschlüsselungszertifikat", totpStatus.Found, totpStatus.Thumbprint, totpStatus.NotBefore, totpStatus.NotAfter, true, now, null);

        MonitoredCertificateStatus workerClientEntry;
        using (var workerClientCertificate = TotpProtectionCertificateLoader.TryFind(workerClientOptions.Value.ClientCertificateThumbprint))
        {
            workerClientEntry = workerClientCertificate is null
                ? new MonitoredCertificateStatus("API-zu-Worker mTLS-Client-Zertifikat", false, workerClientOptions.Value.ClientCertificateThumbprint ?? string.Empty, null, null, true, now, null)
                : new MonitoredCertificateStatus("API-zu-Worker mTLS-Client-Zertifikat", true, workerClientCertificate.Thumbprint, workerClientCertificate.NotBefore, workerClientCertificate.NotAfter, true, now, null);
        }

        var activeWebSigningCertificate = await dbContext.WebSigningCertificates.AsNoTracking()
            .Where(x => x.IsActive).OrderBy(x => x.ValidUntilUtc).FirstOrDefaultAsync(cancellationToken);
        var webSigningEntry = activeWebSigningCertificate is null
            ? new MonitoredCertificateStatus("Web-Signaturzertifikat", false, string.Empty, null, null, true, now, null)
            : new MonitoredCertificateStatus("Web-Signaturzertifikat", true, activeWebSigningCertificate.Thumbprint, activeWebSigningCertificate.ValidFromUtc, activeWebSigningCertificate.ValidUntilUtc, true, now, null);

        var workerServerObservation = await workerServerCertificateObservationStore.GetLatestAsync(cancellationToken);
        var workerServerEntry = workerServerObservation is null
            ? new MonitoredCertificateStatus("Worker-Server-Zertifikat (mTLS)", false, string.Empty, null, null, false, null, null)
            : new MonitoredCertificateStatus("Worker-Server-Zertifikat (mTLS)", true, workerServerObservation.Thumbprint, workerServerObservation.NotBeforeUtc, workerServerObservation.NotAfterUtc, false, workerServerObservation.ObservedUtc, workerServerObservation.WasAccepted);

        return new CertificateOverview([totpEntry, workerClientEntry, webSigningEntry, workerServerEntry]);
    }

    public async Task<MailSettingsSnapshot> GetMailSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await dbContext.MailSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return settings is null
            ? new MailSettingsSnapshot(false, true, string.Empty, 0, string.Empty, null, false, SmtpTlsMode.None, string.Empty)
            : new MailSettingsSnapshot(true, settings.IsEnabled, settings.SmtpHost, settings.SmtpPort, settings.SenderAddress, settings.Username, settings.EncryptedPassword is not null, (SmtpTlsMode)settings.TlsMode, Convert.ToBase64String(settings.RowVersion));
    }

    /// <summary>Upserts the single mail-settings row. A new (non-empty) password is protected with the currently
    /// configured certificate, sharing the exact same <see cref="ICertificateSecretProtector"/> instance and
    /// <see cref="ProtectionKeyId"/> namespace as TOTP secrets, so <see cref="RotateTotpProtectionCertificateAsync"/>
    /// can re-encrypt both in one pass.</summary>
    public async Task<AdministrationUpdateResult> UpsertMailSettingsAsync(UpsertMailSettingsRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var existing = await dbContext.MailSettings.SingleOrDefaultAsync(cancellationToken);
        string before;
        MailSettingsEntity settings;
        if (existing is null)
        {
            if (!string.IsNullOrWhiteSpace(request.RowVersion)) return AdministrationUpdateResult.Conflict;
            settings = new MailSettingsEntity { MailSettingsId = Guid.NewGuid(), SmtpHost = request.SmtpHost, SmtpPort = request.SmtpPort, SenderAddress = request.SenderAddress, UpdatedUtc = now, UpdatedBy = string.Empty };
            dbContext.MailSettings.Add(settings);
            before = "new";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.RowVersion)) return AdministrationUpdateResult.Invalid;
            byte[] rowVersion; try { rowVersion = Convert.FromBase64String(request.RowVersion); } catch (FormatException) { return AdministrationUpdateResult.Invalid; }
            if (!existing.RowVersion.SequenceEqual(rowVersion)) return AdministrationUpdateResult.Conflict;
            settings = existing;
            before = $"host={settings.SmtpHost}:{settings.SmtpPort};sender={settings.SenderAddress};username={settings.Username};tlsMode={settings.TlsMode};hasPassword={settings.EncryptedPassword is not null};isEnabled={settings.IsEnabled}";
        }

        settings.IsEnabled = request.IsEnabled;
        settings.SmtpHost = request.SmtpHost;
        settings.SmtpPort = request.SmtpPort;
        settings.SenderAddress = request.SenderAddress;
        settings.Username = request.Username;
        settings.TlsMode = (int)request.TlsMode;
        settings.UpdatedUtc = now;

        if (request.ClearPassword)
        {
            settings.EncryptedPassword = null;
            settings.ProtectionKeyId = null;
        }
        else if (!string.IsNullOrEmpty(request.NewPassword))
        {
            var plaintext = System.Text.Encoding.UTF8.GetBytes(request.NewPassword);
            try
            {
                settings.EncryptedPassword = certificateSecretProtector.Protect(plaintext);
                settings.ProtectionKeyId = certificateSecretProtector.KeyId;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        var (administratorAccountId, administratorDisplayName) = await ResolveAdministratorAsync(request.Actor, cancellationToken);
        settings.UpdatedBy = administratorDisplayName ?? administratorAccountId?.ToString() ?? "unknown";
        dbContext.AuditEvents.Add(new AuditEventEntity { EventId = Guid.NewGuid(), EventType = "MailSettingsUpdated", OccurredUtc = now, FrontendClientId = auditContext.FrontendClientId, SourceIpAddress = auditContext.SourceIpAddress, ClientSourceIpAddress = auditContext.ClientSourceIpAddress, SourceComponent = "Api", CorrelationId = auditContext.CorrelationId, ActorAccountId = administratorAccountId, ActorAccountDisplayNameSnapshot = administratorDisplayName, Result = "Succeeded", AuthenticationMethod = "Windows", PolicyRequirementsSummary = $"before:{before};after:host={request.SmtpHost}:{request.SmtpPort};sender={request.SenderAddress};username={request.Username};tlsMode={request.TlsMode};hasPassword={settings.EncryptedPassword is not null};isEnabled={request.IsEnabled}" });
        try { await dbContext.SaveChangesAsync(cancellationToken); return AdministrationUpdateResult.Updated; } catch (DbUpdateConcurrencyException ex) { logger.LogWarning(ex, "SaveChangesAsync hit a concurrent update while saving mail settings; returning Conflict."); return AdministrationUpdateResult.Conflict; }
    }

    /// <summary>Sends a real test email using the currently *persisted* settings (not the in-flight edit form),
    /// so the test genuinely exercises what would be used for real notifications. Always audited, whether the
    /// send succeeded or failed.</summary>
    public async Task<MailSettingsTestResult> TestMailSettingsAsync(TestMailSettingsRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        var resolved = await resolvedMailSettingsProvider.GetAsync(cancellationToken);
        if (resolved.Status == ResolvedMailSettingsStatus.NotConfigured) return new MailSettingsTestResult(false, "Es sind noch keine Mail-Einstellungen gespeichert.");
        if (resolved.Status == ResolvedMailSettingsStatus.ProtectionKeyMismatch) return new MailSettingsTestResult(false, "Das gespeicherte Passwort ist mit einem anderen Zertifikat geschützt als dem aktuell konfigurierten.");
        if (resolved.Status == ResolvedMailSettingsStatus.Disabled) return new MailSettingsTestResult(false, "Der Mail-Versand ist deaktiviert. Bitte zuerst aktivieren, um eine Test-E-Mail zu senden.");
        var settings = resolved.Settings!;

        var outcome = await mailSender.SendAsync(new MailMessageSendRequest(settings.SmtpHost, settings.SmtpPort, settings.SenderAddress, settings.TlsMode, settings.Username, settings.Password,
            [request.RecipientAddress], "ADDS PIM - Test-E-Mail", "Dies ist eine Test-E-Mail der ADDS-PIM Mail-Einstellungen."), cancellationToken);

        var now = timeProvider.GetUtcNow();
        var (administratorAccountId, administratorDisplayName) = await ResolveAdministratorAsync(request.Actor, cancellationToken);
        dbContext.AuditEvents.Add(new AuditEventEntity { EventId = Guid.NewGuid(), EventType = "MailSettingsTestExecuted", OccurredUtc = now, FrontendClientId = auditContext.FrontendClientId, SourceIpAddress = auditContext.SourceIpAddress, ClientSourceIpAddress = auditContext.ClientSourceIpAddress, SourceComponent = "Api", CorrelationId = auditContext.CorrelationId, ActorAccountId = administratorAccountId, ActorAccountDisplayNameSnapshot = administratorDisplayName, Result = outcome.Succeeded ? "Succeeded" : "Failed", AuthenticationMethod = "Windows", PolicyRequirementsSummary = $"recipient={request.RecipientAddress}" });
        await dbContext.SaveChangesAsync(cancellationToken);

        return new MailSettingsTestResult(outcome.Succeeded, outcome.ErrorMessage);
    }

    /// <summary>Counts only - never message bodies or recipient addresses - for the admin UI's queue-status
    /// widget. <see cref="MailNotificationOutboxStatus.FailedCount"/> counts pending rows with at least one
    /// recorded failed delivery attempt; a pending row with zero attempts yet is not "failed", just not
    /// dispatched yet.</summary>
    public async Task<MailNotificationOutboxStatus> GetMailNotificationOutboxStatusAsync(CancellationToken cancellationToken)
    {
        var pending = dbContext.MailNotificationOutbox.AsNoTracking().Where(x => x.DeliveredUtc == null);
        var pendingCount = await pending.CountAsync(cancellationToken);
        var failedCount = await pending.CountAsync(x => x.LastFailureMessage != null, cancellationToken);
        return new MailNotificationOutboxStatus(pendingCount, failedCount);
    }

    /// <summary>Bulk-deletes every currently pending (undelivered) outbox row in one statement, then records a
    /// single audit event carrying the number purged - not one audit event per row, since this is one
    /// administrative action, not N.</summary>
    public async Task<AdministrationUpdateResult> PurgeMailNotificationOutboxAsync(PurgeMailNotificationOutboxRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var purgedCount = await dbContext.MailNotificationOutbox.Where(x => x.DeliveredUtc == null).ExecuteDeleteAsync(cancellationToken);
        var (administratorAccountId, administratorDisplayName) = await ResolveAdministratorAsync(request.Actor, cancellationToken);
        dbContext.AuditEvents.Add(new AuditEventEntity { EventId = Guid.NewGuid(), EventType = "MailNotificationOutboxPurged", OccurredUtc = now, FrontendClientId = auditContext.FrontendClientId, SourceIpAddress = auditContext.SourceIpAddress, ClientSourceIpAddress = auditContext.ClientSourceIpAddress, SourceComponent = "Api", CorrelationId = auditContext.CorrelationId, ActorAccountId = administratorAccountId, ActorAccountDisplayNameSnapshot = administratorDisplayName, Result = "Succeeded", AuthenticationMethod = "Windows", PolicyRequirementsSummary = $"purgedCount={purgedCount}" });
        await dbContext.SaveChangesAsync(cancellationToken);
        return AdministrationUpdateResult.Updated;
    }

    public async Task<RequesterNotificationSettingsSnapshot> GetRequesterNotificationSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await dbContext.RequesterNotificationSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return settings is null
            ? new RequesterNotificationSettingsSnapshot(false, null, null, string.Empty)
            : new RequesterNotificationSettingsSnapshot(true, settings.CcAddress, settings.BccAddress, Convert.ToBase64String(settings.RowVersion));
    }

    public async Task<AdministrationUpdateResult> UpsertRequesterNotificationSettingsAsync(UpsertRequesterNotificationSettingsRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var existing = await dbContext.RequesterNotificationSettings.SingleOrDefaultAsync(cancellationToken);
        string before;
        RequesterNotificationSettingsEntity settings;
        if (existing is null)
        {
            if (!string.IsNullOrWhiteSpace(request.RowVersion)) return AdministrationUpdateResult.Conflict;
            settings = new RequesterNotificationSettingsEntity { RequesterNotificationSettingsId = Guid.NewGuid(), UpdatedUtc = now, UpdatedBy = string.Empty };
            dbContext.RequesterNotificationSettings.Add(settings);
            before = "new";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.RowVersion)) return AdministrationUpdateResult.Invalid;
            byte[] rowVersion; try { rowVersion = Convert.FromBase64String(request.RowVersion); } catch (FormatException) { return AdministrationUpdateResult.Invalid; }
            if (!existing.RowVersion.SequenceEqual(rowVersion)) return AdministrationUpdateResult.Conflict;
            settings = existing;
            before = $"cc={settings.CcAddress};bcc={settings.BccAddress}";
        }

        settings.CcAddress = request.CcAddress;
        settings.BccAddress = request.BccAddress;
        settings.UpdatedUtc = now;
        var (administratorAccountId, administratorDisplayName) = await ResolveAdministratorAsync(request.Actor, cancellationToken);
        settings.UpdatedBy = administratorDisplayName ?? administratorAccountId?.ToString() ?? "unknown";
        dbContext.AuditEvents.Add(new AuditEventEntity { EventId = Guid.NewGuid(), EventType = "RequesterNotificationSettingsUpdated", OccurredUtc = now, FrontendClientId = auditContext.FrontendClientId, SourceIpAddress = auditContext.SourceIpAddress, ClientSourceIpAddress = auditContext.ClientSourceIpAddress, SourceComponent = "Api", CorrelationId = auditContext.CorrelationId, ActorAccountId = administratorAccountId, ActorAccountDisplayNameSnapshot = administratorDisplayName, Result = "Succeeded", AuthenticationMethod = "Windows", PolicyRequirementsSummary = $"before:{before};after:cc={request.CcAddress};bcc={request.BccAddress}" });
        try { await dbContext.SaveChangesAsync(cancellationToken); return AdministrationUpdateResult.Updated; } catch (DbUpdateConcurrencyException ex) { logger.LogWarning(ex, "SaveChangesAsync hit a concurrent update while saving requester notification settings; returning Conflict."); return AdministrationUpdateResult.Conflict; }
    }

    public async Task<AdministrationUpdateResult> SetPersonNotificationEmailOverrideAsync(SetPersonNotificationEmailOverrideRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        byte[] rowVersion; try { rowVersion = Convert.FromBase64String(request.RowVersion); } catch (FormatException) { return AdministrationUpdateResult.Invalid; }
        var person = await dbContext.Persons.SingleOrDefaultAsync(x => x.PersonId == request.PersonId, cancellationToken);
        if (person is null) return AdministrationUpdateResult.NotFound;
        if (!person.RowVersion.SequenceEqual(rowVersion)) return AdministrationUpdateResult.Conflict;

        var now = timeProvider.GetUtcNow();
        var before = person.NotificationEmailOverride;
        person.NotificationEmailOverride = request.NotificationEmailOverride;
        person.ModifiedUtc = now;
        var (administratorAccountId, administratorDisplayName) = await ResolveAdministratorAsync(request.Actor, cancellationToken);
        dbContext.AuditEvents.Add(new AuditEventEntity { EventId = Guid.NewGuid(), EventType = "PersonNotificationEmailOverrideUpdated", OccurredUtc = now, FrontendClientId = auditContext.FrontendClientId, SourceIpAddress = auditContext.SourceIpAddress, ClientSourceIpAddress = auditContext.ClientSourceIpAddress, SourceComponent = "Api", CorrelationId = auditContext.CorrelationId, ActorAccountId = administratorAccountId, ActorAccountDisplayNameSnapshot = administratorDisplayName, PersonId = person.PersonId, PersonDisplayNameSnapshot = person.DisplayName, Result = "Succeeded", AuthenticationMethod = "Windows", PolicyRequirementsSummary = $"before:notificationEmailOverride={before};after:notificationEmailOverride={request.NotificationEmailOverride}" });
        try { await dbContext.SaveChangesAsync(cancellationToken); return AdministrationUpdateResult.Updated; } catch (DbUpdateConcurrencyException ex) { logger.LogWarning(ex, "SaveChangesAsync hit a concurrent update; returning Conflict."); return AdministrationUpdateResult.Conflict; }
    }
}

using ADDS.PIM.Application.MembershipRequests;
using ADDS.PIM.Application.Notifications;
using ADDS.PIM.Contracts.Administration.V1;
using ADDS.PIM.Contracts.Notifications;
using ADDS.PIM.Domain.MembershipRequests;
using ADDS.PIM.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ADDS.PIM.Infrastructure.Persistence;

public sealed class EfMembershipRequestStateStore(PimDbContext dbContext, TimeProvider timeProvider) : IMembershipRequestStateStore
{
    private static readonly IReadOnlyDictionary<MembershipRequestStatus, string> OutcomeTexts = new Dictionary<MembershipRequestStatus, string>
    {
        [MembershipRequestStatus.Succeeded] = "Successful",
        [MembershipRequestStatus.Failed] = "Not successful",
        [MembershipRequestStatus.Rejected] = "Not successful",
        [MembershipRequestStatus.Expired] = "Not successful",
        [MembershipRequestStatus.Cancelled] = "Not successful",
    };

    public async Task TransitionAsync(Guid requestId, MembershipRequestStatus expectedStatus, MembershipRequestStatus nextStatus, MembershipRequestTransitionAuditContext auditContext, string reason, CancellationToken cancellationToken)
    {
        if (!MembershipRequestStateMachine.CanTransition(expectedStatus, nextStatus))
            throw new InvalidOperationException($"Illegal membership-request transition: {expectedStatus} to {nextStatus}.");

        var request = await dbContext.MembershipRequests.SingleOrDefaultAsync(x => x.RequestId == requestId, cancellationToken)
            ?? throw new InvalidOperationException("Membership request does not exist.");
        if (request.Status != expectedStatus)
            throw new InvalidOperationException("Membership request state changed concurrently.");

        var occurredUtc = timeProvider.GetUtcNow();
        request.Status = nextStatus;
        // An administrator-triggered transition (e.g. orphaned-request cleanup) attributes the audit trail to
        // the administrator who acted, not the original requester whose request is being terminated. Detected
        // by presence, not just AdministratorAccountId, since an administrator without a local DirectoryAccount
        // row still resolves to a display name only (EfAdministrationDataStore.ResolveAdministratorAsync).
        var isAdministratorTriggered = auditContext.AdministratorAccountId is not null || auditContext.AdministratorDisplayName is not null;
        Guid? actorAccountId = isAdministratorTriggered ? auditContext.AdministratorAccountId : request.ActorAccountId;
        var actorDisplayNameSnapshot = isAdministratorTriggered ? auditContext.AdministratorDisplayName : request.ActorAccountDisplayNameSnapshot;
        dbContext.MembershipRequestStatusHistory.Add(new MembershipRequestStatusHistoryEntity
        {
            EntryId = Guid.NewGuid(), RequestId = requestId, PreviousStatus = expectedStatus, NewStatus = nextStatus,
            OccurredUtc = occurredUtc, ActorId = actorAccountId?.ToString("D") ?? actorDisplayNameSnapshot ?? "Administrator", SourceComponent = "Api", Reason = reason
        });
        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            EventId = Guid.NewGuid(), EventType = $"MembershipRequest{nextStatus}", OccurredUtc = occurredUtc,
            PersonId = request.PersonId, ActorAccountId = actorAccountId, TargetAccountId = request.TargetAccountId,
            PersonDisplayNameSnapshot = request.PersonDisplayNameSnapshot, ActorAccountDisplayNameSnapshot = actorDisplayNameSnapshot,
            TargetAccountDisplayNameSnapshot = request.TargetAccountDisplayNameSnapshot, TargetGroupDisplayNameSnapshot = request.TargetGroupDisplayNameSnapshot,
            FrontendClientId = auditContext.FrontendClientId, SourceIpAddress = auditContext.SourceIpAddress, ClientSourceIpAddress = auditContext.ClientSourceIpAddress, SourceComponent = "Api",
            CorrelationId = auditContext.CorrelationId, RequestId = request.RequestId, TargetGroupId = request.TargetGroupId,
            RequestedTtlSeconds = request.RequestedTtlSeconds, Result = nextStatus is MembershipRequestStatus.Failed or MembershipRequestStatus.Rejected ? "Failed" : "Succeeded",
            FailureCategory = auditContext.FailureCategory, AuthenticationMethod = auditContext.AuthenticationMethod, PolicyRequirementsSummary = auditContext.PolicyRequirementsSummary
        });

        if (MembershipRequestStateMachine.IsTerminal(nextStatus))
        {
            await EnqueueOutcomeNotificationAsync(request, nextStatus, reason, occurredUtc, cancellationToken);
            await EnqueueRequesterNotificationAsync(request, nextStatus, reason, occurredUtc, cancellationToken);
        }

        if (nextStatus == MembershipRequestStatus.AwaitingApproval)
        {
            await EnqueueApprovalPendingNotificationAsync(request, reason, occurredUtc, cancellationToken);
        }

        if (auditContext.DecidingApproverPersonId is Guid decidingApproverPersonId)
        {
            await EnqueueApprovalDecisionNotificationAsync(request, nextStatus, reason, occurredUtc, decidingApproverPersonId, auditContext.DecidingApproverDisplayName, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Enqueues a rendered notification email for every active <see cref="GroupNotificationRecipientEntity"/>
    /// of the request's target group, using the single "membership-request-outcome" template - in the same
    /// transaction as the status transition itself. Silently does nothing if the group has no active recipients
    /// configured (feature is opt-in per group, no separate enable flag) or if the template has never been saved
    /// (feature not yet configured system-wide).</summary>
    private async Task EnqueueOutcomeNotificationAsync(MembershipRequestEntity request, MembershipRequestStatus nextStatus, string reason, DateTimeOffset occurredUtc, CancellationToken cancellationToken)
    {
        var recipients = await dbContext.GroupNotificationRecipients
            .Where(x => x.TargetGroupId == request.TargetGroupId && x.IsActive)
            .Select(x => new { x.EmailAddress, x.RecipientType })
            .ToListAsync(cancellationToken);
        if (recipients.Count == 0) return;

        string AddressesFor(MailRecipientType type) => string.Join(';', recipients.Where(x => x.RecipientType == (int)type).Select(x => x.EmailAddress));

        var template = await dbContext.NotificationTemplates.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TemplateKey == NotificationTemplateKeys.MembershipRequestOutcome, cancellationToken);
        if (template is null) return;

        var placeholders = BuildPlaceholders(request, nextStatus, reason, occurredUtc);

        dbContext.MailNotificationOutbox.Add(new MailNotificationOutboxEntity
        {
            OutboxId = Guid.NewGuid(),
            RequestId = request.RequestId,
            ToAddresses = AddressesFor(MailRecipientType.To),
            CcAddresses = AddressesFor(MailRecipientType.Cc),
            BccAddresses = AddressesFor(MailRecipientType.Bcc),
            Subject = NotificationTemplateRenderer.Render(template.Subject, placeholders),
            Body = NotificationTemplateRenderer.Render(template.Body, placeholders),
            CreatedUtc = occurredUtc,
        });
    }

    /// <summary>Enqueues a rendered notification email to the requester themselves (distinct from the
    /// per-group <see cref="EnqueueOutcomeNotificationAsync"/> recipients), using the single
    /// "requester-outcome-notification" template plus a global Cc/Bcc policy
    /// (<see cref="RequesterNotificationSettingsEntity"/>). The requester's email is
    /// <see cref="PersonEntity.NotificationEmailOverride"/> when set (an admin-recorded preference to receive
    /// these elsewhere, e.g. not at the AD/Exchange mailbox), falling back to the AD <c>EmailAddress</c> of
    /// their currently active <c>MayAuthenticate</c> account otherwise. Silently does nothing if no email can be
    /// resolved at all, or if the template has never been saved - this never affects the group-recipients path,
    /// which already ran independently.</summary>
    private async Task EnqueueRequesterNotificationAsync(MembershipRequestEntity request, MembershipRequestStatus nextStatus, string reason, DateTimeOffset occurredUtc, CancellationToken cancellationToken)
    {
        var requesterEmail = await ResolvePersonEmailAsync(request.PersonId, cancellationToken);
        if (string.IsNullOrWhiteSpace(requesterEmail)) return;

        var template = await dbContext.NotificationTemplates.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TemplateKey == NotificationTemplateKeys.RequesterOutcomeNotification, cancellationToken);
        if (template is null) return;

        var settings = await dbContext.RequesterNotificationSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var placeholders = BuildPlaceholders(request, nextStatus, reason, occurredUtc);

        dbContext.MailNotificationOutbox.Add(new MailNotificationOutboxEntity
        {
            OutboxId = Guid.NewGuid(),
            RequestId = request.RequestId,
            ToAddresses = requesterEmail,
            CcAddresses = settings?.CcAddress ?? string.Empty,
            BccAddresses = settings?.BccAddress ?? string.Empty,
            Subject = NotificationTemplateRenderer.Render(template.Subject, placeholders),
            Body = NotificationTemplateRenderer.Render(template.Body, placeholders),
            CreatedUtc = occurredUtc,
        });
    }

    /// <summary>Enqueues a rendered notification email to every active <see cref="GroupApproverEntity"/> of the
    /// request's target group that opted in (<see cref="GroupApproverEntity.NotifyByEmail"/>), using the single
    /// "approval-pending-notification" template - fired once the request enters <see
    /// cref="MembershipRequestStatus.AwaitingApproval"/>. Silently does nothing if the group has no opted-in
    /// approvers with a resolvable email, or if the template has never been saved.</summary>
    private async Task EnqueueApprovalPendingNotificationAsync(MembershipRequestEntity request, string reason, DateTimeOffset occurredUtc, CancellationToken cancellationToken)
    {
        var approverEmails = await ResolveApproverEmailsAsync(request.TargetGroupId, excludePersonId: null, cancellationToken);
        if (approverEmails.Count == 0) return;

        var template = await dbContext.NotificationTemplates.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TemplateKey == NotificationTemplateKeys.ApprovalPendingNotification, cancellationToken);
        if (template is null) return;

        var placeholders = BuildPlaceholders(request, MembershipRequestStatus.AwaitingApproval, reason, occurredUtc);

        dbContext.MailNotificationOutbox.Add(new MailNotificationOutboxEntity
        {
            OutboxId = Guid.NewGuid(),
            RequestId = request.RequestId,
            ToAddresses = string.Join(';', approverEmails),
            CcAddresses = string.Empty,
            BccAddresses = string.Empty,
            Subject = NotificationTemplateRenderer.Render(template.Subject, placeholders),
            Body = NotificationTemplateRenderer.Render(template.Body, placeholders),
            CreatedUtc = occurredUtc,
        });
    }

    /// <summary>Enqueues a rendered notification email to every OTHER opted-in active approver of the request's
    /// target group - i.e. everyone <see cref="EnqueueApprovalPendingNotificationAsync"/> notified except
    /// <paramref name="decidingApproverPersonId"/> - using the single "approval-decision-notification" template,
    /// whenever an explicit approve/reject decision was made (see <see
    /// cref="MembershipRequestTransitionAuditContext.DecidingApproverPersonId"/>; never fires for
    /// administrator-triggered transitions like orphaned-request cleanup, which leave that field null).</summary>
    private async Task EnqueueApprovalDecisionNotificationAsync(MembershipRequestEntity request, MembershipRequestStatus nextStatus, string reason, DateTimeOffset occurredUtc, Guid decidingApproverPersonId, string? decidingApproverDisplayName, CancellationToken cancellationToken)
    {
        var approverEmails = await ResolveApproverEmailsAsync(request.TargetGroupId, decidingApproverPersonId, cancellationToken);
        if (approverEmails.Count == 0) return;

        var template = await dbContext.NotificationTemplates.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TemplateKey == NotificationTemplateKeys.ApprovalDecisionNotification, cancellationToken);
        if (template is null) return;

        var placeholders = BuildPlaceholders(request, nextStatus, reason, occurredUtc);
        placeholders["DecidingApproverDisplayName"] = decidingApproverDisplayName ?? string.Empty;

        dbContext.MailNotificationOutbox.Add(new MailNotificationOutboxEntity
        {
            OutboxId = Guid.NewGuid(),
            RequestId = request.RequestId,
            ToAddresses = string.Join(';', approverEmails),
            CcAddresses = string.Empty,
            BccAddresses = string.Empty,
            Subject = NotificationTemplateRenderer.Render(template.Subject, placeholders),
            Body = NotificationTemplateRenderer.Render(template.Body, placeholders),
            CreatedUtc = occurredUtc,
        });
    }

    /// <summary>Shared email-resolution logic (override-if-set, else the AD email of the <c>MayAuthenticate</c>
    /// account) used for a single person - see <see cref="EnqueueRequesterNotificationAsync"/>.</summary>
    private async Task<string?> ResolvePersonEmailAsync(Guid personId, CancellationToken cancellationToken)
    {
        var resolved = await (from person in dbContext.Persons
                               where person.PersonId == personId
                               select new
                               {
                                   AdEmail = (from link in dbContext.PersonAccountLinks
                                              join account in dbContext.DirectoryAccounts on link.AccountId equals account.AccountId
                                              where link.PersonId == person.PersonId && link.IsActive && link.MayAuthenticate
                                              select account.EmailAddress).FirstOrDefault(),
                                   person.NotificationEmailOverride,
                               }).SingleOrDefaultAsync(cancellationToken);
        return resolved is null ? null : (string.IsNullOrWhiteSpace(resolved.NotificationEmailOverride) ? resolved.AdEmail : resolved.NotificationEmailOverride);
    }

    /// <summary>Same override-if-set/AD-fallback resolution as <see cref="ResolvePersonEmailAsync"/>, applied in
    /// one query to every active, opted-in (<see cref="GroupApproverEntity.NotifyByEmail"/>) approver of
    /// <paramref name="targetGroupId"/>, optionally excluding one person (the approver who just decided).</summary>
    private async Task<IReadOnlyList<string>> ResolveApproverEmailsAsync(Guid targetGroupId, Guid? excludePersonId, CancellationToken cancellationToken)
    {
        var approvers = await (from approver in dbContext.GroupApprovers
                                where approver.TargetGroupId == targetGroupId && approver.IsActive && approver.NotifyByEmail
                                      && (excludePersonId == null || approver.PersonId != excludePersonId)
                                select new
                                {
                                    AdEmail = (from link in dbContext.PersonAccountLinks
                                               join account in dbContext.DirectoryAccounts on link.AccountId equals account.AccountId
                                               where link.PersonId == approver.PersonId && link.IsActive && link.MayAuthenticate
                                               select account.EmailAddress).FirstOrDefault(),
                                    OverrideEmail = dbContext.Persons.Where(p => p.PersonId == approver.PersonId).Select(p => p.NotificationEmailOverride).FirstOrDefault(),
                                }).ToListAsync(cancellationToken);

        return approvers
            .Select(a => string.IsNullOrWhiteSpace(a.OverrideEmail) ? a.AdEmail : a.OverrideEmail)
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Select(email => email!)
            .ToArray();
    }

    private static Dictionary<string, string> BuildPlaceholders(MembershipRequestEntity request, MembershipRequestStatus nextStatus, string reason, DateTimeOffset occurredUtc) => new()
    {
        ["PersonDisplayName"] = request.PersonDisplayNameSnapshot,
        ["ActorAccountDisplayName"] = request.ActorAccountDisplayNameSnapshot,
        ["TargetAccountDisplayName"] = request.TargetAccountDisplayNameSnapshot,
        ["TargetGroupDisplayName"] = request.TargetGroupDisplayNameSnapshot,
        ["RequestedTtlHours"] = (request.RequestedTtlSeconds / 3600m).ToString("0.##"),
        ["TicketReference"] = request.TicketReference ?? string.Empty,
        ["RequestReason"] = request.Reason,
        ["StatusText"] = nextStatus.ToString(),
        ["OutcomeText"] = OutcomeTexts.GetValueOrDefault(nextStatus, nextStatus.ToString()),
        ["OutcomeReason"] = reason,
        ["OccurredUtc"] = occurredUtc.ToString("u"),
        ["RequestId"] = request.RequestId.ToString("D"),
    };
}

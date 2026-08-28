using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Application.Security;
using ADDS.PIM.Contracts.Administration.V1;
using ADDS.PIM.Web.Operator;

namespace ADDS.PIM.Web.Administration;

public interface IAdministrationClient
{
    Task<IReadOnlyList<ManagedTargetGroup>> GetTargetGroupsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ManagedTicketReferencePattern>> GetTicketReferencePatternsAsync(CancellationToken cancellationToken);
    Task UpsertTicketReferencePatternAsync(ManagedTicketReferencePattern pattern, CancellationToken cancellationToken);
    Task DeleteTicketReferencePatternAsync(ManagedTicketReferencePattern pattern, CancellationToken cancellationToken);
    Task<IReadOnlyList<ManagedGroupNotificationRecipient>> GetGroupNotificationRecipientsAsync(CancellationToken cancellationToken);
    Task UpsertGroupNotificationRecipientAsync(ManagedGroupNotificationRecipient recipient, CancellationToken cancellationToken);
    Task DeleteGroupNotificationRecipientAsync(ManagedGroupNotificationRecipient recipient, CancellationToken cancellationToken);
    Task<NotificationTemplateSnapshot> GetNotificationTemplateAsync(string templateKey, CancellationToken cancellationToken);
    Task UpsertNotificationTemplateAsync(string templateKey, string subject, string body, string? rowVersion, CancellationToken cancellationToken);
    Task<DirectoryReconciliationOverview> GetDirectoryReconciliationAsync(CancellationToken cancellationToken);
    Task<AuditLogPage> GetAuditLogAsync(int pageNumber, int pageSize, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, string? eventType, string? result, string? actorAccount, CancellationToken cancellationToken);
    Task<TechnicalErrorLogPage> GetTechnicalErrorsAsync(int pageNumber, int pageSize, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, Guid? requestId, Guid? correlationId, CancellationToken cancellationToken);
    Task StartDirectoryReconciliationAsync(CancellationToken cancellationToken);
    Task DeactivateDirectoryReconciliationFindingAsync(ManagedDirectoryReconciliationFinding finding, CancellationToken cancellationToken);
    Task<IdentityPurgeScopePreview> GetIdentityPurgeScopePreviewAsync(string initiatorType, Guid initiatorId, CancellationToken cancellationToken);
    Task<IReadOnlyList<IdentityPurgeCandidate>> GetIdentityPurgeCandidatesAsync(CancellationToken cancellationToken);
    Task ExecuteIdentityPurgeAsync(string initiatorType, Guid initiatorId, string confirmation, CancellationToken cancellationToken);
    Task<IReadOnlyList<ManagedOrphanedSecondFactorRequest>> GetOrphanedSecondFactorRequestsAsync(CancellationToken cancellationToken);
    Task ExpireOrphanedSecondFactorRequestAsync(Guid requestId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ManagedPerson>> GetPersonsAsync(CancellationToken cancellationToken);
    Task<ManagedPersonDetail> GetPersonDetailAsync(Guid personId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DirectoryUserSearchResult>> SearchDirectoryUsersAsync(string searchTerm, CancellationToken cancellationToken);
    Task CreatePersonFromDirectoryAccountAsync(Guid objectGuid, CancellationToken cancellationToken);
    Task SetPersonActiveAsync(ManagedPerson person, bool active, CancellationToken cancellationToken);
    Task<IReadOnlyList<DirectoryUserSearchResult>> SearchDirectoryAccountsAsync(string searchTerm, CancellationToken cancellationToken);
    Task CreatePersonAccountLinkAsync(Guid personId, Guid objectGuid, bool mayAuthenticate, bool mayReceivePrivileges, bool mayApprove, CancellationToken cancellationToken);
    Task UpdatePersonAccountLinkPurposesAsync(Guid personId, ManagedPersonAccountLink link, bool mayAuthenticate, bool mayReceivePrivileges, bool mayApprove, CancellationToken cancellationToken);
    Task SetPersonAccountLinkActiveAsync(Guid personId, ManagedPersonAccountLink link, bool active, CancellationToken cancellationToken);
    Task<IReadOnlyList<DirectoryGroupSearchResult>> SearchDirectoryGroupsAsync(string searchTerm, CancellationToken cancellationToken);
    Task<IReadOnlyList<ManagedTotpFactor>> ListTotpFactorsAsync(Guid personId, CancellationToken cancellationToken);
    Task RevokeTotpFactorAsync(Guid personId, ManagedTotpFactor factor, CancellationToken cancellationToken);
    Task<IReadOnlyList<ManagedFido2Credential>> ListFido2CredentialsAsync(Guid personId, CancellationToken cancellationToken);
    Task RevokeFido2CredentialAsync(Guid personId, ManagedFido2Credential credential, CancellationToken cancellationToken);
    Task<IReadOnlyList<ManagedDirectEntitlement>> GetEntitlementsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<EntitlementSubject>> GetEligibleEntitlementSubjectsAsync(CancellationToken cancellationToken);
    Task CreateDirectEntitlementAsync(Guid personId, Guid targetAccountId, Guid targetGroupId, DateTimeOffset validFromUtc, DateTimeOffset? validUntilUtc, CancellationToken cancellationToken);
    Task UpdateTargetGroupPolicyAsync(ManagedTargetGroup group, CancellationToken cancellationToken);
    Task CreateTargetGroupAsync(Guid objectGuid, long minimumTtlSeconds, long maximumTtlSeconds, long defaultTtlSeconds, long allowedTtlStepSeconds, bool requiresSecondFactor, int allowedSecondFactorTypes, bool requiresTicket, bool requiresApproval, CancellationToken cancellationToken);
    Task DeactivateTargetGroupAsync(ManagedTargetGroup group, CancellationToken cancellationToken);
    Task ReactivateTargetGroupAsync(ManagedTargetGroup group, CancellationToken cancellationToken);
    Task DeactivateDirectEntitlementAsync(ManagedDirectEntitlement entitlement, CancellationToken cancellationToken);
    Task UpdateDirectEntitlementValidityAsync(ManagedDirectEntitlement entitlement, DateTimeOffset? validUntilUtc, CancellationToken cancellationToken);
    Task UpdateDirectEntitlementConstraintsAsync(ManagedDirectEntitlement entitlement, long? minimumTtlSeconds, long? maximumTtlSeconds, long? allowedTtlStepSeconds, bool? requiresSecondFactor, bool? requiresTicket, bool? requiresApproval, CancellationToken cancellationToken);
    Task<IReadOnlyList<ManagedGroupApprover>> GetGroupApproversAsync(Guid targetGroupId, CancellationToken cancellationToken);
    Task AddGroupApproverAsync(Guid targetGroupId, Guid personId, CancellationToken cancellationToken);
    Task DeactivateGroupApproverAsync(Guid targetGroupId, ManagedGroupApprover approver, CancellationToken cancellationToken);
    Task UpdateGroupApproverNotificationPreferenceAsync(Guid targetGroupId, ManagedGroupApprover approver, bool notifyByEmail, CancellationToken cancellationToken);
    Task<IReadOnlyList<ManagedOrphanedApprovalRequest>> GetOrphanedApprovalRequestsAsync(CancellationToken cancellationToken);
    Task ExpireOrphanedApprovalRequestAsync(Guid requestId, CancellationToken cancellationToken);
    Task<TotpProtectionCertificateStatus> GetTotpProtectionCertificateStatusAsync(CancellationToken cancellationToken);
    Task RotateTotpProtectionCertificateAsync(string incomingCertificateThumbprint, string confirmation, CancellationToken cancellationToken);
    Task<CertificateOverview> GetCertificateOverviewAsync(CancellationToken cancellationToken);
    Task<MailSettingsSnapshot> GetMailSettingsAsync(CancellationToken cancellationToken);
    Task UpsertMailSettingsAsync(bool isEnabled, string smtpHost, int smtpPort, string senderAddress, string? username, string? newPassword, bool clearPassword, SmtpTlsMode tlsMode, string? rowVersion, CancellationToken cancellationToken);
    Task<MailSettingsTestResult> TestMailSettingsAsync(string recipientAddress, CancellationToken cancellationToken);
    Task<MailNotificationOutboxStatus> GetMailNotificationOutboxStatusAsync(CancellationToken cancellationToken);
    Task PurgeMailNotificationOutboxAsync(CancellationToken cancellationToken);
    Task<RequesterNotificationSettingsSnapshot> GetRequesterNotificationSettingsAsync(CancellationToken cancellationToken);
    Task UpsertRequesterNotificationSettingsAsync(string? ccAddress, string? bccAddress, string? rowVersion, CancellationToken cancellationToken);
    Task SetPersonNotificationEmailOverrideAsync(Guid personId, string? notificationEmailOverride, string rowVersion, CancellationToken cancellationToken);
}

/// <summary>Every call resolves the real, currently Kerberos-authenticated actor via <see cref="ICurrentPimActorContext"/> - the Actor identity sent to the API (and therefore the ActorAccountId attributed on every resulting audit event) is never a static configuration value.</summary>
public sealed class AdministrationClient(HttpClient httpClient, OperatorTestOptions options, ICurrentPimActorContext actorContext, IHttpContextAccessor httpContextAccessor) : IAdministrationClient
{
    public Task<DirectoryReconciliationOverview> GetDirectoryReconciliationAsync(CancellationToken cancellationToken)
        => PostAsync<DirectoryReconciliationOverview>("/api/v1/admin/directory-reconciliation/query", cancellationToken);

    public async Task<AuditLogPage> GetAuditLogAsync(int pageNumber, int pageSize, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, string? eventType, string? result, string? actorAccount, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(new QueryAuditLogRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), pageNumber, pageSize, fromUtc, toUtc, eventType, result, null, actorAccount));
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/audit-log/query", body, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuditLogPage>(cancellationToken) ?? throw new InvalidOperationException("The API returned no audit log page.");
    }

    public async Task<TechnicalErrorLogPage> GetTechnicalErrorsAsync(int pageNumber, int pageSize, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, Guid? requestId, Guid? correlationId, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(new QueryTechnicalErrorLogRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), pageNumber, pageSize, fromUtc, toUtc, requestId, correlationId));
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/technical-errors/query", body, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TechnicalErrorLogPage>(cancellationToken) ?? throw new InvalidOperationException("The API returned no technical error log page.");
    }

    public async Task StartDirectoryReconciliationAsync(CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(new StartDirectoryReconciliationRequest(new(actor.DirectoryScopeId, actor.ObjectGuid)));
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/directory-reconciliation/runs", body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeactivateDirectoryReconciliationFindingAsync(ManagedDirectoryReconciliationFinding finding, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var path = $"/api/v1/admin/directory-reconciliation/findings/{finding.FindingId:D}/deactivate";
        var body = JsonSerializer.SerializeToUtf8Bytes(new DeactivateDirectoryReconciliationFindingRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), finding.FindingId, finding.RowVersion));
        using var response = await SendAsync(HttpMethod.Post, path, body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IdentityPurgeScopePreview> GetIdentityPurgeScopePreviewAsync(string initiatorType, Guid initiatorId, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(new QueryIdentityPurgeScopeRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), initiatorType, initiatorId));
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/identity-purge/query", body, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IdentityPurgeScopePreview>(cancellationToken) ?? throw new InvalidOperationException("The API returned no purge scope preview.");
    }

    public Task<IReadOnlyList<IdentityPurgeCandidate>> GetIdentityPurgeCandidatesAsync(CancellationToken cancellationToken)
        => PostAsync<IReadOnlyList<IdentityPurgeCandidate>>("/api/v1/admin/identity-purge/candidates/query", cancellationToken);

    public async Task ExecuteIdentityPurgeAsync(string initiatorType, Guid initiatorId, string confirmation, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(new ExecuteIdentityPurgeRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), initiatorType, initiatorId, confirmation));
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/identity-purge/execute", body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public Task<IReadOnlyList<ManagedOrphanedSecondFactorRequest>> GetOrphanedSecondFactorRequestsAsync(CancellationToken cancellationToken)
        => PostAsync<IReadOnlyList<ManagedOrphanedSecondFactorRequest>>("/api/v1/admin/mfa/orphaned-requests/query", cancellationToken);

    public async Task ExpireOrphanedSecondFactorRequestAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(new ExpireOrphanedSecondFactorRequestRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), requestId));
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/mfa/orphaned-requests/expire", body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public Task<IReadOnlyList<ManagedTargetGroup>> GetTargetGroupsAsync(CancellationToken cancellationToken)
        => PostAsync<IReadOnlyList<ManagedTargetGroup>>("/api/v1/admin/target-groups/query", cancellationToken);

    public Task<IReadOnlyList<ManagedTicketReferencePattern>> GetTicketReferencePatternsAsync(CancellationToken cancellationToken)
        => PostAsync<IReadOnlyList<ManagedTicketReferencePattern>>("/api/v1/admin/settings/ticket-reference-patterns/query", cancellationToken);

    public async Task UpsertTicketReferencePatternAsync(ManagedTicketReferencePattern pattern, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var path = $"/api/v1/admin/settings/ticket-reference-patterns/{pattern.TicketReferencePatternId:D}";
        var body = JsonSerializer.SerializeToUtf8Bytes(new UpsertTicketReferencePatternRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), pattern.TicketReferencePatternId, pattern.TargetGroupId, pattern.Label, pattern.Expression, pattern.IsActive, pattern.RowVersion));
        using var response = await SendAsync(HttpMethod.Put, path, body, cancellationToken); response.EnsureSuccessStatusCode();
    }

    public async Task DeleteTicketReferencePatternAsync(ManagedTicketReferencePattern pattern, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var path = $"/api/v1/admin/settings/ticket-reference-patterns/{pattern.TicketReferencePatternId:D}";
        var body = JsonSerializer.SerializeToUtf8Bytes(new DeleteTicketReferencePatternRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), pattern.TicketReferencePatternId, pattern.RowVersion));
        using var response = await SendAsync(HttpMethod.Delete, path, body, cancellationToken); response.EnsureSuccessStatusCode();
    }

    public Task<IReadOnlyList<ManagedGroupNotificationRecipient>> GetGroupNotificationRecipientsAsync(CancellationToken cancellationToken)
        => PostAsync<IReadOnlyList<ManagedGroupNotificationRecipient>>("/api/v1/admin/settings/group-notification-recipients/query", cancellationToken);

    public async Task UpsertGroupNotificationRecipientAsync(ManagedGroupNotificationRecipient recipient, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var path = $"/api/v1/admin/settings/group-notification-recipients/{recipient.GroupNotificationRecipientId:D}";
        var body = JsonSerializer.SerializeToUtf8Bytes(new UpsertGroupNotificationRecipientRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), recipient.GroupNotificationRecipientId, recipient.TargetGroupId, recipient.EmailAddress, recipient.RecipientType, recipient.IsActive, recipient.RowVersion));
        using var response = await SendAsync(HttpMethod.Put, path, body, cancellationToken); response.EnsureSuccessStatusCode();
    }

    public async Task DeleteGroupNotificationRecipientAsync(ManagedGroupNotificationRecipient recipient, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var path = $"/api/v1/admin/settings/group-notification-recipients/{recipient.GroupNotificationRecipientId:D}";
        var body = JsonSerializer.SerializeToUtf8Bytes(new DeleteGroupNotificationRecipientRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), recipient.GroupNotificationRecipientId, recipient.RowVersion));
        using var response = await SendAsync(HttpMethod.Delete, path, body, cancellationToken); response.EnsureSuccessStatusCode();
    }

    public async Task<NotificationTemplateSnapshot> GetNotificationTemplateAsync(string templateKey, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(new QueryNotificationTemplateRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), templateKey));
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/settings/notification-templates/query", body, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<NotificationTemplateSnapshot>(cancellationToken) ?? throw new InvalidOperationException("The API response was invalid.");
    }

    public async Task UpsertNotificationTemplateAsync(string templateKey, string subject, string body, string? rowVersion, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var requestBody = JsonSerializer.SerializeToUtf8Bytes(new UpsertNotificationTemplateRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), templateKey, subject, body, rowVersion));
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/settings/notification-templates/upsert", requestBody, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public Task<TotpProtectionCertificateStatus> GetTotpProtectionCertificateStatusAsync(CancellationToken cancellationToken)
        => PostAsync<TotpProtectionCertificateStatus>("/api/v1/admin/settings/totp-protection-certificate/status/query", cancellationToken);

    public async Task RotateTotpProtectionCertificateAsync(string incomingCertificateThumbprint, string confirmation, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(new RotateTotpProtectionCertificateRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), incomingCertificateThumbprint, confirmation));
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/settings/totp-protection-certificate/rotate", body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public Task<CertificateOverview> GetCertificateOverviewAsync(CancellationToken cancellationToken)
        => PostAsync<CertificateOverview>("/api/v1/admin/certificates/overview/query", cancellationToken);

    public Task<MailSettingsSnapshot> GetMailSettingsAsync(CancellationToken cancellationToken)
        => PostAsync<MailSettingsSnapshot>("/api/v1/admin/settings/mail/query", cancellationToken);

    public async Task UpsertMailSettingsAsync(bool isEnabled, string smtpHost, int smtpPort, string senderAddress, string? username, string? newPassword, bool clearPassword, SmtpTlsMode tlsMode, string? rowVersion, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(new UpsertMailSettingsRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), isEnabled, smtpHost, smtpPort, senderAddress, username, newPassword, clearPassword, tlsMode, rowVersion));
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/settings/mail/upsert", body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public Task<MailNotificationOutboxStatus> GetMailNotificationOutboxStatusAsync(CancellationToken cancellationToken)
        => PostAsync<MailNotificationOutboxStatus>("/api/v1/admin/settings/mail/outbox-status/query", cancellationToken);

    public async Task PurgeMailNotificationOutboxAsync(CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(new PurgeMailNotificationOutboxRequest(new(actor.DirectoryScopeId, actor.ObjectGuid)));
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/settings/mail/outbox/purge", body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<MailSettingsTestResult> TestMailSettingsAsync(string recipientAddress, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(new TestMailSettingsRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), recipientAddress));
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/settings/mail/test", body, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MailSettingsTestResult>(cancellationToken) ?? throw new InvalidOperationException("The API response was invalid.");
    }

    public Task<RequesterNotificationSettingsSnapshot> GetRequesterNotificationSettingsAsync(CancellationToken cancellationToken)
        => PostAsync<RequesterNotificationSettingsSnapshot>("/api/v1/admin/settings/notification-requester/query", cancellationToken);

    public async Task UpsertRequesterNotificationSettingsAsync(string? ccAddress, string? bccAddress, string? rowVersion, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(new UpsertRequesterNotificationSettingsRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), ccAddress, bccAddress, rowVersion));
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/settings/notification-requester/upsert", body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task SetPersonNotificationEmailOverrideAsync(Guid personId, string? notificationEmailOverride, string rowVersion, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(new SetPersonNotificationEmailOverrideRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), personId, notificationEmailOverride, rowVersion));
        using var response = await SendAsync(HttpMethod.Post, $"/api/v1/admin/persons/{personId:D}/notification-email-override", body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public Task<IReadOnlyList<ManagedPerson>> GetPersonsAsync(CancellationToken cancellationToken) => PostAsync<IReadOnlyList<ManagedPerson>>("/api/v1/admin/persons/query", cancellationToken);
    public async Task<ManagedPersonDetail> GetPersonDetailAsync(Guid personId, CancellationToken cancellationToken) => await PostAsync<ManagedPersonDetail>($"/api/v1/admin/persons/{personId:D}/detail/query", cancellationToken);

    public async Task<IReadOnlyList<DirectoryUserSearchResult>> SearchDirectoryUsersAsync(string searchTerm, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(new SearchDirectoryUsersRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), searchTerm));
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/directory-users/search", body, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<DirectoryUserSearchResult>>(cancellationToken) ?? [];
    }

    public async Task CreatePersonFromDirectoryAccountAsync(Guid objectGuid, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(new CreatePersonFromDirectoryAccountRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), objectGuid));
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/persons/from-directory-account", body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task SetPersonActiveAsync(ManagedPerson person, bool active, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var path = $"/api/v1/admin/persons/{person.PersonId:D}/{(active ? "reactivate" : "deactivate")}";
        var body = active
            ? JsonSerializer.SerializeToUtf8Bytes(new ReactivatePersonRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), person.PersonId, person.RowVersion))
            : JsonSerializer.SerializeToUtf8Bytes(new DeactivatePersonRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), person.PersonId, person.RowVersion));
        using var response = await SendAsync(HttpMethod.Post, path, body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<DirectoryUserSearchResult>> SearchDirectoryAccountsAsync(string searchTerm, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(new SearchDirectoryUsersRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), searchTerm));
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/directory-accounts/search", body, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<DirectoryUserSearchResult>>(cancellationToken) ?? [];
    }

    public async Task CreatePersonAccountLinkAsync(Guid personId, Guid objectGuid, bool mayAuthenticate, bool mayReceivePrivileges, bool mayApprove, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var path = $"/api/v1/admin/persons/{personId:D}/accounts";
        var body = JsonSerializer.SerializeToUtf8Bytes(new CreatePersonAccountLinkRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), personId, objectGuid, mayAuthenticate, mayReceivePrivileges, mayApprove));
        using var response = await SendAsync(HttpMethod.Post, path, body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdatePersonAccountLinkPurposesAsync(Guid personId, ManagedPersonAccountLink link, bool mayAuthenticate, bool mayReceivePrivileges, bool mayApprove, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var path = $"/api/v1/admin/persons/{personId:D}/accounts/{link.PersonAccountLinkId:D}/purposes";
        var body = JsonSerializer.SerializeToUtf8Bytes(new UpdatePersonAccountLinkPurposesRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), personId, link.PersonAccountLinkId, mayAuthenticate, mayReceivePrivileges, mayApprove, link.RowVersion));
        using var response = await SendAsync(HttpMethod.Put, path, body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task SetPersonAccountLinkActiveAsync(Guid personId, ManagedPersonAccountLink link, bool active, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var path = $"/api/v1/admin/persons/{personId:D}/accounts/{link.PersonAccountLinkId:D}/{(active ? "reactivate" : "deactivate")}";
        var body = active
            ? JsonSerializer.SerializeToUtf8Bytes(new ReactivatePersonAccountLinkRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), personId, link.PersonAccountLinkId, link.RowVersion))
            : JsonSerializer.SerializeToUtf8Bytes(new DeactivatePersonAccountLinkRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), personId, link.PersonAccountLinkId, link.RowVersion));
        using var response = await SendAsync(HttpMethod.Post, path, body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<DirectoryGroupSearchResult>> SearchDirectoryGroupsAsync(string searchTerm, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(new SearchDirectoryGroupsRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), searchTerm));
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/directory-groups/search", body, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<DirectoryGroupSearchResult>>(cancellationToken) ?? [];
    }

    public Task<IReadOnlyList<ManagedTotpFactor>> ListTotpFactorsAsync(Guid personId, CancellationToken cancellationToken)
        => PostAsync<IReadOnlyList<ManagedTotpFactor>>($"/api/v1/admin/persons/{personId:D}/mfa/totp-factors/query", cancellationToken);

    public async Task RevokeTotpFactorAsync(Guid personId, ManagedTotpFactor factor, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var path = $"/api/v1/admin/persons/{personId:D}/mfa/totp-factors/{factor.TotpFactorId:D}/revoke";
        var body = JsonSerializer.SerializeToUtf8Bytes(new RevokeTotpFactorRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), personId, factor.TotpFactorId, factor.RowVersion));
        using var response = await SendAsync(HttpMethod.Post, path, body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public Task<IReadOnlyList<ManagedFido2Credential>> ListFido2CredentialsAsync(Guid personId, CancellationToken cancellationToken)
        => PostAsync<IReadOnlyList<ManagedFido2Credential>>($"/api/v1/admin/persons/{personId:D}/mfa/fido2-credentials/query", cancellationToken);

    public async Task RevokeFido2CredentialAsync(Guid personId, ManagedFido2Credential credential, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var path = $"/api/v1/admin/persons/{personId:D}/mfa/fido2-credentials/{credential.Fido2CredentialId:D}/revoke";
        var body = JsonSerializer.SerializeToUtf8Bytes(new RevokeFido2CredentialRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), personId, credential.Fido2CredentialId, credential.RowVersion));
        using var response = await SendAsync(HttpMethod.Post, path, body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public Task<IReadOnlyList<ManagedDirectEntitlement>> GetEntitlementsAsync(CancellationToken cancellationToken)
        => PostAsync<IReadOnlyList<ManagedDirectEntitlement>>("/api/v1/admin/entitlements/query", cancellationToken);

    public Task<IReadOnlyList<EntitlementSubject>> GetEligibleEntitlementSubjectsAsync(CancellationToken cancellationToken)
        => PostAsync<IReadOnlyList<EntitlementSubject>>("/api/v1/admin/entitlement-subjects/query", cancellationToken);

    public async Task CreateDirectEntitlementAsync(Guid personId, Guid targetAccountId, Guid targetGroupId, DateTimeOffset validFromUtc, DateTimeOffset? validUntilUtc, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(new CreateDirectEntitlementRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), personId, targetAccountId, targetGroupId, validFromUtc, validUntilUtc));
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/entitlements", body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateTargetGroupPolicyAsync(ManagedTargetGroup group, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var path = $"/api/v1/admin/target-groups/{group.TargetGroupId:D}/policy";
        var body = JsonSerializer.SerializeToUtf8Bytes(new UpdateTargetGroupPolicyRequest(
            new(actor.DirectoryScopeId, actor.ObjectGuid), group.TargetGroupId,
            group.IsEnabledForRequests, group.MinimumTtlSeconds, group.MaximumTtlSeconds,
            group.DefaultTtlSeconds, group.AllowedTtlStepSeconds, group.RequiresSecondFactor,
            group.AllowedSecondFactorTypes, group.RequiresTicket, group.RequiresApproval,
            group.PolicyIsActive, group.RowVersion));
        using var response = await SendAsync(HttpMethod.Put, path, body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task CreateTargetGroupAsync(Guid objectGuid, long minimumTtlSeconds, long maximumTtlSeconds, long defaultTtlSeconds, long allowedTtlStepSeconds, bool requiresSecondFactor, int allowedSecondFactorTypes, bool requiresTicket, bool requiresApproval, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(new CreateTargetGroupRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), objectGuid, minimumTtlSeconds, maximumTtlSeconds, defaultTtlSeconds, allowedTtlStepSeconds, requiresSecondFactor, allowedSecondFactorTypes, requiresTicket, requiresApproval));
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/target-groups", body, cancellationToken); response.EnsureSuccessStatusCode();
    }

    public async Task DeactivateTargetGroupAsync(ManagedTargetGroup group, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var path = $"/api/v1/admin/target-groups/{group.TargetGroupId:D}/deactivate";
        var body = JsonSerializer.SerializeToUtf8Bytes(new DeactivateTargetGroupRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), group.TargetGroupId, group.RowVersion));
        using var response = await SendAsync(HttpMethod.Post, path, body, cancellationToken); response.EnsureSuccessStatusCode();
    }

    public async Task ReactivateTargetGroupAsync(ManagedTargetGroup group, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var path = $"/api/v1/admin/target-groups/{group.TargetGroupId:D}/reactivate";
        var body = JsonSerializer.SerializeToUtf8Bytes(new ReactivateTargetGroupRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), group.TargetGroupId, group.RowVersion));
        using var response = await SendAsync(HttpMethod.Post, path, body, cancellationToken); response.EnsureSuccessStatusCode();
    }

    public async Task DeactivateDirectEntitlementAsync(ManagedDirectEntitlement entitlement, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var path = $"/api/v1/admin/entitlements/{entitlement.EntitlementId:D}/deactivate";
        var body = JsonSerializer.SerializeToUtf8Bytes(new DeactivateDirectEntitlementRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), entitlement.EntitlementId, entitlement.RowVersion));
        using var response = await SendAsync(HttpMethod.Post, path, body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateDirectEntitlementValidityAsync(ManagedDirectEntitlement entitlement, DateTimeOffset? validUntilUtc, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var path = $"/api/v1/admin/entitlements/{entitlement.EntitlementId:D}/validity";
        var body = JsonSerializer.SerializeToUtf8Bytes(new UpdateDirectEntitlementValidityRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), entitlement.EntitlementId, validUntilUtc, entitlement.RowVersion));
        using var response = await SendAsync(HttpMethod.Put, path, body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateDirectEntitlementConstraintsAsync(ManagedDirectEntitlement entitlement, long? minimumTtlSeconds, long? maximumTtlSeconds, long? allowedTtlStepSeconds, bool? requiresSecondFactor, bool? requiresTicket, bool? requiresApproval, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var path = $"/api/v1/admin/entitlements/{entitlement.EntitlementId:D}/constraints";
        var body = JsonSerializer.SerializeToUtf8Bytes(new UpdateDirectEntitlementConstraintsRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), entitlement.EntitlementId, minimumTtlSeconds, maximumTtlSeconds, allowedTtlStepSeconds, requiresSecondFactor, requiresTicket, requiresApproval, entitlement.RowVersion));
        using var response = await SendAsync(HttpMethod.Put, path, body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<ManagedGroupApprover>> GetGroupApproversAsync(Guid targetGroupId, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var path = $"/api/v1/admin/target-groups/{targetGroupId:D}/approvers/query";
        var body = JsonSerializer.SerializeToUtf8Bytes(new QueryGroupApproversRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), targetGroupId));
        using var response = await SendAsync(HttpMethod.Post, path, body, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<ManagedGroupApprover>>(cancellationToken) ?? [];
    }

    public async Task AddGroupApproverAsync(Guid targetGroupId, Guid personId, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var path = $"/api/v1/admin/target-groups/{targetGroupId:D}/approvers";
        var body = JsonSerializer.SerializeToUtf8Bytes(new AddGroupApproverRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), targetGroupId, personId));
        using var response = await SendAsync(HttpMethod.Post, path, body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeactivateGroupApproverAsync(Guid targetGroupId, ManagedGroupApprover approver, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var path = $"/api/v1/admin/target-groups/{targetGroupId:D}/approvers/{approver.GroupApproverId:D}/deactivate";
        var body = JsonSerializer.SerializeToUtf8Bytes(new DeactivateGroupApproverRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), targetGroupId, approver.GroupApproverId, approver.RowVersion));
        using var response = await SendAsync(HttpMethod.Post, path, body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateGroupApproverNotificationPreferenceAsync(Guid targetGroupId, ManagedGroupApprover approver, bool notifyByEmail, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var path = $"/api/v1/admin/target-groups/{targetGroupId:D}/approvers/{approver.GroupApproverId:D}/notification-preference";
        var body = JsonSerializer.SerializeToUtf8Bytes(new UpdateGroupApproverNotificationPreferenceRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), targetGroupId, approver.GroupApproverId, notifyByEmail, approver.RowVersion));
        using var response = await SendAsync(HttpMethod.Post, path, body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public Task<IReadOnlyList<ManagedOrphanedApprovalRequest>> GetOrphanedApprovalRequestsAsync(CancellationToken cancellationToken)
        => PostAsync<IReadOnlyList<ManagedOrphanedApprovalRequest>>("/api/v1/admin/orphaned-approvals/query", cancellationToken);

    public async Task ExpireOrphanedApprovalRequestAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(new ExpireOrphanedApprovalRequestRequest(new(actor.DirectoryScopeId, actor.ObjectGuid), requestId));
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/admin/orphaned-approvals/expire", body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<T> PostAsync<T>(string path, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(new QueryAdministrationRequest(new(actor.DirectoryScopeId, actor.ObjectGuid)));
        using var response = await SendAsync(HttpMethod.Post, path, body, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken) ?? throw new InvalidOperationException("The API response was invalid.");
    }

    private async Task<AuthenticatedDirectoryAccount> RequireActorAsync(CancellationToken cancellationToken)
        => await actorContext.ResolveAsync(cancellationToken) ?? throw new UnauthorizedAccessException("The authenticated Windows account could not be resolved.");

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, byte[] body, CancellationToken cancellationToken)
    {
        var signature = Sign(method.Method, path, body);
        using var request = new HttpRequestMessage(method, path) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("X-Api-Signature-Version", signature.Version); request.Headers.Add("X-Api-Key-Id", signature.KeyId); request.Headers.Add("X-Request-Id", signature.RequestId.ToString("D")); request.Headers.Add("X-Correlation-Id", signature.CorrelationId.ToString("D")); request.Headers.Add("X-Issued-Utc", signature.IssuedUtc.UtcDateTime.ToString("O")); request.Headers.Add("X-Api-Nonce", signature.Nonce); request.Headers.Add("X-Api-Signature", signature.Signature);
        // Informational only, unsigned - the browser's own remote IP as Web itself saw it on the incoming
        // request. The API never talks to the browser directly, so this is the only place that IP is known.
        var clientIp = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrWhiteSpace(clientIp)) request.Headers.Add("X-Client-Ip", clientIp);
        return await httpClient.SendAsync(request, cancellationToken);
    }

    private ApiRequestSignature Sign(string method, string path, byte[] body)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine); store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        using var certificate = store.Certificates.Find(X509FindType.FindByThumbprint, options.SigningCertificateThumbprint!, false).OfType<X509Certificate2>().SingleOrDefault() ?? throw new InvalidOperationException("The configured Web signing certificate was not found.");
        var unsigned = new ApiRequestSignature(ApiRequestSignature.CurrentVersion, options.SigningKeyId!, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_'), method, path, string.Empty, ApiRequestCanonicalizer.ComputeBodyHash(body), "application/json", "1", string.Empty);
        return unsigned with { Signature = ApiRequestCanonicalizer.ComputeSignature(unsigned, certificate) };
    }
}

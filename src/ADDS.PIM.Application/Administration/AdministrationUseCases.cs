using ADDS.PIM.Contracts.Administration.V1;
using ADDS.PIM.Application.Authorization;

namespace ADDS.PIM.Application.Administration;

/// <summary>
/// <see cref="ClientSourceIpAddress"/> is the browser's own remote IP (Web-captured, forwarded via
/// X-Client-Ip) - informational only, distinct from <see cref="SourceIpAddress"/> (the Web frontend
/// instance's own IP as seen by the API).
/// </summary>
public sealed record AdministrationAuditContext(Guid CorrelationId, string FrontendClientId, string? SourceIpAddress, string? ClientSourceIpAddress = null);

public enum AdministrationUpdateResult { Updated, NotFound, Conflict, Invalid }

public interface IAdministrationDataStore
{
    Task<IReadOnlyList<ManagedPerson>> ListPersonsAsync(CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> DeactivatePersonAsync(DeactivatePersonRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> ReactivatePersonAsync(ReactivatePersonRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> CreatePersonFromDirectoryAccountAsync(CreatePersonFromDirectoryAccountRequest request, ResolvedDirectoryAccount account, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<ManagedPersonDetail?> GetPersonDetailAsync(Guid personId, CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> CreatePersonAccountLinkAsync(CreatePersonAccountLinkRequest request, ResolvedDirectoryAccount account, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> UpdatePersonAccountLinkPurposesAsync(UpdatePersonAccountLinkPurposesRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> DeactivatePersonAccountLinkAsync(DeactivatePersonAccountLinkRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> ReactivatePersonAccountLinkAsync(ReactivatePersonAccountLinkRequest request, ResolvedDirectoryAccount account, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<IReadOnlyList<ManagedTotpFactor>> ListTotpFactorsAsync(Guid personId, CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> RevokeTotpFactorAsync(RevokeTotpFactorRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<IReadOnlyList<ManagedFido2Credential>> ListFido2CredentialsAsync(Guid personId, CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> RevokeFido2CredentialAsync(RevokeFido2CredentialRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<Guid?> GetPersonAccountLinkObjectGuidAsync(Guid personId, Guid personAccountLinkId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ManagedTargetGroup>> ListTargetGroupsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ManagedTicketReferencePattern>> ListTicketReferencePatternsAsync(CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> UpsertTicketReferencePatternAsync(UpsertTicketReferencePatternRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> DeleteTicketReferencePatternAsync(DeleteTicketReferencePatternRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<IReadOnlyList<ManagedGroupNotificationRecipient>> ListGroupNotificationRecipientsAsync(CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> UpsertGroupNotificationRecipientAsync(UpsertGroupNotificationRecipientRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> DeleteGroupNotificationRecipientAsync(DeleteGroupNotificationRecipientRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<NotificationTemplateSnapshot> GetNotificationTemplateAsync(string templateKey, CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> UpsertNotificationTemplateAsync(UpsertNotificationTemplateRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<IReadOnlyList<ManagedDirectEntitlement>> ListDirectEntitlementsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<EntitlementSubject>> ListEligibleEntitlementSubjectsAsync(CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> CreateDirectEntitlementAsync(CreateDirectEntitlementRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> UpdateDirectEntitlementValidityAsync(UpdateDirectEntitlementValidityRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> UpdateDirectEntitlementConstraintsAsync(UpdateDirectEntitlementConstraintsRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> UpdateTargetGroupPolicyAsync(UpdateTargetGroupPolicyRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> DeactivateDirectEntitlementAsync(DeactivateDirectEntitlementRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> CreateTargetGroupAsync(CreateTargetGroupRequest request, ResolvedDirectoryGroup group, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> DeactivateTargetGroupAsync(DeactivateTargetGroupRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> ReactivateTargetGroupAsync(ReactivateTargetGroupRequest request, ResolvedDirectoryGroup group, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<Guid?> GetTargetGroupObjectGuidAsync(Guid targetGroupId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ManagedGroupApprover>> ListGroupApproversAsync(Guid targetGroupId, CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> AddGroupApproverAsync(AddGroupApproverRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> DeactivateGroupApproverAsync(DeactivateGroupApproverRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> UpdateGroupApproverNotificationPreferenceAsync(UpdateGroupApproverNotificationPreferenceRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<TotpProtectionCertificateStatus> GetTotpProtectionCertificateStatusAsync(CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> RotateTotpProtectionCertificateAsync(RotateTotpProtectionCertificateRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<CertificateOverview> GetCertificateOverviewAsync(CancellationToken cancellationToken);
    Task<MailSettingsSnapshot> GetMailSettingsAsync(CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> UpsertMailSettingsAsync(UpsertMailSettingsRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<MailSettingsTestResult> TestMailSettingsAsync(TestMailSettingsRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<MailNotificationOutboxStatus> GetMailNotificationOutboxStatusAsync(CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> PurgeMailNotificationOutboxAsync(PurgeMailNotificationOutboxRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<RequesterNotificationSettingsSnapshot> GetRequesterNotificationSettingsAsync(CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> UpsertRequesterNotificationSettingsAsync(UpsertRequesterNotificationSettingsRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
    Task<AdministrationUpdateResult> SetPersonNotificationEmailOverrideAsync(SetPersonNotificationEmailOverrideRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken);
}

public sealed record ResolvedDirectoryGroup(Guid ObjectGuid, string SamAccountName, string DistinguishedName, string DomainQualifiedName, string DisplayName, string? ObjectSid);
public interface IDirectoryGroupResolver
{
    Task<ResolvedDirectoryGroup?> ResolveAsync(Guid objectGuid, CancellationToken cancellationToken);
    Task<IReadOnlyList<ResolvedDirectoryGroup>> SearchAsync(string searchTerm, CancellationToken cancellationToken);
}
public sealed record ResolvedDirectoryAccount(Guid ObjectGuid, string SamAccountName, string DistinguishedName, string DomainQualifiedName, string DisplayName, string? UserPrincipalName, string? EmailAddress, string? ObjectSid, bool IsEnabled);
public interface IDirectoryAccountResolver
{
    Task<IReadOnlyList<ResolvedDirectoryAccount>> SearchPimUsersAsync(string searchTerm, CancellationToken cancellationToken);
    Task<ResolvedDirectoryAccount?> ResolvePimUserAsync(Guid objectGuid, CancellationToken cancellationToken);
    Task<IReadOnlyList<ResolvedDirectoryAccount>> SearchAccountsAsync(string searchTerm, CancellationToken cancellationToken);
    Task<ResolvedDirectoryAccount?> ResolveAccountAsync(Guid objectGuid, CancellationToken cancellationToken);
}

public sealed class AdministrationUseCases(IAdministrationDataStore store, IDirectoryGroupResolver directoryGroupResolver, IDirectoryAccountResolver directoryAccountResolver, DirectoryScopeConfiguration directoryScope)
{
    public Task<IReadOnlyList<ManagedTicketReferencePattern>> ListTicketReferencePatternsAsync(CancellationToken cancellationToken) => store.ListTicketReferencePatternsAsync(cancellationToken);
    public Task<AdministrationUpdateResult> UpsertTicketReferencePatternAsync(UpsertTicketReferencePatternRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
        => request.Actor.DirectoryScopeId == directoryScope.DirectoryScopeId && request.Actor.ObjectGuid != Guid.Empty && request.TargetGroupId != Guid.Empty && !string.IsNullOrWhiteSpace(request.Label) && !string.IsNullOrWhiteSpace(request.Expression) && request.Label.Trim().Length is > 0 and <= 128 && request.Expression.Trim().Length is > 0 and <= 512
            ? store.UpsertTicketReferencePatternAsync(request with { Label = request.Label.Trim(), Expression = request.Expression.Trim() }, auditContext, cancellationToken)
            : Task.FromResult(AdministrationUpdateResult.Invalid);
    public Task<AdministrationUpdateResult> DeleteTicketReferencePatternAsync(DeleteTicketReferencePatternRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
        => request.Actor.DirectoryScopeId == directoryScope.DirectoryScopeId && request.Actor.ObjectGuid != Guid.Empty && request.TicketReferencePatternId != Guid.Empty && !string.IsNullOrWhiteSpace(request.RowVersion)
            ? store.DeleteTicketReferencePatternAsync(request, auditContext, cancellationToken) : Task.FromResult(AdministrationUpdateResult.Invalid);

    public Task<IReadOnlyList<ManagedGroupNotificationRecipient>> ListGroupNotificationRecipientsAsync(CancellationToken cancellationToken) => store.ListGroupNotificationRecipientsAsync(cancellationToken);

    public Task<AdministrationUpdateResult> UpsertGroupNotificationRecipientAsync(UpsertGroupNotificationRecipientRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
        => request.Actor.DirectoryScopeId == directoryScope.DirectoryScopeId && request.Actor.ObjectGuid != Guid.Empty && request.TargetGroupId != Guid.Empty && IsValidEmailAddress(request.EmailAddress)
           && request.RecipientType is MailRecipientType.To or MailRecipientType.Cc or MailRecipientType.Bcc
            ? store.UpsertGroupNotificationRecipientAsync(request with { EmailAddress = request.EmailAddress.Trim() }, auditContext, cancellationToken)
            : Task.FromResult(AdministrationUpdateResult.Invalid);

    public Task<AdministrationUpdateResult> DeleteGroupNotificationRecipientAsync(DeleteGroupNotificationRecipientRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
        => request.Actor.DirectoryScopeId == directoryScope.DirectoryScopeId && request.Actor.ObjectGuid != Guid.Empty && request.GroupNotificationRecipientId != Guid.Empty && !string.IsNullOrWhiteSpace(request.RowVersion)
            ? store.DeleteGroupNotificationRecipientAsync(request, auditContext, cancellationToken) : Task.FromResult(AdministrationUpdateResult.Invalid);

    public Task<NotificationTemplateSnapshot> GetNotificationTemplateAsync(string templateKey, CancellationToken cancellationToken)
        => store.GetNotificationTemplateAsync(templateKey, cancellationToken);

    public Task<AdministrationUpdateResult> UpsertNotificationTemplateAsync(UpsertNotificationTemplateRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
        => request.Actor.DirectoryScopeId == directoryScope.DirectoryScopeId && request.Actor.ObjectGuid != Guid.Empty
           && !string.IsNullOrWhiteSpace(request.TemplateKey) && !string.IsNullOrWhiteSpace(request.Subject) && request.Subject.Trim().Length <= 256
           && !string.IsNullOrWhiteSpace(request.Body) && request.Body.Trim().Length <= 4000
            ? store.UpsertNotificationTemplateAsync(request with { Subject = request.Subject.Trim(), Body = request.Body.Trim() }, auditContext, cancellationToken)
            : Task.FromResult(AdministrationUpdateResult.Invalid);
    public Task<IReadOnlyList<ManagedPerson>> ListPersonsAsync(CancellationToken cancellationToken) => store.ListPersonsAsync(cancellationToken);
    public Task<ManagedPersonDetail?> GetPersonDetailAsync(Guid personId, CancellationToken cancellationToken)
        => personId == Guid.Empty ? Task.FromResult<ManagedPersonDetail?>(null) : store.GetPersonDetailAsync(personId, cancellationToken);

    public Task<AdministrationUpdateResult> DeactivatePersonAsync(DeactivatePersonRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
        => IsValidPersonMutation(request.Actor, request.PersonId, request.RowVersion) ? store.DeactivatePersonAsync(request, auditContext, cancellationToken) : Task.FromResult(AdministrationUpdateResult.Invalid);

    public Task<AdministrationUpdateResult> ReactivatePersonAsync(ReactivatePersonRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
        => IsValidPersonMutation(request.Actor, request.PersonId, request.RowVersion) ? store.ReactivatePersonAsync(request, auditContext, cancellationToken) : Task.FromResult(AdministrationUpdateResult.Invalid);

    public async Task<IReadOnlyList<DirectoryUserSearchResult>> SearchDirectoryUsersAsync(SearchDirectoryUsersRequest request, CancellationToken cancellationToken)
    {
        if (request.Actor.DirectoryScopeId != directoryScope.DirectoryScopeId || request.Actor.ObjectGuid == Guid.Empty || string.IsNullOrWhiteSpace(request.SearchTerm) || request.SearchTerm.Trim().Length is < 2 or > 128) return [];
        return (await directoryAccountResolver.SearchPimUsersAsync(request.SearchTerm.Trim(), cancellationToken)).Select(account => new DirectoryUserSearchResult(account.ObjectGuid, account.DisplayName, account.DomainQualifiedName, account.SamAccountName, account.UserPrincipalName, account.EmailAddress)).ToArray();
    }

    public async Task<AdministrationUpdateResult> CreatePersonFromDirectoryAccountAsync(CreatePersonFromDirectoryAccountRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        if (request.Actor.DirectoryScopeId != directoryScope.DirectoryScopeId || request.Actor.ObjectGuid == Guid.Empty || request.ObjectGuid == Guid.Empty) return AdministrationUpdateResult.Invalid;
        var account = await directoryAccountResolver.ResolvePimUserAsync(request.ObjectGuid, cancellationToken);
        return account is null ? AdministrationUpdateResult.NotFound : await store.CreatePersonFromDirectoryAccountAsync(request, account, auditContext, cancellationToken);
    }

    public async Task<IReadOnlyList<DirectoryUserSearchResult>> SearchDirectoryAccountsAsync(SearchDirectoryUsersRequest request, CancellationToken cancellationToken)
    {
        if (!IsValidDirectorySearch(request.Actor, request.SearchTerm)) return [];
        return (await directoryAccountResolver.SearchAccountsAsync(request.SearchTerm.Trim(), cancellationToken))
            .Select(account => new DirectoryUserSearchResult(account.ObjectGuid, account.DisplayName, account.DomainQualifiedName, account.SamAccountName, account.UserPrincipalName, account.EmailAddress)).ToArray();
    }

    public async Task<AdministrationUpdateResult> CreatePersonAccountLinkAsync(CreatePersonAccountLinkRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        if (!IsValidAccountLinkMutation(request.Actor, request.PersonId, request.ObjectGuid, request.MayAuthenticate, request.MayReceivePrivileges) || (request.MayApprove && !request.MayAuthenticate)) return AdministrationUpdateResult.Invalid;
        var account = await directoryAccountResolver.ResolveAccountAsync(request.ObjectGuid, cancellationToken);
        return account is null || !account.IsEnabled ? AdministrationUpdateResult.NotFound : await store.CreatePersonAccountLinkAsync(request, account, auditContext, cancellationToken);
    }

    public Task<AdministrationUpdateResult> UpdatePersonAccountLinkPurposesAsync(UpdatePersonAccountLinkPurposesRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
        => IsValidAccountLinkMutation(request.Actor, request.PersonId, request.PersonAccountLinkId, request.MayAuthenticate, request.MayReceivePrivileges) && !string.IsNullOrWhiteSpace(request.RowVersion) && !(request.MayApprove && !request.MayAuthenticate)
            ? store.UpdatePersonAccountLinkPurposesAsync(request, auditContext, cancellationToken) : Task.FromResult(AdministrationUpdateResult.Invalid);

    public Task<AdministrationUpdateResult> DeactivatePersonAccountLinkAsync(DeactivatePersonAccountLinkRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
        => IsValidLinkLifecycleMutation(request.Actor, request.PersonId, request.PersonAccountLinkId, request.RowVersion)
            ? store.DeactivatePersonAccountLinkAsync(request, auditContext, cancellationToken) : Task.FromResult(AdministrationUpdateResult.Invalid);

    public Task<IReadOnlyList<ManagedTotpFactor>> ListTotpFactorsAsync(Guid personId, CancellationToken cancellationToken)
        => personId == Guid.Empty ? Task.FromResult<IReadOnlyList<ManagedTotpFactor>>([]) : store.ListTotpFactorsAsync(personId, cancellationToken);

    /// <summary>TOTP factor reset requires this separately authorized, audited administrative workflow - no self-service reset exists.</summary>
    public Task<AdministrationUpdateResult> RevokeTotpFactorAsync(RevokeTotpFactorRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
        => IsValidLinkLifecycleMutation(request.Actor, request.PersonId, request.TotpFactorId, request.RowVersion)
            ? store.RevokeTotpFactorAsync(request, auditContext, cancellationToken) : Task.FromResult(AdministrationUpdateResult.Invalid);

    public Task<IReadOnlyList<ManagedFido2Credential>> ListFido2CredentialsAsync(Guid personId, CancellationToken cancellationToken)
        => personId == Guid.Empty ? Task.FromResult<IReadOnlyList<ManagedFido2Credential>>([]) : store.ListFido2CredentialsAsync(personId, cancellationToken);

    /// <summary>Matches TOTP's admin-only reset model: no self-service revoke exists for FIDO2 credentials either.</summary>
    public Task<AdministrationUpdateResult> RevokeFido2CredentialAsync(RevokeFido2CredentialRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
        => IsValidLinkLifecycleMutation(request.Actor, request.PersonId, request.Fido2CredentialId, request.RowVersion)
            ? store.RevokeFido2CredentialAsync(request, auditContext, cancellationToken) : Task.FromResult(AdministrationUpdateResult.Invalid);

    public async Task<AdministrationUpdateResult> ReactivatePersonAccountLinkAsync(ReactivatePersonAccountLinkRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        if (!IsValidLinkLifecycleMutation(request.Actor, request.PersonId, request.PersonAccountLinkId, request.RowVersion)) return AdministrationUpdateResult.Invalid;
        var objectGuid = await store.GetPersonAccountLinkObjectGuidAsync(request.PersonId, request.PersonAccountLinkId, cancellationToken);
        if (objectGuid is null) return AdministrationUpdateResult.NotFound;
        var account = await directoryAccountResolver.ResolveAccountAsync(objectGuid.Value, cancellationToken);
        return account is null || !account.IsEnabled ? AdministrationUpdateResult.NotFound : await store.ReactivatePersonAccountLinkAsync(request, account, auditContext, cancellationToken);
    }
    public Task<IReadOnlyList<ManagedTargetGroup>> ListTargetGroupsAsync(CancellationToken cancellationToken) => store.ListTargetGroupsAsync(cancellationToken);
    public Task<IReadOnlyList<ManagedDirectEntitlement>> ListDirectEntitlementsAsync(CancellationToken cancellationToken) => store.ListDirectEntitlementsAsync(cancellationToken);
    public Task<IReadOnlyList<EntitlementSubject>> ListEligibleEntitlementSubjectsAsync(CancellationToken cancellationToken) => store.ListEligibleEntitlementSubjectsAsync(cancellationToken);

    public async Task<IReadOnlyList<DirectoryGroupSearchResult>> SearchDirectoryGroupsAsync(SearchDirectoryGroupsRequest request, CancellationToken cancellationToken)
    {
        if (request.Actor.DirectoryScopeId != directoryScope.DirectoryScopeId || request.Actor.ObjectGuid == Guid.Empty || string.IsNullOrWhiteSpace(request.SearchTerm) || request.SearchTerm.Trim().Length is < 2 or > 128)
            return [];
        var groups = await directoryGroupResolver.SearchAsync(request.SearchTerm.Trim(), cancellationToken);
        return groups.Select(group => new DirectoryGroupSearchResult(group.ObjectGuid, group.DisplayName, group.DomainQualifiedName)).ToArray();
    }

    public Task<AdministrationUpdateResult> CreateDirectEntitlementAsync(CreateDirectEntitlementRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        if (request.PersonId == Guid.Empty || request.TargetAccountId == Guid.Empty || request.TargetGroupId == Guid.Empty
            || request.Actor.DirectoryScopeId == Guid.Empty || request.Actor.ObjectGuid == Guid.Empty
            || request.ValidFromUtc == default || (request.ValidUntilUtc is not null && request.ValidUntilUtc <= request.ValidFromUtc))
            return Task.FromResult(AdministrationUpdateResult.Invalid);
        return store.CreateDirectEntitlementAsync(request, auditContext, cancellationToken);
    }

    public Task<AdministrationUpdateResult> UpdateDirectEntitlementValidityAsync(UpdateDirectEntitlementValidityRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        if (request.EntitlementId == Guid.Empty || request.Actor.DirectoryScopeId == Guid.Empty || request.Actor.ObjectGuid == Guid.Empty || string.IsNullOrWhiteSpace(request.RowVersion))
            return Task.FromResult(AdministrationUpdateResult.Invalid);
        return store.UpdateDirectEntitlementValidityAsync(request, auditContext, cancellationToken);
    }

    public Task<AdministrationUpdateResult> UpdateDirectEntitlementConstraintsAsync(UpdateDirectEntitlementConstraintsRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        if (request.EntitlementId == Guid.Empty || request.Actor.DirectoryScopeId == Guid.Empty || request.Actor.ObjectGuid == Guid.Empty || string.IsNullOrWhiteSpace(request.RowVersion)
            || request.MinimumTtlSeconds is <= 0 || request.MaximumTtlSeconds is <= 0 || request.AllowedTtlStepSeconds is <= 0
            || (request.MinimumTtlSeconds is not null && request.MaximumTtlSeconds is not null && request.MaximumTtlSeconds < request.MinimumTtlSeconds))
            return Task.FromResult(AdministrationUpdateResult.Invalid);
        return store.UpdateDirectEntitlementConstraintsAsync(request, auditContext, cancellationToken);
    }

    public Task<AdministrationUpdateResult> UpdateTargetGroupPolicyAsync(UpdateTargetGroupPolicyRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        if (request.TargetGroupId == Guid.Empty || request.Actor.DirectoryScopeId == Guid.Empty || request.Actor.ObjectGuid == Guid.Empty
            || request.MinimumTtlSeconds <= 0 || request.MaximumTtlSeconds < request.MinimumTtlSeconds
            || request.DefaultTtlSeconds < request.MinimumTtlSeconds || request.DefaultTtlSeconds > request.MaximumTtlSeconds
            || request.AllowedTtlStepSeconds <= 0 || string.IsNullOrWhiteSpace(request.RowVersion)
            || request.AllowedSecondFactorTypes is < 0 or > 3
            || (request.RequiresSecondFactor && request.AllowedSecondFactorTypes == 0))
        {
            return Task.FromResult(AdministrationUpdateResult.Invalid);
        }
        return store.UpdateTargetGroupPolicyAsync(request, auditContext, cancellationToken);
    }

    public Task<AdministrationUpdateResult> DeactivateDirectEntitlementAsync(DeactivateDirectEntitlementRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        if (request.EntitlementId == Guid.Empty || request.Actor.DirectoryScopeId == Guid.Empty || request.Actor.ObjectGuid == Guid.Empty || string.IsNullOrWhiteSpace(request.RowVersion))
            return Task.FromResult(AdministrationUpdateResult.Invalid);
        return store.DeactivateDirectEntitlementAsync(request, auditContext, cancellationToken);
    }

    public async Task<AdministrationUpdateResult> CreateTargetGroupAsync(CreateTargetGroupRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        if (request.RequiresTicket || !IsValidPolicy(request.MinimumTtlSeconds, request.MaximumTtlSeconds, request.DefaultTtlSeconds, request.AllowedTtlStepSeconds, request.RequiresSecondFactor, request.AllowedSecondFactorTypes)
            || request.Actor.DirectoryScopeId != directoryScope.DirectoryScopeId || request.Actor.ObjectGuid == Guid.Empty || request.ObjectGuid == Guid.Empty) return AdministrationUpdateResult.Invalid;
        var group = await directoryGroupResolver.ResolveAsync(request.ObjectGuid, cancellationToken);
        return group is null ? AdministrationUpdateResult.NotFound : await store.CreateTargetGroupAsync(request, group, auditContext, cancellationToken);
    }

    public Task<AdministrationUpdateResult> DeactivateTargetGroupAsync(DeactivateTargetGroupRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
        => request.TargetGroupId == Guid.Empty || request.Actor.DirectoryScopeId == Guid.Empty || request.Actor.ObjectGuid == Guid.Empty || string.IsNullOrWhiteSpace(request.RowVersion)
            ? Task.FromResult(AdministrationUpdateResult.Invalid) : store.DeactivateTargetGroupAsync(request, auditContext, cancellationToken);

    public async Task<AdministrationUpdateResult> ReactivateTargetGroupAsync(ReactivateTargetGroupRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
    {
        if (request.TargetGroupId == Guid.Empty || request.Actor.DirectoryScopeId != directoryScope.DirectoryScopeId || request.Actor.ObjectGuid == Guid.Empty || string.IsNullOrWhiteSpace(request.RowVersion)) return AdministrationUpdateResult.Invalid;
        var objectGuid = await store.GetTargetGroupObjectGuidAsync(request.TargetGroupId, cancellationToken);
        if (objectGuid is null) return AdministrationUpdateResult.NotFound;
        var group = await directoryGroupResolver.ResolveAsync(objectGuid.Value, cancellationToken);
        return group is null ? AdministrationUpdateResult.NotFound : await store.ReactivateTargetGroupAsync(request, group, auditContext, cancellationToken);
    }

    public Task<IReadOnlyList<ManagedGroupApprover>> ListGroupApproversAsync(Guid targetGroupId, CancellationToken cancellationToken)
        => targetGroupId == Guid.Empty ? Task.FromResult<IReadOnlyList<ManagedGroupApprover>>([]) : store.ListGroupApproversAsync(targetGroupId, cancellationToken);

    public Task<AdministrationUpdateResult> AddGroupApproverAsync(AddGroupApproverRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
        => request.Actor.DirectoryScopeId == directoryScope.DirectoryScopeId && request.Actor.ObjectGuid != Guid.Empty && request.TargetGroupId != Guid.Empty && request.PersonId != Guid.Empty
            ? store.AddGroupApproverAsync(request, auditContext, cancellationToken) : Task.FromResult(AdministrationUpdateResult.Invalid);

    public Task<AdministrationUpdateResult> DeactivateGroupApproverAsync(DeactivateGroupApproverRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
        => request.Actor.DirectoryScopeId == directoryScope.DirectoryScopeId && request.Actor.ObjectGuid != Guid.Empty && request.TargetGroupId != Guid.Empty && request.GroupApproverId != Guid.Empty && !string.IsNullOrWhiteSpace(request.RowVersion)
            ? store.DeactivateGroupApproverAsync(request, auditContext, cancellationToken) : Task.FromResult(AdministrationUpdateResult.Invalid);

    public Task<AdministrationUpdateResult> UpdateGroupApproverNotificationPreferenceAsync(UpdateGroupApproverNotificationPreferenceRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
        => request.Actor.DirectoryScopeId == directoryScope.DirectoryScopeId && request.Actor.ObjectGuid != Guid.Empty && request.TargetGroupId != Guid.Empty && request.GroupApproverId != Guid.Empty && !string.IsNullOrWhiteSpace(request.RowVersion)
            ? store.UpdateGroupApproverNotificationPreferenceAsync(request, auditContext, cancellationToken) : Task.FromResult(AdministrationUpdateResult.Invalid);

    public Task<TotpProtectionCertificateStatus> GetTotpProtectionCertificateStatusAsync(CancellationToken cancellationToken) => store.GetTotpProtectionCertificateStatusAsync(cancellationToken);

    /// <summary>one-time, audited re-encryption of every TOTP secret to a new protection certificate. The typed confirmation gate mirrors <see cref="ADDS.PIM.Application.Administration.IdentityPurgeUseCase.ExecuteAsync"/>'s "PURGE {id}" pattern for a similarly high-blast-radius, irreversible administrative action.</summary>
    public Task<AdministrationUpdateResult> RotateTotpProtectionCertificateAsync(RotateTotpProtectionCertificateRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
        => request.Actor.DirectoryScopeId == directoryScope.DirectoryScopeId && request.Actor.ObjectGuid != Guid.Empty
           && !string.IsNullOrWhiteSpace(request.IncomingCertificateThumbprint)
           && string.Equals(request.Confirmation, $"ROTATE {request.IncomingCertificateThumbprint}", StringComparison.Ordinal)
            ? store.RotateTotpProtectionCertificateAsync(request, auditContext, cancellationToken) : Task.FromResult(AdministrationUpdateResult.Invalid);

    public Task<CertificateOverview> GetCertificateOverviewAsync(CancellationToken cancellationToken) => store.GetCertificateOverviewAsync(cancellationToken);

    public Task<MailSettingsSnapshot> GetMailSettingsAsync(CancellationToken cancellationToken) => store.GetMailSettingsAsync(cancellationToken);

    public Task<AdministrationUpdateResult> UpsertMailSettingsAsync(UpsertMailSettingsRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
        => request.Actor.DirectoryScopeId == directoryScope.DirectoryScopeId && request.Actor.ObjectGuid != Guid.Empty
           && !string.IsNullOrWhiteSpace(request.SmtpHost) && request.SmtpHost.Trim().Length <= 256
           && request.SmtpPort is > 0 and <= 65535
           && IsValidEmailAddress(request.SenderAddress)
           && (request.Username is null || request.Username.Trim().Length <= 256)
           && request.TlsMode is SmtpTlsMode.None or SmtpTlsMode.Implicit or SmtpTlsMode.Explicit
           && !(request.ClearPassword && !string.IsNullOrEmpty(request.NewPassword))
            ? store.UpsertMailSettingsAsync(request with { SmtpHost = request.SmtpHost.Trim(), SenderAddress = request.SenderAddress.Trim(), Username = string.IsNullOrWhiteSpace(request.Username) ? null : request.Username.Trim() }, auditContext, cancellationToken)
            : Task.FromResult(AdministrationUpdateResult.Invalid);

    public Task<MailSettingsTestResult> TestMailSettingsAsync(TestMailSettingsRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
        => request.Actor.DirectoryScopeId == directoryScope.DirectoryScopeId && request.Actor.ObjectGuid != Guid.Empty && IsValidEmailAddress(request.RecipientAddress)
            ? store.TestMailSettingsAsync(request with { RecipientAddress = request.RecipientAddress.Trim() }, auditContext, cancellationToken)
            : Task.FromResult(new MailSettingsTestResult(false, "Ungültige Empfängeradresse."));

    public Task<MailNotificationOutboxStatus> GetMailNotificationOutboxStatusAsync(CancellationToken cancellationToken) => store.GetMailNotificationOutboxStatusAsync(cancellationToken);

    public Task<AdministrationUpdateResult> PurgeMailNotificationOutboxAsync(PurgeMailNotificationOutboxRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
        => request.Actor.DirectoryScopeId == directoryScope.DirectoryScopeId && request.Actor.ObjectGuid != Guid.Empty
            ? store.PurgeMailNotificationOutboxAsync(request, auditContext, cancellationToken) : Task.FromResult(AdministrationUpdateResult.Invalid);

    public Task<RequesterNotificationSettingsSnapshot> GetRequesterNotificationSettingsAsync(CancellationToken cancellationToken) => store.GetRequesterNotificationSettingsAsync(cancellationToken);

    public Task<AdministrationUpdateResult> UpsertRequesterNotificationSettingsAsync(UpsertRequesterNotificationSettingsRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
        => request.Actor.DirectoryScopeId == directoryScope.DirectoryScopeId && request.Actor.ObjectGuid != Guid.Empty
           && (string.IsNullOrWhiteSpace(request.CcAddress) || IsValidEmailAddress(request.CcAddress))
           && (string.IsNullOrWhiteSpace(request.BccAddress) || IsValidEmailAddress(request.BccAddress))
            ? store.UpsertRequesterNotificationSettingsAsync(request with { CcAddress = string.IsNullOrWhiteSpace(request.CcAddress) ? null : request.CcAddress.Trim(), BccAddress = string.IsNullOrWhiteSpace(request.BccAddress) ? null : request.BccAddress.Trim() }, auditContext, cancellationToken)
            : Task.FromResult(AdministrationUpdateResult.Invalid);

    public Task<AdministrationUpdateResult> SetPersonNotificationEmailOverrideAsync(SetPersonNotificationEmailOverrideRequest request, AdministrationAuditContext auditContext, CancellationToken cancellationToken)
        => IsValidPersonMutation(request.Actor, request.PersonId, request.RowVersion)
           && (string.IsNullOrWhiteSpace(request.NotificationEmailOverride) || IsValidEmailAddress(request.NotificationEmailOverride))
            ? store.SetPersonNotificationEmailOverrideAsync(request with { NotificationEmailOverride = string.IsNullOrWhiteSpace(request.NotificationEmailOverride) ? null : request.NotificationEmailOverride.Trim() }, auditContext, cancellationToken)
            : Task.FromResult(AdministrationUpdateResult.Invalid);

    private static bool IsValidEmailAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || address.Trim().Length > 320) return false;
        var trimmed = address.Trim();
        var atIndex = trimmed.IndexOf('@');
        return atIndex > 0 && atIndex < trimmed.Length - 1 && trimmed.IndexOf('@', atIndex + 1) < 0;
    }

    private static bool IsValidPolicy(long minimum, long maximum, long defaultTtl, long step, bool requiresSecondFactor, int allowedSecondFactorTypes)
        => minimum > 0 && maximum >= minimum && defaultTtl >= minimum && defaultTtl <= maximum && step > 0 && allowedSecondFactorTypes is >= 0 and <= 3 && (!requiresSecondFactor || allowedSecondFactorTypes != 0);
    private bool IsValidPersonMutation(AdministrationActor actor, Guid personId, string rowVersion) => actor.DirectoryScopeId == directoryScope.DirectoryScopeId && actor.ObjectGuid != Guid.Empty && personId != Guid.Empty && !string.IsNullOrWhiteSpace(rowVersion);
    private bool IsValidDirectorySearch(AdministrationActor actor, string searchTerm) => actor.DirectoryScopeId == directoryScope.DirectoryScopeId && actor.ObjectGuid != Guid.Empty && !string.IsNullOrWhiteSpace(searchTerm) && searchTerm.Trim().Length is >= 2 and <= 128;
    private bool IsValidAccountLinkMutation(AdministrationActor actor, Guid personId, Guid id, bool mayAuthenticate, bool mayReceivePrivileges) => actor.DirectoryScopeId == directoryScope.DirectoryScopeId && actor.ObjectGuid != Guid.Empty && personId != Guid.Empty && id != Guid.Empty && (mayAuthenticate || mayReceivePrivileges);
    private bool IsValidLinkLifecycleMutation(AdministrationActor actor, Guid personId, Guid linkId, string rowVersion) => actor.DirectoryScopeId == directoryScope.DirectoryScopeId && actor.ObjectGuid != Guid.Empty && personId != Guid.Empty && linkId != Guid.Empty && !string.IsNullOrWhiteSpace(rowVersion);
}

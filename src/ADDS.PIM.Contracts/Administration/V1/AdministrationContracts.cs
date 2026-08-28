namespace ADDS.PIM.Contracts.Administration.V1;

public sealed record AdministrationActor(Guid DirectoryScopeId, Guid ObjectGuid);

public sealed record QueryAdministrationRequest(AdministrationActor Actor);
public sealed record StartDirectoryReconciliationRequest(AdministrationActor Actor);
public sealed record DeactivateDirectoryReconciliationFindingRequest(AdministrationActor Actor, Guid FindingId, string RowVersion);
public sealed record ManagedDirectoryReconciliationRun(Guid RunId, string Status, DateTimeOffset RequestedUtc, DateTimeOffset? StartedUtc, DateTimeOffset? CompletedUtc, string? FailureCategory, string RowVersion);
public sealed record ManagedDirectoryReconciliationFinding(Guid FindingId, Guid RunId, string EntityType, Guid EntityId, Guid DirectoryScopeId, Guid ObjectGuid, string Reason, string DisplayName, DateTimeOffset DetectedUtc, bool IsResolved, DateTimeOffset? ResolvedUtc, DateTimeOffset? DeactivatedUtc, string RowVersion);
public sealed record DirectoryReconciliationOverview(IReadOnlyList<ManagedDirectoryReconciliationRun> Runs, IReadOnlyList<ManagedDirectoryReconciliationFinding> Findings);
public sealed record QueryIdentityPurgeScopeRequest(AdministrationActor Actor, string InitiatorType, Guid InitiatorId);
public sealed record IdentityPurgeCandidate(string InitiatorType, Guid InitiatorId, string DisplayName);
public sealed record ExecuteIdentityPurgeRequest(AdministrationActor Actor, string InitiatorType, Guid InitiatorId, string Confirmation);
public sealed record IdentityPurgeScopePreview(string InitiatorType, Guid InitiatorId, string DisplayName, bool IsEligible, string? BlockingReason, int Persons, int DirectoryAccounts, int PersonAccountLinks, int TargetGroups, int GroupPolicies, int DirectEntitlements, int MembershipRequests, int MembershipRequestStatusHistory, int MfaTransactions, int TotpFactors, int TotpUsedTimeSteps, int Fido2Credentials, string CanonicalScopeSummary);

public sealed record ManagedOrphanedSecondFactorRequest(Guid RequestId, string PersonDisplayName, string TargetAccountDisplayName, string TargetGroupDisplayName, DateTimeOffset CreatedUtc, DateTimeOffset TransactionExpiresUtc);
public sealed record ExpireOrphanedSecondFactorRequestRequest(AdministrationActor Actor, Guid RequestId);

public sealed record ManagedOrphanedApprovalRequest(Guid RequestId, string PersonDisplayName, string TargetAccountDisplayName, string TargetGroupDisplayName, DateTimeOffset CreatedUtc, DateTimeOffset EnteredAwaitingApprovalUtc);
public sealed record ExpireOrphanedApprovalRequestRequest(AdministrationActor Actor, Guid RequestId);

/// <summary>read-only diagnostic status of the currently configured TOTP secret-protection certificate. <see cref="Found"/> false means the configured thumbprint has no matching certificate in LocalMachine\My.</summary>
public sealed record TotpProtectionCertificateStatus(bool Found, string Thumbprint, DateTimeOffset? NotBefore, DateTimeOffset? NotAfter, bool PrivateKeyAccessible, int ActiveFactorsProtected, int TotalFactorsProtected);

/// <summary>one-time, transactional re-encryption of every persisted TOTP secret from the currently configured (outgoing) certificate to <see cref="IncomingCertificateThumbprint"/>. <see cref="Confirmation"/> must equal <c>"ROTATE {IncomingCertificateThumbprint}"</c>, exactly, mirroring the identity-purge confirmation pattern.</summary>
public sealed record RotateTotpProtectionCertificateRequest(AdministrationActor Actor, string IncomingCertificateThumbprint, string Confirmation);

public enum SmtpTlsMode { None, Implicit, Explicit }

/// <summary><see cref="HasPassword"/> reports whether a protected SMTP password is stored, without ever returning the password itself. <see cref="Configured"/> false means no mail settings row exists yet. <see cref="IsEnabled"/> is the global kill switch for the entire mail-notification feature - when false, every notification type (group, requester, approval pending/decision) keeps enqueuing normally but delivery is paused until re-enabled.</summary>
public sealed record MailSettingsSnapshot(bool Configured, bool IsEnabled, string SmtpHost, int SmtpPort, string SenderAddress, string? Username, bool HasPassword, SmtpTlsMode TlsMode, string RowVersion);

/// <summary><see cref="NewPassword"/> null/empty leaves an existing stored password unchanged; set <see cref="ClearPassword"/> to remove it (e.g. for an unauthenticated relay). <see cref="RowVersion"/> is required except for the very first save (no row exists yet).</summary>
public sealed record UpsertMailSettingsRequest(AdministrationActor Actor, bool IsEnabled, string SmtpHost, int SmtpPort, string SenderAddress, string? Username, string? NewPassword, bool ClearPassword, SmtpTlsMode TlsMode, string? RowVersion);

public sealed record TestMailSettingsRequest(AdministrationActor Actor, string RecipientAddress);

/// <summary>Aggregate view of <c>MailNotificationOutbox</c> for the admin UI - counts only, never message bodies. <see cref="FailedCount"/> is the subset of <see cref="PendingCount"/> that has recorded at least one failed delivery attempt (<c>LastFailureMessage</c> set).</summary>
public sealed record MailNotificationOutboxStatus(int PendingCount, int FailedCount);

/// <summary>Permanently discards every currently pending (undelivered) <c>MailNotificationOutbox</c> row - e.g. after a long mail-sending outage, so a backlog of now-stale notifications is not sent all at once when re-enabled. Already-delivered rows are never touched.</summary>
public sealed record PurgeMailNotificationOutboxRequest(AdministrationActor Actor);

/// <summary>Global Cc/Bcc policy applied to every requester outcome notification (the email sent to the person
/// who submitted a membership request), distinct from the SMTP transport settings above. <see cref="Configured"/>
/// false means no row exists yet (both addresses treated as unset).</summary>
public sealed record RequesterNotificationSettingsSnapshot(bool Configured, string? CcAddress, string? BccAddress, string RowVersion);
public sealed record UpsertRequesterNotificationSettingsRequest(AdministrationActor Actor, string? CcAddress, string? BccAddress, string? RowVersion);

public sealed record MailSettingsTestResult(bool Succeeded, string? ErrorMessage);

/// <summary>One row of the admin certificate overview. <see cref="IsLiveCheck"/> true means <see cref="ObservedUtc"/> is effectively "now" (checked directly against LocalMachine\My or SQL on this request); false means it reflects the last actual API-to-Worker mTLS handshake, which can be stale during quiet periods. <see cref="WasAccepted"/> is only meaningful (non-null) for the Worker server certificate row.</summary>
public sealed record MonitoredCertificateStatus(string Label, bool Found, string Thumbprint, DateTimeOffset? NotBefore, DateTimeOffset? NotAfter, bool IsLiveCheck, DateTimeOffset? ObservedUtc, bool? WasAccepted);

public sealed record CertificateOverview(IReadOnlyList<MonitoredCertificateStatus> Certificates);

/// <summary><see cref="NotificationEmailOverride"/> takes precedence over the AD-sourced email of the person's linked account when set; the AD value is used only as a fallback when this is empty.</summary>
public sealed record ManagedPerson(Guid PersonId, string DisplayName, bool IsActive, DateTimeOffset ValidFromUtc, DateTimeOffset? ValidUntilUtc, string? NotificationEmailOverride, string RowVersion);
public sealed record DeactivatePersonRequest(AdministrationActor Actor, Guid PersonId, string RowVersion);
public sealed record ReactivatePersonRequest(AdministrationActor Actor, Guid PersonId, string RowVersion);
/// <summary><see cref="NotificationEmailOverride"/> null/blank clears the override.</summary>
public sealed record SetPersonNotificationEmailOverrideRequest(AdministrationActor Actor, Guid PersonId, string? NotificationEmailOverride, string RowVersion);
public sealed record SearchDirectoryUsersRequest(AdministrationActor Actor, string SearchTerm);
public sealed record DirectoryUserSearchResult(Guid ObjectGuid, string DisplayName, string DomainQualifiedName, string SamAccountName, string? UserPrincipalName, string? EmailAddress);
public sealed record CreatePersonFromDirectoryAccountRequest(AdministrationActor Actor, Guid ObjectGuid);
public sealed record ManagedPersonAccountLink(
    Guid PersonAccountLinkId,
    Guid AccountId,
    string DisplayName,
    string DomainQualifiedName,
    string? UserPrincipalName,
    string? EmailAddress,
    bool IsEnabledInDirectory,
    bool IsActive,
    bool MayAuthenticate,
    bool MayReceivePrivileges,
    bool MayApprove,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset? ValidUntilUtc,
    string RowVersion);
public sealed record ManagedPersonDetail(ManagedPerson Person, IReadOnlyList<ManagedPersonAccountLink> Accounts);
public sealed record CreatePersonAccountLinkRequest(AdministrationActor Actor, Guid PersonId, Guid ObjectGuid, bool MayAuthenticate, bool MayReceivePrivileges, bool MayApprove);
public sealed record UpdatePersonAccountLinkPurposesRequest(AdministrationActor Actor, Guid PersonId, Guid PersonAccountLinkId, bool MayAuthenticate, bool MayReceivePrivileges, bool MayApprove, string RowVersion);
public sealed record DeactivatePersonAccountLinkRequest(AdministrationActor Actor, Guid PersonId, Guid PersonAccountLinkId, string RowVersion);
public sealed record ReactivatePersonAccountLinkRequest(AdministrationActor Actor, Guid PersonId, Guid PersonAccountLinkId, string RowVersion);

/// <summary>Never carries EncryptedSecret/ProtectionKeyId - those are verification-path only.</summary>
public sealed record ManagedTotpFactor(Guid TotpFactorId, Guid PersonId, DateTimeOffset EnrolledUtc, DateTimeOffset? ConfirmedUtc, bool IsActive, DateTimeOffset? LockedUntilUtc, DateTimeOffset? RevokedUtc, string? RevokedBy, string RowVersion);
public sealed record RevokeTotpFactorRequest(AdministrationActor Actor, Guid PersonId, Guid TotpFactorId, string RowVersion);

/// <summary>Never carries PublicKey/CredentialId - those are verification-path only.</summary>
public sealed record ManagedFido2Credential(Guid Fido2CredentialId, Guid PersonId, string? Label, string? Aaguid, DateTimeOffset CreatedUtc, DateTimeOffset? RevokedUtc, string? RevokedBy, string RowVersion);
public sealed record RevokeFido2CredentialRequest(AdministrationActor Actor, Guid PersonId, Guid Fido2CredentialId, string RowVersion);

public sealed record ManagedTargetGroup(
    Guid TargetGroupId,
    string DisplayName,
    string DomainQualifiedName,
    bool IsEnabledForRequests,
    long MinimumTtlSeconds,
    long MaximumTtlSeconds,
    long DefaultTtlSeconds,
    long AllowedTtlStepSeconds,
    bool RequiresSecondFactor,
    int AllowedSecondFactorTypes,
    bool RequiresTicket,
    bool RequiresApproval,
    bool PolicyIsActive,
    string RowVersion);

public sealed record ManagedGroupApprover(
    Guid GroupApproverId,
    Guid TargetGroupId,
    Guid PersonId,
    string PersonDisplayName,
    bool IsActive,
    bool NotifyByEmail,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset? ValidUntilUtc,
    string RowVersion);

public sealed record QueryGroupApproversRequest(AdministrationActor Actor, Guid TargetGroupId);
public sealed record AddGroupApproverRequest(AdministrationActor Actor, Guid TargetGroupId, Guid PersonId);
public sealed record DeactivateGroupApproverRequest(AdministrationActor Actor, Guid TargetGroupId, Guid GroupApproverId, string RowVersion);
public sealed record UpdateGroupApproverNotificationPreferenceRequest(AdministrationActor Actor, Guid TargetGroupId, Guid GroupApproverId, bool NotifyByEmail, string RowVersion);

public sealed record ManagedTicketReferencePattern(Guid TicketReferencePatternId, Guid TargetGroupId, string TargetGroupDisplayName, string Label, string Expression, bool IsActive, string RowVersion);
public sealed record UpsertTicketReferencePatternRequest(AdministrationActor Actor, Guid TicketReferencePatternId, Guid TargetGroupId, string Label, string Expression, bool IsActive, string? RowVersion);
public sealed record DeleteTicketReferencePatternRequest(AdministrationActor Actor, Guid TicketReferencePatternId, string RowVersion);

/// <summary>Which message header the recipient's address is placed in when a notification is sent.</summary>
public enum MailRecipientType { To, Cc, Bcc }

public sealed record ManagedGroupNotificationRecipient(Guid GroupNotificationRecipientId, Guid TargetGroupId, string TargetGroupDisplayName, string EmailAddress, MailRecipientType RecipientType, bool IsActive, string RowVersion);
public sealed record UpsertGroupNotificationRecipientRequest(AdministrationActor Actor, Guid GroupNotificationRecipientId, Guid TargetGroupId, string EmailAddress, MailRecipientType RecipientType, bool IsActive, string? RowVersion);
public sealed record DeleteGroupNotificationRecipientRequest(AdministrationActor Actor, Guid GroupNotificationRecipientId, string RowVersion);

/// <summary><see cref="RowVersion"/> is null when the template has never been saved yet (first save creates the row).</summary>
public sealed record NotificationTemplateSnapshot(string TemplateKey, string Subject, string Body, string? RowVersion);
public sealed record QueryNotificationTemplateRequest(AdministrationActor Actor, string TemplateKey);
public sealed record UpsertNotificationTemplateRequest(AdministrationActor Actor, string TemplateKey, string Subject, string Body, string? RowVersion);

public sealed record UpdateTargetGroupPolicyRequest(
    AdministrationActor Actor,
    Guid TargetGroupId,
    bool IsEnabledForRequests,
    long MinimumTtlSeconds,
    long MaximumTtlSeconds,
    long DefaultTtlSeconds,
    long AllowedTtlStepSeconds,
    bool RequiresSecondFactor,
    int AllowedSecondFactorTypes,
    bool RequiresTicket,
    bool RequiresApproval,
    bool PolicyIsActive,
    string RowVersion);

public sealed record CreateTargetGroupRequest(
    AdministrationActor Actor,
    Guid ObjectGuid,
    long MinimumTtlSeconds,
    long MaximumTtlSeconds,
    long DefaultTtlSeconds,
    long AllowedTtlStepSeconds,
    bool RequiresSecondFactor,
    int AllowedSecondFactorTypes,
    bool RequiresTicket,
    bool RequiresApproval);

public sealed record SearchDirectoryGroupsRequest(AdministrationActor Actor, string SearchTerm);
public sealed record DirectoryGroupSearchResult(Guid ObjectGuid, string DisplayName, string DomainQualifiedName);

public sealed record DeactivateTargetGroupRequest(
    AdministrationActor Actor,
    Guid TargetGroupId,
    string RowVersion);

public sealed record ReactivateTargetGroupRequest(
    AdministrationActor Actor,
    Guid TargetGroupId,
    string RowVersion);

public sealed record ManagedDirectEntitlement(
    Guid EntitlementId,
    Guid PersonId,
    string PersonDisplayName,
    Guid TargetAccountId,
    string TargetAccountDisplayName,
    Guid TargetGroupId,
    string TargetGroupDisplayName,
    bool IsActive,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset? ValidUntilUtc,
    long? MinimumTtlSeconds,
    long? MaximumTtlSeconds,
    long? AllowedTtlStepSeconds,
    bool? RequiresSecondFactor,
    bool? RequiresTicket,
    bool? RequiresApproval,
    string RowVersion);

public sealed record DeactivateDirectEntitlementRequest(
    AdministrationActor Actor,
    Guid EntitlementId,
    string RowVersion);

public sealed record EntitlementSubject(
    Guid PersonId,
    string PersonDisplayName,
    Guid TargetAccountId,
    string TargetAccountDisplayName);

public sealed record CreateDirectEntitlementRequest(
    AdministrationActor Actor,
    Guid PersonId,
    Guid TargetAccountId,
    Guid TargetGroupId,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset? ValidUntilUtc);

public sealed record UpdateDirectEntitlementValidityRequest(
    AdministrationActor Actor,
    Guid EntitlementId,
    DateTimeOffset? ValidUntilUtc,
    string RowVersion);

public sealed record UpdateDirectEntitlementConstraintsRequest(
    AdministrationActor Actor,
    Guid EntitlementId,
    long? MinimumTtlSeconds,
    long? MaximumTtlSeconds,
    long? AllowedTtlStepSeconds,
    bool? RequiresSecondFactor,
    bool? RequiresTicket,
    bool? RequiresApproval,
    string RowVersion);

public sealed record QueryAuditLogRequest(
    AdministrationActor Actor,
    int PageNumber,
    int PageSize,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    string? EventType,
    string? Result,
    Guid? CorrelationId,
    string? ActorAccount);

public sealed record AuditLogEntry(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredUtc,
    string? PersonDisplayNameSnapshot,
    string? ActorAccountDisplayNameSnapshot,
    string? TargetAccountDisplayNameSnapshot,
    string? TargetGroupDisplayNameSnapshot,
    string SourceComponent,
    string? SourceIpAddress,
    string? ClientSourceIpAddress,
    Guid CorrelationId,
    Guid? RequestId,
    long? RequestedTtlSeconds,
    string Result,
    string? FailureCategory,
    string AuthenticationMethod,
    string FrontendClientId);

public sealed record AuditLogPage(
    IReadOnlyList<AuditLogEntry> Items,
    int TotalCount,
    IReadOnlyList<string> AvailableEventTypes,
    IReadOnlyList<string> AvailableResults,
    IReadOnlyList<string> AvailableActorAccounts);

public sealed record QueryTechnicalErrorLogRequest(
    AdministrationActor Actor,
    int PageNumber,
    int PageSize,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    Guid? RequestId,
    Guid? CorrelationId);

public sealed record TechnicalErrorLogEntry(
    Guid ErrorId,
    DateTimeOffset OccurredUtc,
    Guid? RequestId,
    Guid? CorrelationId,
    string HttpMethod,
    string Path,
    int? StatusCode,
    string ExceptionType,
    string Message,
    string? StackTrace,
    string SourceComponent);

public sealed record TechnicalErrorLogPage(IReadOnlyList<TechnicalErrorLogEntry> Items, int TotalCount);

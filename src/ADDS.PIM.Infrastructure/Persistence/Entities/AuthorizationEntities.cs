using ADDS.PIM.Domain.Security;

namespace ADDS.PIM.Infrastructure.Persistence.Entities;

public sealed class DirectoryScopeEntity
{
    public Guid DirectoryScopeId { get; set; }
    public required string StableScopeIdentifier { get; set; }
    public required string DisplayName { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ModifiedUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class PersonEntity
{
    public Guid PersonId { get; set; }
    public required string DisplayName { get; set; }
    public string? ExternalReference { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset ValidFromUtc { get; set; }
    public DateTimeOffset? ValidUntilUtc { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ModifiedUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class DirectoryAccountEntity
{
    public Guid AccountId { get; set; }
    public Guid DirectoryScopeId { get; set; }
    public Guid ObjectGuid { get; set; }
    public string? ObjectSid { get; set; }
    public required string SamAccountName { get; set; }
    public string? UserPrincipalName { get; set; }
    public string? EmailAddress { get; set; }
    public required string DistinguishedName { get; set; }
    public required string DomainQualifiedName { get; set; }
    public required string DisplayName { get; set; }
    public bool IsEnabledInDirectory { get; set; }
    public bool IsWithinAllowedScope { get; set; }
    public DateTimeOffset LastVerifiedUtc { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ModifiedUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class PersonAccountLinkEntity
{
    public Guid PersonAccountLinkId { get; set; }
    public Guid PersonId { get; set; }
    public Guid AccountId { get; set; }
    public bool MayAuthenticate { get; set; }
    public bool MayReceivePrivileges { get; set; }
    public bool MayApprove { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset ValidFromUtc { get; set; }
    public DateTimeOffset? ValidUntilUtc { get; set; }
    public required string CreatedBy { get; set; }
    public required string ModifiedBy { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ModifiedUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class GroupPolicyEntity
{
    public Guid GroupPolicyId { get; set; }
    public long MinimumTtlSeconds { get; set; }
    public long MaximumTtlSeconds { get; set; }
    public long DefaultTtlSeconds { get; set; }
    public long AllowedTtlStepSeconds { get; set; }
    public bool RequiresSecondFactor { get; set; }
    public SecondFactorType AllowedSecondFactorTypes { get; set; }
    public bool RequiresTicket { get; set; }
    public bool RequiresApproval { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ModifiedUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class TicketReferencePatternEntity
{
    public Guid TicketReferencePatternId { get; set; }
    public Guid GroupPolicyId { get; set; }
    public required string Label { get; set; }
    public required string Expression { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ModifiedUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class TargetGroupEntity
{
    public Guid TargetGroupId { get; set; }
    public Guid DirectoryScopeId { get; set; }
    public Guid ObjectGuid { get; set; }
    public string? ObjectSid { get; set; }
    public required string SamAccountName { get; set; }
    public required string DistinguishedName { get; set; }
    public required string DomainQualifiedName { get; set; }
    public required string DisplayName { get; set; }
    public Guid GroupPolicyId { get; set; }
    public bool IsEnabledForRequests { get; set; }
    public bool IsWithinAllowedScope { get; set; }
    public DateTimeOffset LastVerifiedUtc { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ModifiedUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class GroupApproverEntity
{
    public Guid GroupApproverId { get; set; }
    public Guid TargetGroupId { get; set; }
    public Guid PersonId { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset ValidFromUtc { get; set; }
    public DateTimeOffset? ValidUntilUtc { get; set; }
    public required string CreatedBy { get; set; }
    public required string ModifiedBy { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ModifiedUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class DirectEntitlementEntity
{
    public Guid EntitlementId { get; set; }
    public Guid PersonId { get; set; }
    public Guid TargetAccountId { get; set; }
    public Guid TargetGroupId { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset ValidFromUtc { get; set; }
    public DateTimeOffset? ValidUntilUtc { get; set; }
    public long? MinimumTtlSeconds { get; set; }
    public long? MaximumTtlSeconds { get; set; }
    public long? AllowedTtlStepSeconds { get; set; }
    public bool? RequiresSecondFactor { get; set; }
    public bool? RequiresTicket { get; set; }
    public bool? RequiresApproval { get; set; }
    public required string CreatedBy { get; set; }
    public required string ModifiedBy { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ModifiedUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

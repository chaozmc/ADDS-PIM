namespace ADDS.PIM.Contracts.MembershipRequests.V1;

public sealed record QueryMyAvailableEntitlements(Guid ActorDirectoryScopeId, Guid ActorObjectGuid);

public sealed record MyAvailableMembershipEntitlement(Guid EntitlementId, Guid TargetAccountId, Guid TargetGroupId, string TargetAccountDisplayName, string TargetGroupDisplayName, long MinimumTtlSeconds, long MaximumTtlSeconds, long DefaultTtlSeconds, long TtlStepSeconds, bool RequiresSecondFactor, bool RequiresApproval, bool RequiresTicket, int AllowedSecondFactorTypes);

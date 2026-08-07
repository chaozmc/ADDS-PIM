namespace ADDS.PIM.Contracts.Prototype;

public sealed record PrototypeMembershipRequestAcceptedResponse(
    Guid RequestId,
    DateTimeOffset ReceivedAtUtc,
    string Status);

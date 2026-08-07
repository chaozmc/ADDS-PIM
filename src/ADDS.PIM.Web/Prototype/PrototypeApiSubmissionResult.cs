namespace ADDS.PIM.Web.Prototype;

public sealed record PrototypeApiSubmissionResult(
    bool Accepted,
    Guid? RequestId,
    DateTimeOffset? ReceivedAtUtc,
    string Message);

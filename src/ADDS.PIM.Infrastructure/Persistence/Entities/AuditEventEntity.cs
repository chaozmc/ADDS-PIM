namespace ADDS.PIM.Infrastructure.Persistence.Entities;

public sealed class AuditEventEntity
{
    public Guid EventId { get; set; }

    public required string EventType { get; set; }

    public DateTimeOffset OccurredUtc { get; set; }

    public Guid? PersonId { get; set; }

    public Guid? ActorAccountId { get; set; }

    public Guid? TargetAccountId { get; set; }

    public string? PersonDisplayNameSnapshot { get; set; }

    public string? ActorAccountDisplayNameSnapshot { get; set; }

    public string? TargetAccountDisplayNameSnapshot { get; set; }

    public string? TargetGroupDisplayNameSnapshot { get; set; }

    public required string FrontendClientId { get; set; }

    public string? SourceIpAddress { get; set; }

    /// <summary>
    /// The browser's own remote IP as captured by Web on the incoming request, forwarded to the API
    /// via the (unsigned, informational-only) X-Client-Ip header - distinct from
    /// <see cref="SourceIpAddress"/>, which is always the Web frontend instance's own IP as seen by the
    /// API (the two are only ever equal when Web and the browser are the same host).
    /// </summary>
    public string? ClientSourceIpAddress { get; set; }

    public required string SourceComponent { get; set; }

    public Guid CorrelationId { get; set; }

    public Guid? RequestId { get; set; }

    public Guid? TargetGroupId { get; set; }

    public long? RequestedTtlSeconds { get; set; }

    public required string Result { get; set; }

    public string? FailureCategory { get; set; }

    public required string AuthenticationMethod { get; set; }

    public required string PolicyRequirementsSummary { get; set; }
}

namespace ADDS.PIM.Infrastructure.Persistence.Entities;

public sealed class TechnicalErrorLogEntryEntity
{
    public Guid ErrorId { get; set; }

    public DateTimeOffset OccurredUtc { get; set; }

    public Guid? RequestId { get; set; }

    public Guid? CorrelationId { get; set; }

    public required string HttpMethod { get; set; }

    public required string Path { get; set; }

    public int? StatusCode { get; set; }

    public required string ExceptionType { get; set; }

    public required string Message { get; set; }

    public string? StackTrace { get; set; }

    public required string SourceComponent { get; set; }
}

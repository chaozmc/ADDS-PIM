namespace ADDS.PIM.Web.Prototype;

public sealed record PrototypeGroup(
    Guid Id,
    string Name,
    string Description,
    string SecurityClass,
    string RequiredFactor,
    IReadOnlyList<long> AllowedTtlSeconds,
    long DefaultTtlSeconds);

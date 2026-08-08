namespace ADDS.PIM.AdWorker.ActiveDirectory;

public sealed class ActiveDirectoryOptions
{
    public const string SectionName = "ActiveDirectory";
    public required string DomainController { get; init; }
}

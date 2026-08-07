namespace ADDS.PIM.Web.Prototype;

/// <summary>
/// Demonstrates the UI flow without calling the API, database, or Active Directory.
/// </summary>
public sealed class PrototypeRequestSimulator(TimeProvider timeProvider) : IPrototypeRequestSimulator
{
    public PrototypeSubmissionResult Simulate(PrototypeGroup group, PrototypeRequestForm form)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(form);

        return new PrototypeSubmissionResult(Guid.NewGuid(), timeProvider.GetUtcNow());
    }
}

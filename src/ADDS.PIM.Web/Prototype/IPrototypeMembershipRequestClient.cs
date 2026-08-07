namespace ADDS.PIM.Web.Prototype;

public interface IPrototypeMembershipRequestClient
{
    Task<PrototypeApiSubmissionResult> SubmitAsync(
        PrototypeGroup group,
        PrototypeRequestForm form,
        CancellationToken cancellationToken);
}

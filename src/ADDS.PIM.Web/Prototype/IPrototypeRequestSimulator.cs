namespace ADDS.PIM.Web.Prototype;

public interface IPrototypeRequestSimulator
{
    PrototypeSubmissionResult Simulate(PrototypeGroup group, PrototypeRequestForm form);
}

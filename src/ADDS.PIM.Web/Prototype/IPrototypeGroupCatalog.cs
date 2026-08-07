namespace ADDS.PIM.Web.Prototype;

public interface IPrototypeGroupCatalog
{
    IReadOnlyList<PrototypeGroup> GetRequestableGroups();

    PrototypeGroup? FindById(Guid id);
}

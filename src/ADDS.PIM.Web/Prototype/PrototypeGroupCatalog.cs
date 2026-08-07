namespace ADDS.PIM.Web.Prototype;

public sealed class PrototypeGroupCatalog : IPrototypeGroupCatalog
{
    private static readonly IReadOnlyList<PrototypeGroup> Groups =
    [
        new(
            Guid.Parse("b310ef94-47ee-4e13-94db-cfc09834e953"),
            "SG-Server-Operations",
            "Zeitlich begrenzter administrativer Zugriff für Server-Betrieb und Incident-Bearbeitung.",
            "Erhöht",
            "Erneute Bestätigung vor Ausführung",
            [900, 1800, 3600, 7200],
            3600),
        new(
            Guid.Parse("e0cdf5c7-60fa-4ec0-a22c-9e22ab80ea73"),
            "SG-Database-Support",
            "Unterstützung für geplante Wartungen und Störungsbehebungen an Datenbankdiensten.",
            "Standard",
            "Keine zusätzliche Bestätigung im Prototyp",
            [900, 1800, 3600],
            1800),
        new(
            Guid.Parse("a54800df-001f-42c7-b1ae-7fe4ad1b9d70"),
            "SG-PKI-Operations",
            "Operativer Zugriff auf freigegebene PKI-Betriebsaufgaben.",
            "Hoch",
            "Phishing-resistente Bestätigung erforderlich",
            [900, 1800],
            900)
    ];

    public IReadOnlyList<PrototypeGroup> GetRequestableGroups() => Groups;

    public PrototypeGroup? FindById(Guid id) => Groups.SingleOrDefault(group => group.Id == id);
}

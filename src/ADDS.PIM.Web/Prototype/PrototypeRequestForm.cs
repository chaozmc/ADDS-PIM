using System.ComponentModel.DataAnnotations;

namespace ADDS.PIM.Web.Prototype;

public sealed class PrototypeRequestForm
{
    [Range(1, long.MaxValue, ErrorMessage = "Bitte wählen Sie eine zulässige Dauer.")]
    public long RequestedTtlSeconds { get; set; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "Eine Begründung ist erforderlich.")]
    [StringLength(2000, ErrorMessage = "Die Begründung darf höchstens 2.000 Zeichen enthalten.")]
    public string Reason { get; set; } = string.Empty;

    public string? TicketReference { get; set; }
}

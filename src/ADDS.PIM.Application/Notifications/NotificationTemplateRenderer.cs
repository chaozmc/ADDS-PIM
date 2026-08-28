namespace ADDS.PIM.Application.Notifications;

/// <summary>Minimal <c>{PlaceholderName}</c> token replacement for notification-template subject/body text - no
/// templating library, since a flat set of known placeholders is all any template currently needs. A placeholder
/// with no matching entry in <paramref name="placeholders"/> is left in the output untouched, so a typo in an
/// admin-edited template is visible rather than silently swallowed.</summary>
public static class NotificationTemplateRenderer
{
    public static string Render(string template, IReadOnlyDictionary<string, string> placeholders)
    {
        var rendered = template;
        foreach (var (name, value) in placeholders)
        {
            rendered = rendered.Replace($"{{{name}}}", value);
        }
        return rendered;
    }
}

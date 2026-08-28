using ADDS.PIM.Application.Notifications;

namespace ADDS.PIM.Application.Tests.Notifications;

public sealed class NotificationTemplateRendererTests
{
    [Fact]
    public void Render_ReplacesKnownPlaceholders()
    {
        var result = NotificationTemplateRenderer.Render(
            "Hello {PersonDisplayName}, group {TargetGroupDisplayName} is {StatusText}.",
            new Dictionary<string, string> { ["PersonDisplayName"] = "Max Mustermann", ["TargetGroupDisplayName"] = "GRP-App01", ["StatusText"] = "Rejected" });

        Assert.Equal("Hello Max Mustermann, group GRP-App01 is Rejected.", result);
    }

    [Fact]
    public void Render_LeavesUnknownPlaceholdersUntouched()
    {
        var result = NotificationTemplateRenderer.Render("Value: {SomeTypo}", new Dictionary<string, string> { ["Other"] = "x" });

        Assert.Equal("Value: {SomeTypo}", result);
    }

    [Fact]
    public void Render_DoesNotCorruptSimilarlyNamedPlaceholders()
    {
        var result = NotificationTemplateRenderer.Render("{Reason} vs {ReasonCode}", new Dictionary<string, string> { ["Reason"] = "because" });

        Assert.Equal("because vs {ReasonCode}", result);
    }

    [Fact]
    public void Render_ReplacesRepeatedPlaceholderEverywhere()
    {
        var result = NotificationTemplateRenderer.Render("{StatusText} - {StatusText}", new Dictionary<string, string> { ["StatusText"] = "Failed" });

        Assert.Equal("Failed - Failed", result);
    }
}

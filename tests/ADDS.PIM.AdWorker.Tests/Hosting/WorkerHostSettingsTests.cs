using ADDS.PIM.AdWorker.Hosting;

namespace ADDS.PIM.AdWorker.Tests.Hosting;

public sealed class WorkerHostSettingsTests
{
    [Fact]
    public void Create_Rejects_WildcardOrLoopbackBindAddress()
    {
        Assert.Throws<InvalidOperationException>(() => Create("0.0.0.0"));
        Assert.Throws<InvalidOperationException>(() => Create("127.0.0.1"));
    }

    [Fact]
    public void Create_NormalizesThumbprintsAndRetainsSpecificBinding()
    {
        var settings = Create("192.0.2.10", "aa bb cc", ["11 22 33"]);

        Assert.Equal("AABBCC", settings.ServerCertificateThumbprint);
        Assert.Contains("112233", settings.AllowedApiClientCertificateThumbprints);
        Assert.Equal("192.0.2.10", settings.BindAddress.ToString());
    }

    [Fact]
    public void TimeWindow_RejectsFutureAndExpiredCommands()
    {
        var now = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

        Assert.True(WorkerCommandTimeWindow.IsCurrent(now.AddSeconds(-300), now, 300));
        Assert.False(WorkerCommandTimeWindow.IsCurrent(now.AddSeconds(-301), now, 300));
        Assert.False(WorkerCommandTimeWindow.IsCurrent(now.AddSeconds(1), now, 300));
    }

    private static WorkerHostSettings Create(string bindAddress, string serverThumbprint = "AABBCC", string[]? allowedThumbprints = null)
        => WorkerHostSettings.Create(
            bindAddress,
            8443,
            serverThumbprint,
            allowedThumbprints ?? ["112233"],
            "Server=(local);Database=ADDS_PIM;Integrated Security=true;Encrypt=true;",
            "gdc.example.org",
            300);
}

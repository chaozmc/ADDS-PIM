using ADDS.PIM.Infrastructure.Worker;

namespace ADDS.PIM.Infrastructure.Tests.Worker;

public sealed class WorkerClientOptionsTests
{
    [Fact]
    public void GetEndpoint_AcceptsOnlyTheFixedPrivateWorkerOperation()
    {
        var options = new WorkerClientOptions
        {
            Endpoint = "https://pim-worker.example.org:8990/internal/v1/temporary-group-memberships"
        };

        Assert.Equal("https://pim-worker.example.org:8990/internal/v1/temporary-group-memberships", options.GetEndpoint().ToString());
        Assert.Throws<InvalidOperationException>(() => new WorkerClientOptions { Endpoint = "http://pim-worker.example.org:8990/internal/v1/temporary-group-memberships" }.GetEndpoint());
        Assert.Throws<InvalidOperationException>(() => new WorkerClientOptions { Endpoint = "https://pim-worker.example.org:8990/other" }.GetEndpoint());
        Assert.Throws<InvalidOperationException>(() => new WorkerClientOptions { Endpoint = "https://pim-worker.example.org:8990/internal/v1/temporary-group-memberships?redirect=elsewhere" }.GetEndpoint());
    }

    [Fact]
    public void GetExpectedServerCertificateThumbprints_NormalizesAndRequiresAnAllowlist()
    {
        var options = new WorkerClientOptions { ExpectedServerCertificateThumbprints = ["aa bb cc"] };

        Assert.Contains("AABBCC", options.GetExpectedServerCertificateThumbprints());
        Assert.Throws<InvalidOperationException>(() => new WorkerClientOptions().GetExpectedServerCertificateThumbprints());
    }
}

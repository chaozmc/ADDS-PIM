using ADDS.PIM.Application.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ADDS.PIM.Infrastructure.Worker;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPimWorkerClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WorkerClientOptions>().Bind(configuration.GetSection(WorkerClientOptions.SectionName));
        services.AddSingleton<WorkerClientCertificateProvider>();
        services.AddSingleton<WorkerServerCertificateObservationCache>();
        services.AddHttpClient<IWorkerMembershipClient, HttpsWorkerMembershipClient>(client => client.Timeout = TimeSpan.FromSeconds(45))
            .ConfigurePrimaryHttpMessageHandler(serviceProvider => HttpsWorkerMembershipClient.CreateHandler(
                serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<WorkerClientOptions>>().Value,
                serviceProvider.GetRequiredService<WorkerClientCertificateProvider>(),
                serviceProvider.GetRequiredService<WorkerServerCertificateObservationCache>(),
                serviceProvider.GetRequiredService<TimeProvider>()));
        return services;
    }
}

using ADDS.PIM.Application.Mfa;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ADDS.PIM.Infrastructure.Mfa;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPimTotpSecretProtection(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TotpSecretProtectionOptions>().Bind(configuration.GetSection(TotpSecretProtectionOptions.SectionName));
        services.AddSingleton<ITotpSecretProtector, CertificateTotpSecretProtector>();
        services.AddSingleton<ITotpSecretProtectorFactory, CertificateTotpSecretProtectorFactory>();
        return services;
    }
}

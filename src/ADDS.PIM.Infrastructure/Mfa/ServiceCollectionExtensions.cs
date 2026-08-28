using ADDS.PIM.Application.Security;
using ADDS.PIM.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ADDS.PIM.Infrastructure.Mfa;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPimTotpSecretProtection(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TotpSecretProtectionOptions>().Bind(configuration.GetSection(TotpSecretProtectionOptions.SectionName));
        services.AddSingleton<ICertificateSecretProtector, CertificateSecretProtector>();
        services.AddSingleton<ICertificateSecretProtectorFactory, CertificateSecretProtectorFactory>();
        return services;
    }
}

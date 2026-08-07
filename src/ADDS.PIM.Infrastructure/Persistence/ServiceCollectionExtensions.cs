using ADDS.PIM.Application.MembershipRequests;
using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Application.Worker;
using ADDS.PIM.Application.Mfa;
using ADDS.PIM.Application.Administration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ADDS.PIM.Infrastructure.Security;
using ADDS.PIM.Application.Security;
using Microsoft.Extensions.Configuration;
using ADDS.PIM.Application.Audit;
using ADDS.PIM.Infrastructure.Audit;
using ADDS.PIM.Application.Diagnostics;

namespace ADDS.PIM.Infrastructure.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPimSqlServerPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<PimDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IMembershipRequestCreationStore, EfMembershipRequestCreationStore>();
        services.AddScoped<IMyMembershipRequestHistoryStore, EfMyMembershipRequestHistoryStore>();
        services.AddScoped<IMembershipRequestStateStore, EfMembershipRequestStateStore>();
        services.AddScoped<IMembershipAuthorizationDataSource, EfMembershipAuthorizationDataSource>();
        services.AddScoped<ITicketReferencePolicySource, EfMembershipAuthorizationDataSource>();
        services.AddScoped<IWorkerCommandStore, EfWorkerCommandStore>();
        services.AddScoped<IApiRequestReplayStore, EfApiRequestReplayStore>();
        services.AddScoped<IWebSigningCertificateResolver, EfWebSigningCertificateResolver>();
        services.AddScoped<IAdministrationDataStore, EfAdministrationDataStore>();
        services.AddScoped<ITotpFactorEnrollmentStore, EfTotpFactorEnrollmentStore>();
        services.AddScoped<ITotpEnrollmentConfirmationStore, EfTotpEnrollmentConfirmationStore>();
        services.AddScoped<ICreateMfaTransactionStore, EfCreateMfaTransactionStore>();
        services.AddScoped<IMfaTransactionStore, EfMfaTransactionStore>();
        services.AddScoped<ITotpVerificationStore, EfTotpVerificationStore>();
        services.AddScoped<IMfaStatusStore, EfMfaStatusStore>();
        services.AddScoped<IPersonAccountLabelResolver, EfPersonAccountLabelResolver>();
        services.AddScoped<IFido2CredentialStore, EfFido2CredentialStore>();
        services.AddScoped<IFido2ChallengeStore, EfFido2ChallengeStore>();
        services.AddScoped<IFido2VerificationStore, EfFido2VerificationStore>();
        services.AddScoped<ADDS.PIM.Infrastructure.Mfa.Fido2WebAuthnCeremony>();
        services.AddScoped<IFido2RegistrationCeremony>(sp => sp.GetRequiredService<ADDS.PIM.Infrastructure.Mfa.Fido2WebAuthnCeremony>());
        services.AddScoped<IFido2AssertionCeremony>(sp => sp.GetRequiredService<ADDS.PIM.Infrastructure.Mfa.Fido2WebAuthnCeremony>());
        services.AddScoped<IDirectoryReconciliationStore, EfDirectoryReconciliationStore>();
        services.AddScoped<IAuditLogStore, EfAuditLogStore>();
        services.AddScoped<ITechnicalErrorLogStore, EfTechnicalErrorLogStore>();
        services.AddSingleton<IPurgeEventLog, WindowsPurgeEventLog>();
        services.AddScoped<IPurgeEventOutboxStore, EfPurgeEventOutboxStore>();
        services.AddScoped<IIdentityPurgeScopeStore, EfIdentityPurgeScopeStore>();
        services.AddScoped<IOrphanedSecondFactorRequestStore, EfOrphanedSecondFactorRequestStore>();
        services.AddScoped<IGroupApprovalAuthorizer, EfGroupApprovalAuthorizer>();
        services.AddScoped<IApprovalDataSource, EfApprovalDataSource>();
        services.AddScoped<IOrphanedApprovalRequestStore, EfOrphanedApprovalRequestStore>();
        services.AddScoped<ADDS.PIM.Application.Worker.IWorkerServerCertificateObservationStore, EfWorkerServerCertificateObservationStore>();
        return services;
    }
}

public static class ApplicationAccessServiceCollectionExtensions
{
    public static IServiceCollection AddPimApplicationAccess(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApplicationAccessOptions>().Bind(configuration.GetSection(ApplicationAccessOptions.SectionName)).Validate(options =>
        {
            try { options.Validate(); return true; } catch { return false; }
        }, "ApplicationAccess configuration is incomplete.").ValidateOnStart();
        services.AddScoped<IApplicationAccessAuthorizer, DirectoryGroupAccessAuthorizer>();
        services.AddScoped<IDirectoryGroupResolver, DirectoryGroupResolver>();
        services.AddScoped<IDirectoryAccountResolver, DirectoryAccountResolver>();
        services.AddScoped<IDirectoryReconciliationResolver, DirectoryReconciliationResolver>();
        return services;
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using ADDS.PIM.Application.Worker;
using ADDS.PIM.Contracts.Worker.V1;
using Microsoft.Extensions.Options;

namespace ADDS.PIM.Infrastructure.Worker;

internal sealed class HttpsWorkerMembershipClient(
    HttpClient httpClient,
    IOptions<WorkerClientOptions> options,
    TimeProvider timeProvider,
    WorkerServerCertificateObservationCache observationCache,
    IWorkerServerCertificateObservationStore observationStore) : IWorkerMembershipClient
{
    public async Task<WorkerMembershipDispatchResult> DispatchAsync(
        DispatchTemporaryGroupMembershipCommand request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        var endpoint = options.Value.GetEndpoint();
        var command = CreateCommand(request);

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(command)
        };

        try
        {
            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<TemporaryGroupMembershipResult>(cancellationToken);
                return result is null
                    ? new(WorkerMembershipDispatchKind.Rejected, null, (int)response.StatusCode, "WorkerResponseInvalid")
                    : new(WorkerMembershipDispatchKind.Completed, result, (int)response.StatusCode, null);
            }

            return new(WorkerMembershipDispatchKind.Rejected, null, (int)response.StatusCode, "WorkerRejectedCommand");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(WorkerMembershipDispatchKind.Timeout, null, null, "WorkerTimeout");
        }
        catch (HttpRequestException)
        {
            return new(WorkerMembershipDispatchKind.TransportFailure, null, null, "WorkerTransportFailure");
        }
        finally
        {
            // Best-effort admin-diagnostic capture (certificate overview) - deliberately never allowed to
            // affect the dispatch outcome computed above, which is why this is swallowed rather than surfaced.
            await PersistObservedServerCertificateAsync();
        }
    }

    private async Task PersistObservedServerCertificateAsync()
    {
        var observation = observationCache.TakeLatest();
        if (observation is null) return;
        try
        {
            await observationStore.RecordAsync(observation, CancellationToken.None);
        }
        catch
        {
            // Diagnostic cache only - a write failure here must never surface as a membership dispatch failure.
        }
    }

    internal static HttpMessageHandler CreateHandler(WorkerClientOptions options, WorkerClientCertificateProvider certificateProvider, WorkerServerCertificateObservationCache observationCache, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(certificateProvider);
        ArgumentNullException.ThrowIfNull(observationCache);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _ = options.GetEndpoint();
        var allowedServerThumbprints = options.GetExpectedServerCertificateThumbprints();

        var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(certificateProvider.LoadClientCertificate(options.ClientCertificateThumbprint ?? string.Empty));
        handler.ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
        {
            var accepted = errors == SslPolicyErrors.None
                && certificate is not null
                && allowedServerThumbprints.Contains(WorkerClientCertificateProvider.NormalizeThumbprint(certificate.GetCertHashString()));
            if (certificate is not null)
            {
                observationCache.Record(certificate, accepted, timeProvider.GetUtcNow());
            }

            return accepted;
        };
        return handler;
    }

    private TemporaryGroupMembershipCommand CreateCommand(DispatchTemporaryGroupMembershipCommand request)
    {
        var command = new TemporaryGroupMembershipCommand(
            TemporaryGroupMembershipCommand.CurrentVersion,
            Guid.NewGuid(),
            request.RequestId,
            request.CorrelationId,
            timeProvider.GetUtcNow(),
            CreateNonce(),
            request.DirectoryScopeId,
            request.TargetAccountObjectGuid,
            request.TargetGroupObjectGuid,
            request.RequestedTtlSeconds,
            string.Empty);
        return command with { CommandHash = TemporaryGroupMembershipCommandCanonicalizer.ComputeHash(command) };
    }

    private static void Validate(DispatchTemporaryGroupMembershipCommand request)
    {
        if (request.RequestId == Guid.Empty || request.CorrelationId == Guid.Empty || request.DirectoryScopeId == Guid.Empty
            || request.TargetAccountObjectGuid == Guid.Empty || request.TargetGroupObjectGuid == Guid.Empty || request.RequestedTtlSeconds <= 0)
        {
            throw new ArgumentException("The worker dispatch command is incomplete.", nameof(request));
        }
    }

    private static string CreateNonce()
        => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

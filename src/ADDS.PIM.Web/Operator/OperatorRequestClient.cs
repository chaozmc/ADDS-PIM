using System.Security.Cryptography.X509Certificates;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ADDS.PIM.Application.Security;
using ADDS.PIM.Contracts.MembershipRequests.V1;
using ADDS.PIM.Contracts.Mfa.V1;

namespace ADDS.PIM.Web.Operator;

/// <summary>
/// The Web signing key used to sign every Web-&gt;API request. Despite the historical section
/// name, this no longer carries any actor/test identity - the real actor is always resolved from the
/// current Kerberos session via <see cref="ICurrentPimActorContext"/>.
/// </summary>
public sealed class OperatorTestOptions
{
    public const string SectionName = "OperatorTest";
    public string? SigningCertificateThumbprint { get; init; }
    public string? SigningKeyId { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SigningCertificateThumbprint) || string.IsNullOrWhiteSpace(SigningKeyId))
            throw new InvalidOperationException("OperatorTest configuration is incomplete.");
    }
}

public interface IOperatorRequestClient
{
    Task<IReadOnlyList<MyAvailableMembershipEntitlement>> ListAvailableEntitlementsAsync(CancellationToken cancellationToken);
    Task<MembershipRequestSubmissionResult> SubmitAsync(Guid targetAccountId, Guid targetGroupId, long requestedTtlSeconds, string reason, string? ticketReference, CancellationToken cancellationToken);
    Task<MembershipRequestAccepted> VerifyTotpSecondFactorAsync(Guid requestId, string code, CancellationToken cancellationToken);
    /// <summary>Null when the person already has an active TOTP factor (no self-service re-enrollment).</summary>
    Task<TotpEnrollmentStarted?> EnrollTotpFactorAsync(CancellationToken cancellationToken);
    Task<bool> ConfirmTotpEnrollmentAsync(Guid totpFactorId, string code, CancellationToken cancellationToken);
    Task<MyMfaStatusResponse?> GetMfaStatusAsync(CancellationToken cancellationToken);

    Task<Fido2RegistrationOptionsResponse> StartFido2RegistrationAsync(Guid? stepUpChallengeId, string? stepUpTotpCode, string? stepUpAssertionResponseJson, CancellationToken cancellationToken);
    Task CompleteFido2RegistrationAsync(Guid challengeId, string attestationResponseJson, string? label, CancellationToken cancellationToken);
    Task<Fido2AssertionOptionsResponse> GetFido2AssertionOptionsAsync(Guid requestId, CancellationToken cancellationToken);
    Task<MembershipRequestAccepted> VerifyFido2SecondFactorAsync(Guid requestId, string assertionResponseJson, CancellationToken cancellationToken);

    Task<MyMembershipRequestsPage> ListMyRequestsAsync(
        int pageNumber,
        int pageSize,
        int? year,
        int? month,
        Guid? entitlementId,
        Guid? targetGroupId,
        CancellationToken cancellationToken);

    Task<bool> GetApproverEligibilityAsync(CancellationToken cancellationToken);
    Task<PendingApprovalsPage> ListPendingApprovalsAsync(int pageNumber, int pageSize, CancellationToken cancellationToken);
    Task<ApprovalDecisionAccepted> ApproveMembershipRequestAsync(Guid requestId, CancellationToken cancellationToken);
    Task<ApprovalDecisionAccepted> RejectMembershipRequestAsync(Guid requestId, string rejectionReason, CancellationToken cancellationToken);
}

/// <summary>Either an immediately accepted/executed request, or one now AwaitingSecondFactor pending TOTP/FIDO2 confirmation.</summary>
public sealed record MembershipRequestSubmissionResult(MembershipRequestAccepted? Accepted = null, MembershipRequestPendingSecondFactor? PendingSecondFactor = null);

/// <summary>Preserves the signed request identity when the API cannot return an accepted result.</summary>
public sealed class MembershipRequestSubmissionException(Guid requestId, Guid correlationId, HttpStatusCode? statusCode, string? problemTitle = null, Exception? innerException = null)
    : Exception("The membership request was not accepted.", innerException)
{
    public Guid RequestId { get; } = requestId;
    public Guid CorrelationId { get; } = correlationId;
    public HttpStatusCode? StatusCode { get; } = statusCode;
    public string? ProblemTitle { get; } = problemTitle;
}

public sealed class OperatorRequestClient(HttpClient httpClient, OperatorTestOptions options, ICurrentPimActorContext actorContext, IHttpContextAccessor httpContextAccessor) : IOperatorRequestClient
{
    public async Task<IReadOnlyList<MyAvailableMembershipEntitlement>> ListAvailableEntitlementsAsync(CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var payload = new QueryMyAvailableEntitlements(actor.DirectoryScopeId, actor.ObjectGuid);
        var body = JsonSerializer.SerializeToUtf8Bytes(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/membership-requests/mine/available-entitlements/query") { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new("application/json"); AddHeaders(request, Sign(HttpMethod.Post, "/api/v1/membership-requests/mine/available-entitlements/query", body));
        using var response = await httpClient.SendAsync(request, cancellationToken); response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<MyAvailableMembershipEntitlement>>(cancellationToken) ?? [];
    }

    public async Task<MembershipRequestSubmissionResult> SubmitAsync(Guid targetAccountId, Guid targetGroupId, long requestedTtlSeconds, string reason, string? ticketReference, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A reason is required.", nameof(reason));
        var actor = await RequireActorAsync(cancellationToken);
        var payload = new CreateMembershipRequest(actor.DirectoryScopeId, actor.ObjectGuid, targetAccountId, targetGroupId, requestedTtlSeconds, reason.Trim(), string.IsNullOrWhiteSpace(ticketReference) ? null : ticketReference.Trim());
        var body = JsonSerializer.SerializeToUtf8Bytes(payload);
        var signature = Sign(HttpMethod.Post, "/api/v1/membership-requests", body);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/membership-requests") { Content = new ByteArrayContent(body) };
            request.Content.Headers.ContentType = new("application/json");
            AddHeaders(request, signature);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var problemTitle = await TryReadProblemTitleAsync(response, cancellationToken);
                throw new MembershipRequestSubmissionException(signature.RequestId, signature.CorrelationId, response.StatusCode, problemTitle);
            }
            var element = await response.Content.ReadFromJsonAsync<JsonElement>(JsonSerializerOptions.Web, cancellationToken);
            if (element.TryGetProperty("status", out var statusProperty) && statusProperty.GetString() == "AwaitingSecondFactor")
            {
                var pending = element.Deserialize<MembershipRequestPendingSecondFactor>(JsonSerializerOptions.Web)
                    ?? throw new MembershipRequestSubmissionException(signature.RequestId, signature.CorrelationId, response.StatusCode);
                return new MembershipRequestSubmissionResult(PendingSecondFactor: pending);
            }
            var accepted = element.Deserialize<MembershipRequestAccepted>(JsonSerializerOptions.Web)
                ?? throw new MembershipRequestSubmissionException(signature.RequestId, signature.CorrelationId, response.StatusCode);
            return new MembershipRequestSubmissionResult(Accepted: accepted);
        }
        catch (MembershipRequestSubmissionException) { throw; }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new MembershipRequestSubmissionException(signature.RequestId, signature.CorrelationId, null, null, exception);
        }
    }

    public async Task<MembershipRequestAccepted> VerifyTotpSecondFactorAsync(Guid requestId, string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("A code is required.", nameof(code));
        var actor = await RequireActorAsync(cancellationToken);
        var path = $"/api/v1/membership-requests/{requestId:D}/second-factor/totp/verify";
        var payload = new VerifyTotpSecondFactorRequest(actor.DirectoryScopeId, actor.ObjectGuid, code.Trim());
        var body = JsonSerializer.SerializeToUtf8Bytes(payload);
        var signature = Sign(HttpMethod.Post, path, body);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(body) };
            request.Content.Headers.ContentType = new("application/json");
            AddHeaders(request, signature);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) throw new MembershipRequestSubmissionException(requestId, signature.CorrelationId, response.StatusCode);
            return await response.Content.ReadFromJsonAsync<MembershipRequestAccepted>(cancellationToken)
                ?? throw new MembershipRequestSubmissionException(requestId, signature.CorrelationId, response.StatusCode);
        }
        catch (MembershipRequestSubmissionException) { throw; }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new MembershipRequestSubmissionException(requestId, signature.CorrelationId, null, null, exception);
        }
    }

    public async Task<TotpEnrollmentStarted?> EnrollTotpFactorAsync(CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        const string path = "/api/v1/mfa/totp/enroll";
        var payload = new EnrollTotpFactorRequest(actor.DirectoryScopeId, actor.ObjectGuid);
        var body = JsonSerializer.SerializeToUtf8Bytes(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new("application/json");
        AddHeaders(request, Sign(HttpMethod.Post, path, body));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TotpEnrollmentStarted>(cancellationToken) ?? throw new InvalidOperationException("The API response was invalid.");
    }

    public async Task<bool> ConfirmTotpEnrollmentAsync(Guid totpFactorId, string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("A code is required.", nameof(code));
        var actor = await RequireActorAsync(cancellationToken);
        var path = $"/api/v1/mfa/totp/enroll/{totpFactorId:D}/confirm";
        var payload = new ConfirmTotpEnrollmentRequest(actor.DirectoryScopeId, actor.ObjectGuid, code.Trim());
        var body = JsonSerializer.SerializeToUtf8Bytes(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new("application/json");
        AddHeaders(request, Sign(HttpMethod.Post, path, body));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<MyMfaStatusResponse?> GetMfaStatusAsync(CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        const string path = "/api/v1/mfa/status/query";
        var payload = new QueryMyMfaStatus(actor.DirectoryScopeId, actor.ObjectGuid);
        var body = JsonSerializer.SerializeToUtf8Bytes(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new("application/json");
        AddHeaders(request, Sign(HttpMethod.Post, path, body));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Forbidden) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MyMfaStatusResponse>(cancellationToken);
    }

    public async Task<Fido2RegistrationOptionsResponse> StartFido2RegistrationAsync(Guid? stepUpChallengeId, string? stepUpTotpCode, string? stepUpAssertionResponseJson, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        const string path = "/api/v1/mfa/fido2/register/options";
        var payload = new StartFido2RegistrationRequest(actor.DirectoryScopeId, actor.ObjectGuid, stepUpChallengeId, stepUpTotpCode, stepUpAssertionResponseJson);
        var body = JsonSerializer.SerializeToUtf8Bytes(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new("application/json");
        AddHeaders(request, Sign(HttpMethod.Post, path, body));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Fido2RegistrationOptionsResponse>(cancellationToken) ?? throw new InvalidOperationException("The API response was invalid.");
    }

    public async Task CompleteFido2RegistrationAsync(Guid challengeId, string attestationResponseJson, string? label, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        const string path = "/api/v1/mfa/fido2/register/complete";
        var payload = new CompleteFido2RegistrationRequest(actor.DirectoryScopeId, actor.ObjectGuid, challengeId, attestationResponseJson, label);
        var body = JsonSerializer.SerializeToUtf8Bytes(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new("application/json");
        AddHeaders(request, Sign(HttpMethod.Post, path, body));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<Fido2AssertionOptionsResponse> GetFido2AssertionOptionsAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var path = $"/api/v1/membership-requests/{requestId:D}/second-factor/fido2/options";
        var payload = new Fido2AssertionOptionsQuery(actor.DirectoryScopeId, actor.ObjectGuid);
        var body = JsonSerializer.SerializeToUtf8Bytes(payload);
        var signature = Sign(HttpMethod.Post, path, body);
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new("application/json");
        AddHeaders(request, signature);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new MembershipRequestSubmissionException(requestId, signature.CorrelationId, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<Fido2AssertionOptionsResponse>(cancellationToken)
            ?? throw new MembershipRequestSubmissionException(requestId, signature.CorrelationId, response.StatusCode);
    }

    public async Task<MembershipRequestAccepted> VerifyFido2SecondFactorAsync(Guid requestId, string assertionResponseJson, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var path = $"/api/v1/membership-requests/{requestId:D}/second-factor/fido2/verify";
        var payload = new VerifyFido2SecondFactorRequest(actor.DirectoryScopeId, actor.ObjectGuid, assertionResponseJson);
        var body = JsonSerializer.SerializeToUtf8Bytes(payload);
        var signature = Sign(HttpMethod.Post, path, body);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(body) };
            request.Content.Headers.ContentType = new("application/json");
            AddHeaders(request, signature);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) throw new MembershipRequestSubmissionException(requestId, signature.CorrelationId, response.StatusCode);
            return await response.Content.ReadFromJsonAsync<MembershipRequestAccepted>(cancellationToken)
                ?? throw new MembershipRequestSubmissionException(requestId, signature.CorrelationId, response.StatusCode);
        }
        catch (MembershipRequestSubmissionException) { throw; }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new MembershipRequestSubmissionException(requestId, signature.CorrelationId, null, null, exception);
        }
    }

    public async Task<MyMembershipRequestsPage> ListMyRequestsAsync(
        int pageNumber,
        int pageSize,
        int? year,
        int? month,
        Guid? entitlementId,
        Guid? targetGroupId,
        CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var payload = new QueryMyMembershipRequests(actor.DirectoryScopeId, actor.ObjectGuid, pageNumber, pageSize, year, month, entitlementId, targetGroupId);
        var body = JsonSerializer.SerializeToUtf8Bytes(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/membership-requests/mine/query") { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new("application/json");
        AddHeaders(request, Sign(HttpMethod.Post, "/api/v1/membership-requests/mine/query", body));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MyMembershipRequestsPage>(cancellationToken) ?? throw new InvalidOperationException("The API response was invalid.");
    }

    public async Task<bool> GetApproverEligibilityAsync(CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        const string path = "/api/v1/membership-requests/pending-approvals/eligibility";
        var payload = new QueryApproverEligibility(actor.DirectoryScopeId, actor.ObjectGuid);
        var body = JsonSerializer.SerializeToUtf8Bytes(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new("application/json");
        AddHeaders(request, Sign(HttpMethod.Post, path, body));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return false;
        var result = await response.Content.ReadFromJsonAsync<ApproverEligibilityResponse>(cancellationToken);
        return result?.IsApprover ?? false;
    }

    public async Task<PendingApprovalsPage> ListPendingApprovalsAsync(int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        const string path = "/api/v1/membership-requests/pending-approvals/query";
        var payload = new QueryPendingApprovals(actor.DirectoryScopeId, actor.ObjectGuid, pageNumber, pageSize);
        var body = JsonSerializer.SerializeToUtf8Bytes(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new("application/json");
        AddHeaders(request, Sign(HttpMethod.Post, path, body));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PendingApprovalsPage>(cancellationToken) ?? throw new InvalidOperationException("The API response was invalid.");
    }

    public async Task<ApprovalDecisionAccepted> ApproveMembershipRequestAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(cancellationToken);
        var path = $"/api/v1/membership-requests/{requestId:D}/approve";
        var payload = new ApproveMembershipRequest(actor.DirectoryScopeId, actor.ObjectGuid);
        var body = JsonSerializer.SerializeToUtf8Bytes(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new("application/json");
        AddHeaders(request, Sign(HttpMethod.Post, path, body));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ApprovalDecisionAccepted>(cancellationToken) ?? throw new InvalidOperationException("The API response was invalid.");
    }

    public async Task<ApprovalDecisionAccepted> RejectMembershipRequestAsync(Guid requestId, string rejectionReason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rejectionReason)) throw new ArgumentException("A rejection reason is required.", nameof(rejectionReason));
        var actor = await RequireActorAsync(cancellationToken);
        var path = $"/api/v1/membership-requests/{requestId:D}/reject";
        var payload = new RejectMembershipRequest(actor.DirectoryScopeId, actor.ObjectGuid, rejectionReason.Trim());
        var body = JsonSerializer.SerializeToUtf8Bytes(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new("application/json");
        AddHeaders(request, Sign(HttpMethod.Post, path, body));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ApprovalDecisionAccepted>(cancellationToken) ?? throw new InvalidOperationException("The API response was invalid.");
    }

    private static async Task<string?> TryReadProblemTitleAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            return problem.TryGetProperty("title", out var title) ? title.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<ADDS.PIM.Application.Authorization.AuthenticatedDirectoryAccount> RequireActorAsync(CancellationToken cancellationToken)
        => await actorContext.ResolveAsync(cancellationToken) ?? throw new UnauthorizedAccessException("The authenticated Windows account could not be resolved.");

    private ApiRequestSignature Sign(HttpMethod method, string path, byte[] body)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        using var certificate = store.Certificates.Find(X509FindType.FindByThumbprint, options.SigningCertificateThumbprint!, false).OfType<X509Certificate2>().SingleOrDefault() ?? throw new InvalidOperationException("The configured Web signing certificate was not found.");
        if (!certificate.HasPrivateKey) throw new InvalidOperationException("The configured Web signing certificate private key is unavailable.");
        var unsigned = new ApiRequestSignature(ApiRequestSignature.CurrentVersion, options.SigningKeyId!, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_'), method.Method, path, string.Empty, ApiRequestCanonicalizer.ComputeBodyHash(body), "application/json", "1", string.Empty);
        return unsigned with { Signature = ApiRequestCanonicalizer.ComputeSignature(unsigned, certificate) };
    }

    private void AddHeaders(HttpRequestMessage request, ApiRequestSignature signature)
    {
        request.Headers.Add("X-Api-Signature-Version", signature.Version); request.Headers.Add("X-Api-Key-Id", signature.KeyId); request.Headers.Add("X-Request-Id", signature.RequestId.ToString("D")); request.Headers.Add("X-Correlation-Id", signature.CorrelationId.ToString("D")); request.Headers.Add("X-Issued-Utc", signature.IssuedUtc.UtcDateTime.ToString("O")); request.Headers.Add("X-Api-Nonce", signature.Nonce); request.Headers.Add("X-Api-Signature", signature.Signature);
        // Informational only, unsigned - the browser's own remote IP as Web itself saw it on the incoming
        // request. The API never talks to the browser directly, so this is the only place that IP is known.
        var clientIp = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrWhiteSpace(clientIp)) request.Headers.Add("X-Client-Ip", clientIp);
    }
}

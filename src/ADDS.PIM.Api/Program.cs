using ADDS.PIM.Application.Authorization;
using ADDS.PIM.Application.Mfa;
using ADDS.PIM.Application.MembershipRequests;
using ADDS.PIM.Application.Security;
using ADDS.PIM.Infrastructure.Persistence;
using ADDS.PIM.Infrastructure.Worker;
using ADDS.PIM.Infrastructure.Mfa;
using ADDS.PIM.Contracts.MembershipRequests.V1;
using ADDS.PIM.Contracts.Mfa.V1;
using ADDS.PIM.Contracts.Administration.V1;
using ADDS.PIM.Application.Administration;
using ADDS.PIM.Application.Audit;
using ADDS.PIM.Application.Diagnostics;
using System.Text.Json;
using ADDS.PIM.Api;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
var directoryScopeConfiguration = new DirectoryScopeConfiguration(
    Guid.TryParse(builder.Configuration["Directory:ScopeId"], out var directoryScopeId) ? directoryScopeId : Guid.Empty,
    builder.Configuration["Directory:DomainDnsName"] ?? string.Empty,
    builder.Configuration["Directory:ForestDnsName"] ?? string.Empty);
directoryScopeConfiguration.Validate();
builder.Services.AddSingleton(directoryScopeConfiguration);
var fido2Configuration = new Fido2RelyingPartyConfiguration(
    builder.Configuration["Fido2:RelyingPartyId"] ?? throw new InvalidOperationException("Fido2:RelyingPartyId is required."),
    builder.Configuration["Fido2:Origin"] ?? throw new InvalidOperationException("Fido2:Origin is required."));
fido2Configuration.Validate();
builder.Services.AddSingleton(fido2Configuration);
builder.Services.AddSingleton<Fido2NetLib.IFido2>(_ => new Fido2NetLib.Fido2(new Fido2NetLib.Fido2Configuration
{
    ServerDomain = fido2Configuration.RelyingPartyId,
    ServerName = "ADDS-PIM",
    Origins = new HashSet<string> { fido2Configuration.Origin }
}));
builder.Services.AddSingleton(new TechnicalErrorLogFileOptions(builder.Configuration["Diagnostics:TechnicalErrorLogFilePath"]));
builder.Services.AddSingleton(TimeProvider.System);
var databaseConnectionString = builder.Configuration.GetConnectionString("PimDatabase")
    ?? throw new InvalidOperationException("ConnectionStrings:PimDatabase is required.");
builder.Services.AddPimSqlServerPersistence(databaseConnectionString);
builder.Services.AddPimApplicationAccess(builder.Configuration);
builder.Services.AddScoped<CreateMembershipRequestUseCase>();
builder.Services.AddScoped<ListMyMembershipRequestsUseCase>();
builder.Services.AddScoped<ListMyAvailableEntitlementsUseCase>();
builder.Services.AddScoped<AuthorizeMembershipRequestUseCase>();
builder.Services.AddScoped<TicketReferenceValidator>();
builder.Services.AddScoped<ValidateApiRequestSignatureUseCase>();
builder.Services.AddScoped<ExecuteMembershipRequestUseCase>();
builder.Services.AddScoped<ResolveCurrentPersonUseCase>();
builder.Services.AddScoped<CreateTotpEnrollmentUseCase>();
builder.Services.AddScoped<ConfirmTotpEnrollmentUseCase>();
builder.Services.AddScoped<CreateMfaTransactionUseCase>();
builder.Services.AddScoped<SecondFactorEligibilityValidator>();
builder.Services.AddScoped<VerifyTotpTransactionUseCase>();
builder.Services.AddScoped<GetMyMfaStatusUseCase>();
builder.Services.AddScoped<StartFido2RegistrationUseCase>();
builder.Services.AddScoped<CompleteFido2RegistrationUseCase>();
builder.Services.AddScoped<VerifyFido2TransactionUseCase>();
builder.Services.AddScoped<GetFido2AssertionOptionsUseCase>();
builder.Services.AddScoped<AdministrationUseCases>();
builder.Services.AddScoped<DirectoryReconciliationUseCase>();
builder.Services.AddScoped<IdentityPurgeUseCase>();
builder.Services.AddScoped<OrphanedSecondFactorRequestCleanupUseCase>();
builder.Services.AddScoped<OrphanedApprovalRequestCleanupUseCase>();
builder.Services.AddScoped<ListPendingApprovalsUseCase>();
builder.Services.AddScoped<CheckApproverEligibilityUseCase>();
builder.Services.AddScoped<ApproveMembershipRequestUseCase>();
builder.Services.AddScoped<RejectMembershipRequestUseCase>();
builder.Services.AddScoped<AuditLogUseCase>();
builder.Services.AddScoped<RecordTechnicalErrorUseCase>();
builder.Services.AddScoped<TechnicalErrorLogUseCase>();
builder.Services.AddHostedService<DirectoryReconciliationHostedService>();
builder.Services.AddHostedService<PurgeEventOutboxDispatcher>();
builder.Services.AddPimWorkerClient(builder.Configuration);
builder.Services.AddPimTotpSecretProtection(builder.Configuration);
var app = builder.Build();

using (var startupScope = app.Services.CreateScope())
{
    var eventLog = startupScope.ServiceProvider.GetRequiredService<IPurgeEventLog>();
    await eventLog.VerifyWritableAsync(CancellationToken.None);
}

// Unhandled exceptions otherwise surface to the caller only as an opaque 500
// with no durable trace anywhere. This records enough to investigate a
// reported Request-ID without exposing internals in the HTTP response.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception exception)
    {
        var requestId = Guid.TryParse(context.Request.Headers["X-Request-Id"], out var parsedRequestId) ? parsedRequestId : (Guid?)null;
        var correlationId = Guid.TryParse(context.Request.Headers["X-Correlation-Id"], out var parsedCorrelationId) ? parsedCorrelationId : (Guid?)null;

        try
        {
            using var scope = context.RequestServices.CreateScope();
            var recorder = scope.ServiceProvider.GetRequiredService<RecordTechnicalErrorUseCase>();
            await recorder.ExecuteAsync(
                new(
                    requestId ?? Guid.NewGuid(),
                    DateTimeOffset.UtcNow,
                    requestId,
                    correlationId,
                    context.Request.Method,
                    context.Request.Path.Value ?? string.Empty,
                    StatusCodes.Status500InternalServerError,
                    exception.GetType().FullName ?? exception.GetType().Name,
                    exception.Message,
                    exception.ToString(),
                    "Api"),
                CancellationToken.None);
        }
        catch
        {
            // Recording the technical error must never mask the original failure.
        }

        if (context.Response.HasStarted)
        {
            throw;
        }

        context.Response.Clear();
        await Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "An unexpected error occurred.").ExecuteAsync(context);
    }
});

app.MapGet("/health/live", () => Results.NoContent())
    .AllowAnonymous();

app.MapPost("/api/v1/membership-requests", async (HttpContext context, CreateMembershipRequest body, ValidateApiRequestSignatureUseCase signatureValidator, AuthorizeMembershipRequestUseCase authorizer, TicketReferenceValidator ticketValidator, SecondFactorEligibilityValidator secondFactorValidator, CreateMembershipRequestUseCase creator, CreateMfaTransactionUseCase mfaTransactionCreator, ExecuteMembershipRequestUseCase executor, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/membership-requests";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request protection metadata.");
    var replay = await signatureValidator.ExecuteAsync(signature, cancellationToken);
    if (replay.Kind != ApiRequestReplayRegistrationKind.Accepted) return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Request cannot be accepted.");

    var authorization = await authorizer.ExecuteAsync(new(new(body.ActorDirectoryScopeId, body.ActorObjectGuid), body.TargetAccountId, body.TargetGroupId, body.RequestedTtlSeconds), cancellationToken);
    if (authorization is null) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    // A second-factor-required policy defers ticket validation to execution time (once the factor is
    // verified), rather than rejecting or checking the ticket before AwaitingSecondFactor even starts.
    if (!authorization.RequiresSecondFactor && await ticketValidator.ValidateAsync(authorization.TargetGroupId, body.TicketReference, cancellationToken) != TicketReferenceValidationResult.Valid)
        return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Ticket reference is invalid.");
    // Reject immediately when no eligible factor exists at all, rather than creating a request that can
    // never leave AwaitingSecondFactor (poc-status-and-handoff, "Known MFA gap").
    if (authorization.RequiresSecondFactor && !await secondFactorValidator.HasEligibleFactorAsync(authorization.PersonId, authorization.AllowedSecondFactorTypes, cancellationToken))
        return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "No active second factor is enrolled for this account.");

    var created = await creator.ExecuteAsync(new(signature.RequestId, signature.CorrelationId, authorization.PersonId, authorization.ActorAccountId, authorization.TargetAccountId, authorization.EntitlementId, signature.KeyId, "Windows", authorization.PolicyRequirementsSummary, context.Connection.RemoteIpAddress?.ToString(), authorization.TargetGroupId, authorization.PersonDisplayName, authorization.ActorAccountDisplayName, authorization.TargetAccountDisplayName, authorization.TargetGroupDisplayName, body.RequestedTtlSeconds, body.Reason, body.TicketReference), cancellationToken);
    var auditContext = new MembershipRequestTransitionAuditContext(signature.CorrelationId, signature.KeyId, "Windows", authorization.PolicyRequirementsSummary, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context));

    if (authorization.RequiresSecondFactor)
    {
        var mfaTransaction = await mfaTransactionCreator.ExecuteAsync(new(created.RequestId, authorization.PersonId, authorization.ActorAccountId, authorization.TargetAccountId, authorization.TargetGroupId, body.RequestedTtlSeconds, authorization.PolicyRequirementsSummary, authorization.AllowedSecondFactorTypes, auditContext), cancellationToken);
        return Results.Ok(new MembershipRequestPendingSecondFactor(created.RequestId, signature.CorrelationId, mfaTransaction.MfaTransactionId, "AwaitingSecondFactor", mfaTransaction.ExpiresUtc));
    }

    var execution = await executor.ExecuteAsync(new(created.RequestId, signature.CorrelationId, new(body.ActorDirectoryScopeId, body.ActorObjectGuid), body.TargetAccountId, body.TargetGroupId, body.RequestedTtlSeconds, body.TicketReference, auditContext), cancellationToken);
    return Results.Ok(new MembershipRequestAccepted(created.RequestId, signature.CorrelationId, execution.Status.ToString(), created.CreatedUtc, execution.OutcomeMessage));
});

app.MapPost("/api/v1/membership-requests/{requestId:guid}/second-factor/totp/verify", async (Guid requestId, HttpContext context, VerifyTotpSecondFactorRequest body, ValidateApiRequestSignatureUseCase signatureValidator, ResolveCurrentPersonUseCase resolvePerson, VerifyTotpTransactionUseCase verify, ExecuteMembershipRequestUseCase executor, CancellationToken cancellationToken) =>
{
    var path = $"/api/v1/membership-requests/{requestId:D}/second-factor/totp/verify";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request protection metadata.");
    if ((await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted)
        return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Request cannot be accepted.");

    Guid? personId;
    try { personId = await resolvePerson.ExecuteAsync(new(body.ActorDirectoryScopeId, body.ActorObjectGuid), cancellationToken); }
    catch (ArgumentException) { return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid."); }
    if (personId is null) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");

    var auditContext = new MembershipRequestTransitionAuditContext(signature.CorrelationId, signature.KeyId, "Windows", "totp-second-factor-verify", context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context));
    var verification = await verify.ExecuteAsync(new(requestId, personId.Value, body.Code, auditContext), cancellationToken);
    switch (verification.Outcome)
    {
        case TotpTransactionVerificationOutcome.TransactionNotFound:
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No pending second-factor transaction was found.");
        case TotpTransactionVerificationOutcome.TransactionExpired:
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The second-factor transaction has expired.");
        case TotpTransactionVerificationOutcome.FactorNotAllowed:
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "TOTP is not an allowed factor for this request.");
        case TotpTransactionVerificationOutcome.NoActiveFactor:
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "No active TOTP factor is enrolled.");
        case TotpTransactionVerificationOutcome.Locked:
            return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "The TOTP factor is temporarily locked.");
        case TotpTransactionVerificationOutcome.InvalidCode:
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "The submitted code is invalid.");
    }

    var executionAuditContext = new MembershipRequestTransitionAuditContext(signature.CorrelationId, signature.KeyId, "Windows", "totp-second-factor-verify", context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context));
    var execution = await executor.ExecuteFromSecondFactorValidatedAsync(new(requestId, signature.CorrelationId, new(body.ActorDirectoryScopeId, body.ActorObjectGuid), verification.TargetAccountId, verification.TargetGroupId, verification.RequestedTtlSeconds, verification.TicketReference, executionAuditContext), cancellationToken);
    return Results.Ok(new MembershipRequestAccepted(requestId, signature.CorrelationId, execution.Status.ToString(), DateTimeOffset.UtcNow, execution.OutcomeMessage));
});

app.MapPost("/api/v1/mfa/totp/enroll", async (HttpContext context, EnrollTotpFactorRequest body, ValidateApiRequestSignatureUseCase signatureValidator, ResolveCurrentPersonUseCase resolvePerson, IPersonAccountLabelResolver labelResolver, CreateTotpEnrollmentUseCase enroll, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/mfa/totp/enroll";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request protection metadata.");
    if ((await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted)
        return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Request cannot be accepted.");

    Guid? personId;
    try { personId = await resolvePerson.ExecuteAsync(new(body.ActorDirectoryScopeId, body.ActorObjectGuid), cancellationToken); }
    catch (ArgumentException) { return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid."); }
    if (personId is null) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");

    var enrollment = await enroll.ExecuteAsync(new(personId.Value, signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken);
    if (enrollment is null) return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "A TOTP factor is already enrolled. An administrator must reset it before re-enrollment is possible.");
    var secret = Base32.Encode(enrollment.Secret);
    var label = (await labelResolver.ResolveAsync(personId.Value, cancellationToken))?.Combined ?? personId.Value.ToString("D");
    var provisioningUri = $"otpauth://totp/{Uri.EscapeDataString($"ADDS-PIM:{label}")}?secret={secret}&issuer=ADDS-PIM&digits=6&period=30";
    return Results.Ok(new TotpEnrollmentStarted(enrollment.TotpFactorId, provisioningUri, secret, enrollment.ProtectionKeyId, enrollment.EnrollmentExpiresUtc));
});

app.MapPost("/api/v1/mfa/totp/enroll/{factorId:guid}/confirm", async (Guid factorId, HttpContext context, ConfirmTotpEnrollmentRequest body, ValidateApiRequestSignatureUseCase signatureValidator, ResolveCurrentPersonUseCase resolvePerson, ConfirmTotpEnrollmentUseCase confirm, CancellationToken cancellationToken) =>
{
    var path = $"/api/v1/mfa/totp/enroll/{factorId:D}/confirm";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request protection metadata.");
    if ((await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted)
        return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Request cannot be accepted.");

    Guid? personId;
    try { personId = await resolvePerson.ExecuteAsync(new(body.ActorDirectoryScopeId, body.ActorObjectGuid), cancellationToken); }
    catch (ArgumentException) { return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid."); }
    if (personId is null) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");

    var confirmed = await confirm.ExecuteAsync(new(personId.Value, factorId, body.Code, signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken);
    return confirmed ? Results.NoContent() : Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "The submitted code is invalid or the enrollment has expired.");
});

app.MapPost("/api/v1/mfa/status/query", async (HttpContext context, QueryMyMfaStatus body, ValidateApiRequestSignatureUseCase signatureValidator, GetMyMfaStatusUseCase mfaStatus, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/mfa/status/query";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request protection metadata.");
    if ((await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted)
        return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Request cannot be accepted.");

    var result = await mfaStatus.ExecuteAsync(new(body.ActorDirectoryScopeId, body.ActorObjectGuid), cancellationToken);
    if (result is null) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    return Results.Ok(new MyMfaStatusResponse(result.TotpEnrolled, result.TotpConfirmedUtc, result.Fido2Available, result.Fido2Enrolled, result.Fido2ConfirmedUtc,
        result.Fido2Credentials.Select(credential => new Fido2CredentialSummary(credential.Fido2CredentialId, credential.Label, credential.CreatedUtc)).ToArray()));
});

app.MapPost("/api/v1/mfa/fido2/register/options", async (HttpContext context, StartFido2RegistrationRequest body, ValidateApiRequestSignatureUseCase signatureValidator, ResolveCurrentPersonUseCase resolvePerson, IPersonAccountLabelResolver labelResolver, StartFido2RegistrationUseCase start, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/mfa/fido2/register/options";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request protection metadata.");
    if ((await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted)
        return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Request cannot be accepted.");

    Guid? personId;
    try { personId = await resolvePerson.ExecuteAsync(new(body.ActorDirectoryScopeId, body.ActorObjectGuid), cancellationToken); }
    catch (ArgumentException) { return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid."); }
    if (personId is null) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");

    // The WebAuthn user name/display name is shown by the authenticator/browser and stored on the
    // security key itself (visible e.g. via `ykman fido credentials list`) - use a human-recognizable
    // "AD account (display name)" label, never the raw person ID.
    var personLabel = (await labelResolver.ResolveAsync(personId.Value, cancellationToken))?.Combined ?? personId.Value.ToString("D");
    var result = await start.ExecuteAsync(new(personId.Value, personLabel, signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context), body.StepUpChallengeId, body.StepUpTotpCode, body.StepUpAssertionResponseJson), cancellationToken);
    return Results.Ok(new Fido2RegistrationOptionsResponse(result.StepUpRequired, result.StepUpTotpAllowed, result.StepUpChallengeId, result.StepUpAssertionOptionsJson, result.RegistrationChallengeId, result.RegistrationOptionsJson));
});

app.MapPost("/api/v1/mfa/fido2/register/complete", async (HttpContext context, CompleteFido2RegistrationRequest body, ValidateApiRequestSignatureUseCase signatureValidator, ResolveCurrentPersonUseCase resolvePerson, CompleteFido2RegistrationUseCase complete, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/mfa/fido2/register/complete";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request protection metadata.");
    if ((await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted)
        return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Request cannot be accepted.");

    Guid? personId;
    try { personId = await resolvePerson.ExecuteAsync(new(body.ActorDirectoryScopeId, body.ActorObjectGuid), cancellationToken); }
    catch (ArgumentException) { return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid."); }
    if (personId is null) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");

    var result = await complete.ExecuteAsync(new(personId.Value, body.ChallengeId, body.AttestationResponseJson, body.Label, signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken);
    return result.Outcome switch
    {
        Fido2RegistrationCompletionOutcome.Succeeded => Results.NoContent(),
        Fido2RegistrationCompletionOutcome.ChallengeNotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No pending registration challenge was found."),
        Fido2RegistrationCompletionOutcome.ChallengeExpired => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The registration challenge has expired."),
        _ => Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "The passkey could not be registered.")
    };
});

app.MapPost("/api/v1/membership-requests/{requestId:guid}/second-factor/fido2/options", async (Guid requestId, HttpContext context, Fido2AssertionOptionsQuery body, ValidateApiRequestSignatureUseCase signatureValidator, ResolveCurrentPersonUseCase resolvePerson, GetFido2AssertionOptionsUseCase getOptions, CancellationToken cancellationToken) =>
{
    var path = $"/api/v1/membership-requests/{requestId:D}/second-factor/fido2/options";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request protection metadata.");
    if ((await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted)
        return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Request cannot be accepted.");

    Guid? personId;
    try { personId = await resolvePerson.ExecuteAsync(new(body.ActorDirectoryScopeId, body.ActorObjectGuid), cancellationToken); }
    catch (ArgumentException) { return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid."); }
    if (personId is null) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");

    var result = await getOptions.ExecuteAsync(requestId, personId.Value, cancellationToken);
    return result.TransactionFound
        ? Results.Ok(new Fido2AssertionOptionsResponse(result.OptionsJson!, result.ExpiresUtc))
        : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No pending FIDO2 second-factor confirmation was found.");
});

app.MapPost("/api/v1/membership-requests/{requestId:guid}/second-factor/fido2/verify", async (Guid requestId, HttpContext context, VerifyFido2SecondFactorRequest body, ValidateApiRequestSignatureUseCase signatureValidator, ResolveCurrentPersonUseCase resolvePerson, VerifyFido2TransactionUseCase verify, ExecuteMembershipRequestUseCase executor, CancellationToken cancellationToken) =>
{
    var path = $"/api/v1/membership-requests/{requestId:D}/second-factor/fido2/verify";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request protection metadata.");
    if ((await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted)
        return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Request cannot be accepted.");

    Guid? personId;
    try { personId = await resolvePerson.ExecuteAsync(new(body.ActorDirectoryScopeId, body.ActorObjectGuid), cancellationToken); }
    catch (ArgumentException) { return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid."); }
    if (personId is null) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");

    var auditContext = new MembershipRequestTransitionAuditContext(signature.CorrelationId, signature.KeyId, "Windows", "fido2-second-factor-verify", context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context));
    var verification = await verify.ExecuteAsync(new(requestId, personId.Value, body.AssertionResponseJson, auditContext), cancellationToken);
    switch (verification.Outcome)
    {
        case Fido2TransactionVerificationOutcome.TransactionNotFound:
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No pending second-factor transaction was found.");
        case Fido2TransactionVerificationOutcome.TransactionExpired:
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The second-factor transaction has expired.");
        case Fido2TransactionVerificationOutcome.FactorNotAllowed:
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "FIDO2 is not an allowed factor for this request.");
        case Fido2TransactionVerificationOutcome.NoActiveCredential:
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "No active FIDO2 credential is enrolled.");
        case Fido2TransactionVerificationOutcome.Rejected:
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "The passkey assertion could not be verified.");
    }

    var executionAuditContext = new MembershipRequestTransitionAuditContext(signature.CorrelationId, signature.KeyId, "Windows", "fido2-second-factor-verify", context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context));
    var execution = await executor.ExecuteFromSecondFactorValidatedAsync(new(requestId, signature.CorrelationId, new(body.ActorDirectoryScopeId, body.ActorObjectGuid), verification.TargetAccountId, verification.TargetGroupId, verification.RequestedTtlSeconds, verification.TicketReference, executionAuditContext), cancellationToken);
    return Results.Ok(new MembershipRequestAccepted(requestId, signature.CorrelationId, execution.Status.ToString(), DateTimeOffset.UtcNow, execution.OutcomeMessage));
});

app.MapPost("/api/v1/membership-requests/mine/query", async (HttpContext context, QueryMyMembershipRequests body, ValidateApiRequestSignatureUseCase signatureValidator, ListMyMembershipRequestsUseCase history, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/membership-requests/mine/query";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request protection metadata.");
    if ((await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted)
        return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Request cannot be accepted.");

    MembershipRequestHistoryPage? result;
    try
    {
        result = await history.ExecuteAsync(new(
            new(body.ActorDirectoryScopeId, body.ActorObjectGuid),
            body.PageNumber,
            body.PageSize,
            body.Year,
            body.Month,
            body.EntitlementId,
            body.TargetGroupId), cancellationToken);
    }
    catch (ArgumentException)
    {
        return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid.");
    }

    if (result is null) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    return Results.Ok(new MyMembershipRequestsPage(
        result.Items.Select(request => new MyMembershipRequest(request.RequestId, request.EntitlementId, request.TargetGroupId, request.TargetAccountDisplayName, request.TargetGroupDisplayName, request.RequestedTtlSeconds, request.CreatedUtc, request.Status.ToString(), request.Reason, request.TicketReference)).ToArray(),
        result.TotalCount,
        result.AvailableYears,
        result.Entitlements.Select(option => new MyMembershipRequestFilterOption(option.Id, option.DisplayName)).ToArray(),
        result.TargetGroups.Select(option => new MyMembershipRequestFilterOption(option.Id, option.DisplayName)).ToArray()));
});

app.MapPost("/api/v1/membership-requests/mine/available-entitlements/query", async (HttpContext context, QueryMyAvailableEntitlements body, ValidateApiRequestSignatureUseCase signatureValidator, ListMyAvailableEntitlementsUseCase entitlements, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/membership-requests/mine/available-entitlements/query";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted)
        return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Request cannot be accepted.");
    IReadOnlyList<AvailableMembershipEntitlement>? result;
    try { result = await entitlements.ExecuteAsync(new(body.ActorDirectoryScopeId, body.ActorObjectGuid), cancellationToken); }
    catch (ArgumentException) { return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid."); }
    if (result is null) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    return Results.Ok(result.Select(item => new MyAvailableMembershipEntitlement(item.EntitlementId, item.TargetAccountId, item.TargetGroupId, item.TargetAccountDisplayName, item.TargetGroupDisplayName, item.MinimumTtlSeconds, item.MaximumTtlSeconds, item.DefaultTtlSeconds, item.TtlStepSeconds, item.RequiresSecondFactor, item.RequiresApproval, item.RequiresTicket, (int)item.AllowedSecondFactorTypes)).ToArray());
});

app.MapPost("/api/v1/membership-requests/pending-approvals/eligibility", async (HttpContext context, QueryApproverEligibility body, ValidateApiRequestSignatureUseCase signatureValidator, CheckApproverEligibilityUseCase eligibility, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/membership-requests/pending-approvals/eligibility";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted)
        return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Request cannot be accepted.");
    var isApprover = await eligibility.ExecuteAsync(new(body.ActorDirectoryScopeId, body.ActorObjectGuid), cancellationToken);
    return Results.Ok(new ApproverEligibilityResponse(isApprover));
});

app.MapPost("/api/v1/membership-requests/pending-approvals/query", async (HttpContext context, QueryPendingApprovals body, ValidateApiRequestSignatureUseCase signatureValidator, ListPendingApprovalsUseCase pendingApprovals, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/membership-requests/pending-approvals/query";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted)
        return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Request cannot be accepted.");
    var result = await pendingApprovals.ExecuteAsync(new(body.ActorDirectoryScopeId, body.ActorObjectGuid), body.PageNumber, body.PageSize, cancellationToken);
    return result is null ? Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid.") : Results.Ok(result);
});

app.MapPost("/api/v1/membership-requests/{requestId:guid}/approve", async (Guid requestId, HttpContext context, ApproveMembershipRequest body, ValidateApiRequestSignatureUseCase signatureValidator, ApproveMembershipRequestUseCase approve, CancellationToken cancellationToken) =>
{
    var path = $"/api/v1/membership-requests/{requestId:D}/approve";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request protection metadata.");
    if ((await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted)
        return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Request cannot be accepted.");

    var auditContext = new MembershipRequestTransitionAuditContext(signature.CorrelationId, signature.KeyId, "Windows", "approver-decision", context.Connection.RemoteIpAddress?.ToString(), ClientSourceIpAddress: ReadClientIp(context));
    var result = await approve.ExecuteAsync(requestId, new(body.ActorDirectoryScopeId, body.ActorObjectGuid), signature.CorrelationId, auditContext, cancellationToken);
    return result.Outcome switch
    {
        ApprovalDecisionOutcome.Accepted => Results.Ok(new ApprovalDecisionAccepted(requestId, result.Execution!.Status.ToString(), result.Execution.OutcomeMessage)),
        ApprovalDecisionOutcome.NotFound => Results.NotFound(),
        ApprovalDecisionOutcome.NotEligible => Results.Conflict(),
        ApprovalDecisionOutcome.Forbidden => Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized."),
        _ => Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid.")
    };
});

app.MapPost("/api/v1/membership-requests/{requestId:guid}/reject", async (Guid requestId, HttpContext context, RejectMembershipRequest body, ValidateApiRequestSignatureUseCase signatureValidator, RejectMembershipRequestUseCase reject, CancellationToken cancellationToken) =>
{
    var path = $"/api/v1/membership-requests/{requestId:D}/reject";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request protection metadata.");
    if ((await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted)
        return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Request cannot be accepted.");

    var auditContext = new MembershipRequestTransitionAuditContext(signature.CorrelationId, signature.KeyId, "Windows", "approver-decision", context.Connection.RemoteIpAddress?.ToString(), ClientSourceIpAddress: ReadClientIp(context));
    var outcome = await reject.ExecuteAsync(requestId, new(body.ActorDirectoryScopeId, body.ActorObjectGuid), body.RejectionReason, auditContext, cancellationToken);
    return outcome switch
    {
        ApprovalDecisionOutcome.Accepted => Results.Ok(new ApprovalDecisionAccepted(requestId, "Rejected", null)),
        ApprovalDecisionOutcome.NotFound => Results.NotFound(),
        ApprovalDecisionOutcome.NotEligible => Results.Conflict(),
        ApprovalDecisionOutcome.Forbidden => Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized."),
        _ => Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid.")
    };
});

app.MapPost("/api/v1/admin/target-groups/{targetGroupId:guid}/approvers/query", async (Guid targetGroupId, HttpContext context, QueryGroupApproversRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    if (targetGroupId != body.TargetGroupId) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid.");
    var path = $"/api/v1/admin/target-groups/{targetGroupId:D}/approvers/query"; var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    return Results.Ok(await administration.ListGroupApproversAsync(targetGroupId, cancellationToken));
});

app.MapPost("/api/v1/admin/target-groups/{targetGroupId:guid}/approvers", async (Guid targetGroupId, HttpContext context, AddGroupApproverRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    if (targetGroupId != body.TargetGroupId) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid.");
    var path = $"/api/v1/admin/target-groups/{targetGroupId:D}/approvers"; var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var result = await administration.AddGroupApproverAsync(body, new(signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken);
    return result switch { AdministrationUpdateResult.Updated => Results.NoContent(), AdministrationUpdateResult.NotFound => Results.NotFound(), AdministrationUpdateResult.Conflict => Results.Conflict(), _ => Results.UnprocessableEntity() };
});

app.MapPost("/api/v1/admin/target-groups/{targetGroupId:guid}/approvers/{groupApproverId:guid}/deactivate", async (Guid targetGroupId, Guid groupApproverId, HttpContext context, DeactivateGroupApproverRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    if (targetGroupId != body.TargetGroupId || groupApproverId != body.GroupApproverId) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid.");
    var path = $"/api/v1/admin/target-groups/{targetGroupId:D}/approvers/{groupApproverId:D}/deactivate"; var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var result = await administration.DeactivateGroupApproverAsync(body, new(signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken);
    return result switch { AdministrationUpdateResult.Updated => Results.NoContent(), AdministrationUpdateResult.NotFound => Results.NotFound(), AdministrationUpdateResult.Conflict => Results.Conflict(), _ => Results.UnprocessableEntity() };
});

app.MapPost("/api/v1/admin/orphaned-approvals/query", async (HttpContext context, QueryAdministrationRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, OrphanedApprovalRequestCleanupUseCase cleanup, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/admin/orphaned-approvals/query";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var candidates = await cleanup.ListCandidatesAsync(new(body.Actor.DirectoryScopeId, body.Actor.ObjectGuid), cancellationToken);
    return Results.Ok(candidates.Select(candidate => new ManagedOrphanedApprovalRequest(candidate.RequestId, candidate.PersonDisplayName, candidate.TargetAccountDisplayName, candidate.TargetGroupDisplayName, candidate.CreatedUtc, candidate.EnteredAwaitingApprovalUtc)).ToArray());
});

app.MapPost("/api/v1/admin/orphaned-approvals/{requestId:guid}/expire", async (Guid requestId, HttpContext context, ExpireOrphanedApprovalRequestRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, OrphanedApprovalRequestCleanupUseCase cleanup, CancellationToken cancellationToken) =>
{
    if (requestId != body.RequestId) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid.");
    var path = $"/api/v1/admin/orphaned-approvals/{requestId:D}/expire"; var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var auditContext = new MembershipRequestTransitionAuditContext(signature.CorrelationId, signature.KeyId, "Windows", "admin-orphaned-approval-cleanup", context.Connection.RemoteIpAddress?.ToString(), ClientSourceIpAddress: ReadClientIp(context));
    var result = await cleanup.ExpireAsync(new(body.Actor.DirectoryScopeId, body.Actor.ObjectGuid), requestId, auditContext, cancellationToken);
    return result switch { OrphanedApprovalRequestCleanupResult.Expired => Results.NoContent(), OrphanedApprovalRequestCleanupResult.NotFound => Results.NotFound(), _ => Results.Conflict() };
});

app.MapPost("/api/v1/admin/directory-reconciliation/query", async (HttpContext context, QueryAdministrationRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, DirectoryReconciliationUseCase reconciliation, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/admin/directory-reconciliation/query";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var runs = await reconciliation.ListRunsAsync(cancellationToken);
    var findings = await reconciliation.ListFindingsAsync(cancellationToken);
    return Results.Ok(new DirectoryReconciliationOverview(
        runs.Select(run => new ManagedDirectoryReconciliationRun(run.RunId, run.Status.ToString(), run.RequestedUtc, run.StartedUtc, run.CompletedUtc, run.FailureCategory, run.RowVersion)).ToArray(),
        findings.Select(finding => new ManagedDirectoryReconciliationFinding(finding.FindingId, finding.RunId, finding.EntityType.ToString(), finding.EntityId, finding.DirectoryScopeId, finding.ObjectGuid, finding.Reason.ToString(), finding.DisplayName, finding.DetectedUtc, finding.IsResolved, finding.ResolvedUtc, finding.DeactivatedUtc, finding.RowVersion)).ToArray()));
});

app.MapPost("/api/v1/admin/directory-reconciliation/runs", async (HttpContext context, StartDirectoryReconciliationRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, DirectoryReconciliationUseCase reconciliation, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/admin/directory-reconciliation/runs";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var result = await reconciliation.QueueAsync(body, new(signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken);
    return result switch { AdministrationUpdateResult.Updated => Results.Accepted(), AdministrationUpdateResult.Conflict => Results.Conflict(), _ => Results.UnprocessableEntity() };
});

app.MapPost("/api/v1/admin/directory-reconciliation/findings/{findingId:guid}/deactivate", async (Guid findingId, HttpContext context, DeactivateDirectoryReconciliationFindingRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, DirectoryReconciliationUseCase reconciliation, CancellationToken cancellationToken) =>
{
    if (findingId != body.FindingId) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid.");
    var path = $"/api/v1/admin/directory-reconciliation/findings/{findingId:D}/deactivate";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var result = await reconciliation.DeactivateFromFindingAsync(body, new(signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken);
    return result switch { AdministrationUpdateResult.Updated => Results.NoContent(), AdministrationUpdateResult.NotFound => Results.NotFound(), AdministrationUpdateResult.Conflict => Results.Conflict(), _ => Results.UnprocessableEntity() };
});

app.MapPost("/api/v1/admin/audit-log/query", async (HttpContext context, QueryAuditLogRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AuditLogUseCase auditLog, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/admin/audit-log/query";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var result = await auditLog.QueryAsync(body, cancellationToken);
    if (result is null) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid.");
    return Results.Ok(new AuditLogPage(
        result.Items.Select(entry => new AuditLogEntry(entry.EventId, entry.EventType, entry.OccurredUtc, entry.PersonDisplayNameSnapshot, entry.ActorAccountDisplayNameSnapshot, entry.TargetAccountDisplayNameSnapshot, entry.TargetGroupDisplayNameSnapshot, entry.SourceComponent, entry.SourceIpAddress, entry.ClientSourceIpAddress, entry.CorrelationId, entry.RequestId, entry.RequestedTtlSeconds, entry.Result, entry.FailureCategory, entry.AuthenticationMethod, entry.FrontendClientId)).ToArray(),
        result.TotalCount,
        result.AvailableEventTypes,
        result.AvailableResults,
        result.AvailableActorAccounts));
});

app.MapPost("/api/v1/admin/technical-errors/query", async (HttpContext context, QueryTechnicalErrorLogRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, TechnicalErrorLogUseCase technicalErrors, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/admin/technical-errors/query";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var result = await technicalErrors.QueryAsync(body, cancellationToken);
    if (result is null) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid.");
    return Results.Ok(new TechnicalErrorLogPage(
        result.Items.Select(entry => new TechnicalErrorLogEntry(entry.ErrorId, entry.OccurredUtc, entry.RequestId, entry.CorrelationId, entry.HttpMethod, entry.Path, entry.StatusCode, entry.ExceptionType, entry.Message, entry.StackTrace, entry.SourceComponent)).ToArray(),
        result.TotalCount));
});

app.MapPost("/api/v1/admin/identity-purge/query", async (HttpContext context, QueryIdentityPurgeScopeRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, IdentityPurgeUseCase purge, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/admin/identity-purge/query";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    if (!Enum.TryParse<PurgeInitiatorType>(body.InitiatorType, true, out var initiatorType)) return Results.UnprocessableEntity();
    var preview = await purge.GetPreviewAsync(body.Actor, initiatorType, body.InitiatorId, cancellationToken);
    return preview is null ? Results.NotFound() : Results.Ok(new IdentityPurgeScopePreview(preview.InitiatorType.ToString(), preview.InitiatorId, preview.DisplayName, preview.IsEligible, preview.BlockingReason, preview.Persons, preview.DirectoryAccounts, preview.PersonAccountLinks, preview.TargetGroups, preview.GroupPolicies, preview.DirectEntitlements, preview.MembershipRequests, preview.MembershipRequestStatusHistory, preview.MfaTransactions, preview.TotpFactors, preview.TotpUsedTimeSteps, preview.Fido2Credentials, preview.CanonicalScopeSummary));
});

app.MapPost("/api/v1/admin/identity-purge/candidates/query", async (HttpContext context, QueryAdministrationRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, IdentityPurgeUseCase purge, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/admin/identity-purge/candidates/query";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var candidates = await purge.ListCandidatesAsync(body.Actor, cancellationToken);
    return Results.Ok(candidates.Select(candidate => new ADDS.PIM.Contracts.Administration.V1.IdentityPurgeCandidate(candidate.InitiatorType.ToString(), candidate.InitiatorId, candidate.DisplayName)).ToArray());
});

app.MapPost("/api/v1/admin/identity-purge/execute", async (HttpContext context, ExecuteIdentityPurgeRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, IdentityPurgeUseCase purge, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/admin/identity-purge/execute";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    if (!Enum.TryParse<PurgeInitiatorType>(body.InitiatorType, true, out var initiatorType)) return Results.UnprocessableEntity();
    try
    {
        var result = await purge.ExecuteAsync(body.Actor, initiatorType, body.InitiatorId, body.Confirmation, new(signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken);
        return result switch { AdministrationUpdateResult.Updated => Results.NoContent(), AdministrationUpdateResult.NotFound => Results.NotFound(), AdministrationUpdateResult.Conflict => Results.Conflict(), _ => Results.UnprocessableEntity() };
    }
    catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Purge cannot be started because mandatory audit evidence is unavailable.");
    }
});

app.MapPost("/api/v1/admin/mfa/orphaned-requests/query", async (HttpContext context, QueryAdministrationRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, OrphanedSecondFactorRequestCleanupUseCase cleanup, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/admin/mfa/orphaned-requests/query";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var candidates = await cleanup.ListCandidatesAsync(body.Actor, cancellationToken);
    return Results.Ok(candidates.Select(candidate => new ManagedOrphanedSecondFactorRequest(candidate.RequestId, candidate.PersonDisplayName, candidate.TargetAccountDisplayName, candidate.TargetGroupDisplayName, candidate.CreatedUtc, candidate.TransactionExpiresUtc)).ToArray());
});

app.MapPost("/api/v1/admin/mfa/orphaned-requests/expire", async (HttpContext context, ExpireOrphanedSecondFactorRequestRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, OrphanedSecondFactorRequestCleanupUseCase cleanup, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/admin/mfa/orphaned-requests/expire";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var auditContext = new MembershipRequestTransitionAuditContext(signature.CorrelationId, signature.KeyId, "Windows", "orphaned-second-factor-request-cleanup", context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context));
    var result = await cleanup.ExpireAsync(body.Actor, body.RequestId, auditContext, cancellationToken);
    return result switch { OrphanedSecondFactorRequestCleanupResult.Expired => Results.NoContent(), OrphanedSecondFactorRequestCleanupResult.NotFound => Results.NotFound(), _ => Results.Conflict() };
});

app.MapPost("/api/v1/admin/target-groups/query", async (HttpContext context, QueryAdministrationRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    var signature = ReadSignature(context, "/api/v1/admin/target-groups/query", JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    if (!await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    return Results.Ok(await administration.ListTargetGroupsAsync(cancellationToken));
});

app.MapPost("/api/v1/admin/settings/ticket-reference-patterns/query", async (HttpContext context, QueryAdministrationRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/admin/settings/ticket-reference-patterns/query";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    return Results.Ok(await administration.ListTicketReferencePatternsAsync(cancellationToken));
});

app.MapPut("/api/v1/admin/settings/ticket-reference-patterns/{patternId:guid}", async (Guid patternId, HttpContext context, UpsertTicketReferencePatternRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    if (patternId != body.TicketReferencePatternId) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid.");
    var path = $"/api/v1/admin/settings/ticket-reference-patterns/{patternId:D}";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var result = await administration.UpsertTicketReferencePatternAsync(body, new(signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken);
    return result switch { AdministrationUpdateResult.Updated => Results.NoContent(), AdministrationUpdateResult.NotFound => Results.NotFound(), AdministrationUpdateResult.Conflict => Results.Conflict(), _ => Results.UnprocessableEntity() };
});

app.MapDelete("/api/v1/admin/settings/ticket-reference-patterns/{patternId:guid}", async (Guid patternId, HttpContext context, [FromBody] DeleteTicketReferencePatternRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    if (patternId != body.TicketReferencePatternId) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid.");
    var path = $"/api/v1/admin/settings/ticket-reference-patterns/{patternId:D}";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var result = await administration.DeleteTicketReferencePatternAsync(body, new(signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken);
    return result switch { AdministrationUpdateResult.Updated => Results.NoContent(), AdministrationUpdateResult.NotFound => Results.NotFound(), AdministrationUpdateResult.Conflict => Results.Conflict(), _ => Results.UnprocessableEntity() };
});

// TOTP secret-protection certificate rollover.
app.MapPost("/api/v1/admin/settings/totp-protection-certificate/status/query", async (HttpContext context, QueryAdministrationRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/admin/settings/totp-protection-certificate/status/query";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    return Results.Ok(await administration.GetTotpProtectionCertificateStatusAsync(cancellationToken));
});

app.MapPost("/api/v1/admin/settings/totp-protection-certificate/rotate", async (HttpContext context, RotateTotpProtectionCertificateRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/admin/settings/totp-protection-certificate/rotate";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var result = await administration.RotateTotpProtectionCertificateAsync(body, new(signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken);
    return result switch { AdministrationUpdateResult.Updated => Results.NoContent(), AdministrationUpdateResult.NotFound => Results.NotFound(), AdministrationUpdateResult.Conflict => Results.Conflict(), _ => Results.UnprocessableEntity() };
});

app.MapPost("/api/v1/admin/certificates/overview/query", async (HttpContext context, QueryAdministrationRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/admin/certificates/overview/query";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    return Results.Ok(await administration.GetCertificateOverviewAsync(cancellationToken));
});

app.MapPost("/api/v1/admin/persons/query", async (HttpContext context, QueryAdministrationRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/admin/persons/query"; var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    return Results.Ok(await administration.ListPersonsAsync(cancellationToken));
});

app.MapPost("/api/v1/admin/directory-users/search", async (HttpContext context, SearchDirectoryUsersRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/admin/directory-users/search"; var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    if (string.IsNullOrWhiteSpace(body.SearchTerm) || body.SearchTerm.Trim().Length is < 2 or > 128) return Results.UnprocessableEntity();
    return Results.Ok(await administration.SearchDirectoryUsersAsync(body, cancellationToken));
});

app.MapPost("/api/v1/admin/persons/from-directory-account", async (HttpContext context, CreatePersonFromDirectoryAccountRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/admin/persons/from-directory-account"; var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var result = await administration.CreatePersonFromDirectoryAccountAsync(body, new(signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken); return result switch { AdministrationUpdateResult.Updated => Results.NoContent(), AdministrationUpdateResult.NotFound => Results.NotFound(), AdministrationUpdateResult.Conflict => Results.Conflict(), _ => Results.UnprocessableEntity() };
});

app.MapPost("/api/v1/admin/persons/{personId:guid}/deactivate", async (Guid personId, HttpContext context, DeactivatePersonRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    if (personId != body.PersonId) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid."); var path = $"/api/v1/admin/persons/{personId:D}/deactivate"; var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var result = await administration.DeactivatePersonAsync(body, new(signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken); return result switch { AdministrationUpdateResult.Updated => Results.NoContent(), AdministrationUpdateResult.NotFound => Results.NotFound(), AdministrationUpdateResult.Conflict => Results.Conflict(), _ => Results.UnprocessableEntity() };
});

app.MapPost("/api/v1/admin/persons/{personId:guid}/reactivate", async (Guid personId, HttpContext context, ReactivatePersonRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    if (personId != body.PersonId) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid."); var path = $"/api/v1/admin/persons/{personId:D}/reactivate"; var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var result = await administration.ReactivatePersonAsync(body, new(signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken); return result switch { AdministrationUpdateResult.Updated => Results.NoContent(), AdministrationUpdateResult.NotFound => Results.NotFound(), AdministrationUpdateResult.Conflict => Results.Conflict(), _ => Results.UnprocessableEntity() };
});

app.MapPost("/api/v1/admin/persons/{personId:guid}/detail/query", async (Guid personId, HttpContext context, QueryAdministrationRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    var path = $"/api/v1/admin/persons/{personId:D}/detail/query"; var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var detail = await administration.GetPersonDetailAsync(personId, cancellationToken); return detail is null ? Results.NotFound() : Results.Ok(detail);
});

app.MapPost("/api/v1/admin/directory-accounts/search", async (HttpContext context, SearchDirectoryUsersRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/admin/directory-accounts/search"; var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    if (string.IsNullOrWhiteSpace(body.SearchTerm) || body.SearchTerm.Trim().Length is < 2 or > 128) return Results.UnprocessableEntity();
    return Results.Ok(await administration.SearchDirectoryAccountsAsync(body, cancellationToken));
});

app.MapPost("/api/v1/admin/persons/{personId:guid}/accounts", async (Guid personId, HttpContext context, CreatePersonAccountLinkRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    if (personId != body.PersonId) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid."); var path = $"/api/v1/admin/persons/{personId:D}/accounts"; var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var result = await administration.CreatePersonAccountLinkAsync(body, new(signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken); return result switch { AdministrationUpdateResult.Updated => Results.NoContent(), AdministrationUpdateResult.NotFound => Results.NotFound(), AdministrationUpdateResult.Conflict => Results.Conflict(), _ => Results.UnprocessableEntity() };
});

app.MapPut("/api/v1/admin/persons/{personId:guid}/accounts/{linkId:guid}/purposes", async (Guid personId, Guid linkId, HttpContext context, UpdatePersonAccountLinkPurposesRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    if (personId != body.PersonId || linkId != body.PersonAccountLinkId) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid."); var path = $"/api/v1/admin/persons/{personId:D}/accounts/{linkId:D}/purposes"; var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var result = await administration.UpdatePersonAccountLinkPurposesAsync(body, new(signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken); return result switch { AdministrationUpdateResult.Updated => Results.NoContent(), AdministrationUpdateResult.NotFound => Results.NotFound(), AdministrationUpdateResult.Conflict => Results.Conflict(), _ => Results.UnprocessableEntity() };
});

app.MapPost("/api/v1/admin/persons/{personId:guid}/accounts/{linkId:guid}/deactivate", async (Guid personId, Guid linkId, HttpContext context, DeactivatePersonAccountLinkRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    if (personId != body.PersonId || linkId != body.PersonAccountLinkId) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid."); var path = $"/api/v1/admin/persons/{personId:D}/accounts/{linkId:D}/deactivate"; var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var result = await administration.DeactivatePersonAccountLinkAsync(body, new(signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken); return result switch { AdministrationUpdateResult.Updated => Results.NoContent(), AdministrationUpdateResult.NotFound => Results.NotFound(), AdministrationUpdateResult.Conflict => Results.Conflict(), _ => Results.UnprocessableEntity() };
});

app.MapPost("/api/v1/admin/persons/{personId:guid}/accounts/{linkId:guid}/reactivate", async (Guid personId, Guid linkId, HttpContext context, ReactivatePersonAccountLinkRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    if (personId != body.PersonId || linkId != body.PersonAccountLinkId) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid."); var path = $"/api/v1/admin/persons/{personId:D}/accounts/{linkId:D}/reactivate"; var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var result = await administration.ReactivatePersonAccountLinkAsync(body, new(signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken); return result switch { AdministrationUpdateResult.Updated => Results.NoContent(), AdministrationUpdateResult.NotFound => Results.NotFound(), AdministrationUpdateResult.Conflict => Results.Conflict(), _ => Results.UnprocessableEntity() };
});

app.MapPost("/api/v1/admin/persons/{personId:guid}/mfa/totp-factors/query", async (Guid personId, HttpContext context, QueryAdministrationRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    var path = $"/api/v1/admin/persons/{personId:D}/mfa/totp-factors/query"; var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    return Results.Ok(await administration.ListTotpFactorsAsync(personId, cancellationToken));
});

app.MapPost("/api/v1/admin/persons/{personId:guid}/mfa/totp-factors/{totpFactorId:guid}/revoke", async (Guid personId, Guid totpFactorId, HttpContext context, RevokeTotpFactorRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    if (personId != body.PersonId || totpFactorId != body.TotpFactorId) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid."); var path = $"/api/v1/admin/persons/{personId:D}/mfa/totp-factors/{totpFactorId:D}/revoke"; var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var result = await administration.RevokeTotpFactorAsync(body, new(signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken); return result switch { AdministrationUpdateResult.Updated => Results.NoContent(), AdministrationUpdateResult.NotFound => Results.NotFound(), AdministrationUpdateResult.Conflict => Results.Conflict(), _ => Results.UnprocessableEntity() };
});

app.MapPost("/api/v1/admin/persons/{personId:guid}/mfa/fido2-credentials/query", async (Guid personId, HttpContext context, QueryAdministrationRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    var path = $"/api/v1/admin/persons/{personId:D}/mfa/fido2-credentials/query"; var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    return Results.Ok(await administration.ListFido2CredentialsAsync(personId, cancellationToken));
});

app.MapPost("/api/v1/admin/persons/{personId:guid}/mfa/fido2-credentials/{fido2CredentialId:guid}/revoke", async (Guid personId, Guid fido2CredentialId, HttpContext context, RevokeFido2CredentialRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    if (personId != body.PersonId || fido2CredentialId != body.Fido2CredentialId) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid."); var path = $"/api/v1/admin/persons/{personId:D}/mfa/fido2-credentials/{fido2CredentialId:D}/revoke"; var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var result = await administration.RevokeFido2CredentialAsync(body, new(signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken); return result switch { AdministrationUpdateResult.Updated => Results.NoContent(), AdministrationUpdateResult.NotFound => Results.NotFound(), AdministrationUpdateResult.Conflict => Results.Conflict(), _ => Results.UnprocessableEntity() };
});

app.MapPost("/api/v1/admin/directory-groups/search", async (HttpContext context, SearchDirectoryGroupsRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/admin/directory-groups/search";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    if (string.IsNullOrWhiteSpace(body.SearchTerm) || body.SearchTerm.Trim().Length is < 2 or > 128) return Results.UnprocessableEntity();
    return Results.Ok(await administration.SearchDirectoryGroupsAsync(body, cancellationToken));
});

app.MapPost("/api/v1/admin/entitlements/query", async (HttpContext context, QueryAdministrationRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    var signature = ReadSignature(context, "/api/v1/admin/entitlements/query", JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    if (!await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    return Results.Ok(await administration.ListDirectEntitlementsAsync(cancellationToken));
});

app.MapPost("/api/v1/admin/entitlement-subjects/query", async (HttpContext context, QueryAdministrationRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    var signature = ReadSignature(context, "/api/v1/admin/entitlement-subjects/query", JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    if (!await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    return Results.Ok(await administration.ListEligibleEntitlementSubjectsAsync(cancellationToken));
});

app.MapPost("/api/v1/admin/entitlements", async (HttpContext context, CreateDirectEntitlementRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/admin/entitlements";
    var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    if (!await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var result = await administration.CreateDirectEntitlementAsync(body, new(signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken);
    return result switch { AdministrationUpdateResult.Updated => Results.NoContent(), AdministrationUpdateResult.Conflict => Results.Conflict(), AdministrationUpdateResult.NotFound => Results.NotFound(), _ => Results.UnprocessableEntity() };
});

app.MapPut("/api/v1/admin/target-groups/{targetGroupId:guid}/policy", async (Guid targetGroupId, HttpContext context, UpdateTargetGroupPolicyRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    if (targetGroupId != body.TargetGroupId) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid.");
    var signature = ReadSignature(context, $"/api/v1/admin/target-groups/{targetGroupId:D}/policy", JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    if (!await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var result = await administration.UpdateTargetGroupPolicyAsync(body, new(signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken);
    return result switch { AdministrationUpdateResult.Updated => Results.NoContent(), AdministrationUpdateResult.NotFound => Results.NotFound(), AdministrationUpdateResult.Conflict => Results.Conflict(), _ => Results.UnprocessableEntity() };
});

app.MapPost("/api/v1/admin/target-groups", async (HttpContext context, CreateTargetGroupRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    const string path = "/api/v1/admin/target-groups"; var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var result = await administration.CreateTargetGroupAsync(body, new(signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken);
    return result switch { AdministrationUpdateResult.Updated => Results.NoContent(), AdministrationUpdateResult.Conflict => Results.Conflict(), AdministrationUpdateResult.NotFound => Results.NotFound(), _ => Results.UnprocessableEntity() };
});

app.MapPost("/api/v1/admin/target-groups/{targetGroupId:guid}/deactivate", async (Guid targetGroupId, HttpContext context, DeactivateTargetGroupRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    if (targetGroupId != body.TargetGroupId) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid.");
    var path = $"/api/v1/admin/target-groups/{targetGroupId:D}/deactivate"; var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var result = await administration.DeactivateTargetGroupAsync(body, new(signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken);
    return result switch { AdministrationUpdateResult.Updated => Results.NoContent(), AdministrationUpdateResult.NotFound => Results.NotFound(), AdministrationUpdateResult.Conflict => Results.Conflict(), _ => Results.UnprocessableEntity() };
});

app.MapPost("/api/v1/admin/target-groups/{targetGroupId:guid}/reactivate", async (Guid targetGroupId, HttpContext context, ReactivateTargetGroupRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    if (targetGroupId != body.TargetGroupId) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid.");
    var path = $"/api/v1/admin/target-groups/{targetGroupId:D}/reactivate"; var signature = ReadSignature(context, path, JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted || !await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var result = await administration.ReactivateTargetGroupAsync(body, new(signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken);
    return result switch { AdministrationUpdateResult.Updated => Results.NoContent(), AdministrationUpdateResult.NotFound => Results.NotFound(), AdministrationUpdateResult.Conflict => Results.Conflict(), _ => Results.UnprocessableEntity() };
});

app.MapPost("/api/v1/admin/entitlements/{entitlementId:guid}/deactivate", async (Guid entitlementId, HttpContext context, DeactivateDirectEntitlementRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    if (entitlementId != body.EntitlementId) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid.");
    var signature = ReadSignature(context, $"/api/v1/admin/entitlements/{entitlementId:D}/deactivate", JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    if (!await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var result = await administration.DeactivateDirectEntitlementAsync(body, new(signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken);
    return result switch { AdministrationUpdateResult.Updated => Results.NoContent(), AdministrationUpdateResult.NotFound => Results.NotFound(), AdministrationUpdateResult.Conflict => Results.Conflict(), _ => Results.UnprocessableEntity() };
});

app.MapPut("/api/v1/admin/entitlements/{entitlementId:guid}/validity", async (Guid entitlementId, HttpContext context, UpdateDirectEntitlementValidityRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    if (entitlementId != body.EntitlementId) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid.");
    var signature = ReadSignature(context, $"/api/v1/admin/entitlements/{entitlementId:D}/validity", JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    if (!await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var result = await administration.UpdateDirectEntitlementValidityAsync(body, new(signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken);
    return result switch { AdministrationUpdateResult.Updated => Results.NoContent(), AdministrationUpdateResult.NotFound => Results.NotFound(), AdministrationUpdateResult.Conflict => Results.Conflict(), _ => Results.UnprocessableEntity() };
});

app.MapPut("/api/v1/admin/entitlements/{entitlementId:guid}/constraints", async (Guid entitlementId, HttpContext context, UpdateDirectEntitlementConstraintsRequest body, ValidateApiRequestSignatureUseCase signatureValidator, IApplicationAccessAuthorizer accessAuthorizer, AdministrationUseCases administration, CancellationToken cancellationToken) =>
{
    if (entitlementId != body.EntitlementId) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request is invalid.");
    var signature = ReadSignature(context, $"/api/v1/admin/entitlements/{entitlementId:D}/constraints", JsonSerializer.SerializeToUtf8Bytes(body));
    if (signature is null || (await signatureValidator.ExecuteAsync(signature, cancellationToken)).Kind != ApiRequestReplayRegistrationKind.Accepted) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    if (!await accessAuthorizer.IsAuthorizedAsync(body.Actor.ObjectGuid, ApplicationAccessLevel.Administrator, cancellationToken)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Request is not authorized.");
    var result = await administration.UpdateDirectEntitlementConstraintsAsync(body, new(signature.CorrelationId, signature.KeyId, context.Connection.RemoteIpAddress?.ToString(), ReadClientIp(context)), cancellationToken);
    return result switch { AdministrationUpdateResult.Updated => Results.NoContent(), AdministrationUpdateResult.NotFound => Results.NotFound(), AdministrationUpdateResult.Conflict => Results.Conflict(), _ => Results.UnprocessableEntity() };
});

// Informational only, never an authorization or trust signal: the browser's own remote IP as captured by
// Web on the incoming request and forwarded unsigned via X-Client-Ip - distinct from
// context.Connection.RemoteIpAddress here, which is always Web's own IP (the API never talks to the
// browser directly).
static string? ReadClientIp(HttpContext context)
{
    var value = context.Request.Headers["X-Client-Ip"].ToString();
    return string.IsNullOrWhiteSpace(value) ? null : value;
}

static ApiRequestSignature? ReadSignature(HttpContext context, string expectedPath, byte[] body)
{
    var headers = context.Request.Headers;
    if (!Guid.TryParse(headers["X-Request-Id"], out var requestId) || !Guid.TryParse(headers["X-Correlation-Id"], out var correlationId) || !DateTimeOffset.TryParse(headers["X-Issued-Utc"], out var issuedUtc)) return null;
    var version = headers["X-Api-Signature-Version"].ToString(); var keyId = headers["X-Api-Key-Id"].ToString(); var nonce = headers["X-Api-Nonce"].ToString(); var encodedSignature = headers["X-Api-Signature"].ToString();
    if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(nonce) || string.IsNullOrWhiteSpace(encodedSignature)) return null;
    var unsigned = new ApiRequestSignature(version, keyId, requestId, correlationId, issuedUtc, nonce, context.Request.Method, expectedPath, ApiRequestCanonicalizer.CanonicalizeQuery(context.Request.Query.SelectMany(pair => pair.Value.Select(value => new KeyValuePair<string, string?>(pair.Key, value)))), ApiRequestCanonicalizer.ComputeBodyHash(body), context.Request.ContentType ?? string.Empty, "1", string.Empty);
    return unsigned with { Signature = encodedSignature };
}

app.Run();

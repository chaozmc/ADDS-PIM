using System.Net;
using System.Text.Json;
using ADDS.PIM.AdWorker.ActiveDirectory;
using ADDS.PIM.AdWorker.Hosting;
using ADDS.PIM.Application.Worker;
using ADDS.PIM.Contracts.Worker.V1;
using ADDS.PIM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging.EventLog;
using Microsoft.Extensions.Options;

static string RequiredArgument(string[] arguments, string name)
{
    var index = Array.IndexOf(arguments, name);
    return index >= 0 && index + 1 < arguments.Length
        ? arguments[index + 1]
        : throw new ArgumentException($"{name} is required.");
}

var ldapTest = args.Contains("--ldap-ttl-test", StringComparer.Ordinal);
if (ldapTest)
{
    var resultFile = RequiredArgument(args, "--result-file");
    try
    {
        var service = new LdapTemporaryGroupMembershipService(Options.Create(new ActiveDirectoryOptions
        {
            DomainController = RequiredArgument(args, "--domain-controller")
        }));
        var result = await service.AddAndVerifyAsync(
            new TemporaryGroupMembershipOperation(
                Guid.Parse(RequiredArgument(args, "--target-account-object-guid")),
                Guid.Parse(RequiredArgument(args, "--target-group-object-guid")),
                long.Parse(RequiredArgument(args, "--requested-ttl-seconds"), System.Globalization.CultureInfo.InvariantCulture)),
            CancellationToken.None);
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        await File.WriteAllTextAsync(resultFile, json);
        Console.WriteLine(json);
        Environment.ExitCode = result.Kind == TemporaryGroupMembershipResultKind.Verified ? 0 : 1;
    }
    catch (Exception exception)
    {
        await File.WriteAllTextAsync(resultFile, System.Text.Json.JsonSerializer.Serialize(new { error = exception.GetType().Name, message = exception.Message }));
        Environment.ExitCode = 1;
    }

    return;
}

var settings = WorkerHostSettings.LoadFromLocalMachine();
var certificateStore = new WorkerCertificateStore(settings);
var serverCertificate = certificateStore.LoadServerCertificate();
var workerClientCertificate = certificateStore.LoadWorkerClientCertificate();
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null
});

builder.Host.UseWindowsService();
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["ConnectionStrings:PimDatabase"] = settings.SqlConnectionString,
    ["ActiveDirectory:DomainController"] = settings.DomainController
});
builder.Logging.ClearProviders();
builder.Logging.AddEventLog(new EventLogSettings
{
    LogName = WorkerEventLog.LogName,
    SourceName = WorkerEventLog.SourceName
});
builder.Logging.AddFilter<EventLogLoggerProvider>(level => level >= LogLevel.Information);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(settings.BindAddress, settings.Port, listen => listen.UseHttps(https =>
    {
        https.ServerCertificate = serverCertificate;
        https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
        https.ClientCertificateValidation = (certificate, _, errors) =>
            certificateStore.IsAllowedApiClientCertificate(certificate, errors);
    }));
});

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(certificateStore);
builder.Services.AddSingleton(workerClientCertificate);
builder.Services.AddOptions<ActiveDirectoryOptions>()
    .BindConfiguration(ActiveDirectoryOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.DomainController), "ActiveDirectory:DomainController is required.")
    .ValidateOnStart();
builder.Services.AddSingleton<ITemporaryGroupMembershipService, LdapTemporaryGroupMembershipService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddPimSqlServerPersistence(settings.SqlConnectionString);
builder.Services.AddScoped<WorkerCommandProcessor>();

var app = builder.Build();
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ADDS.PIM.AdWorker.Unhandled");
    logger.LogError(WorkerEventLog.UnexpectedFailure, context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error,
        "Unhandled Worker request failure.");
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new { error = "Worker processing failed." });
}));

app.MapGet("/internal/v1/health/live", () => Results.Ok(new { status = "live" }));
app.MapPost("/internal/v1/temporary-group-memberships", async (
    HttpContext context,
    WorkerHostSettings workerSettings,
    WorkerCommandProcessor processor,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("ADDS.PIM.AdWorker.Commands");
    TemporaryGroupMembershipCommand? command;
    try
    {
        command = await context.Request.ReadFromJsonAsync<TemporaryGroupMembershipCommand>(cancellationToken);
    }
    catch (JsonException exception)
    {
        logger.LogWarning(WorkerEventLog.CommandRejected, exception, "Worker command rejected because its JSON representation is invalid.");
        return Results.BadRequest(new { error = "Worker command is invalid." });
    }

    if (command is null)
    {
        logger.LogWarning(WorkerEventLog.CommandRejected, "Worker command rejected because its request body is empty.");
        return Results.BadRequest(new { error = "Worker command is invalid." });
    }

    var certificate = await context.Connection.GetClientCertificateAsync(cancellationToken);
    var certificateThumbprint = WorkerHostSettings.NormalizeThumbprint(certificate?.Thumbprint);
    using var scope = logger.BeginScope(new Dictionary<string, object?>
    {
        ["CommandId"] = command.CommandId,
        ["RequestId"] = command.RequestId,
        ["CorrelationId"] = command.CorrelationId,
        ["ClientCertificateThumbprint"] = certificateThumbprint
    });

    logger.LogInformation(WorkerEventLog.CommandReceived, "Worker command received.");
    if (!WorkerCommandTimeWindow.IsCurrent(command.IssuedUtc, timeProvider.GetUtcNow(), workerSettings.CommandMaxAgeSeconds))
    {
        logger.LogWarning(WorkerEventLog.CommandRejected, "Worker command rejected because its timestamp is outside the accepted window.");
        return Results.BadRequest(new { error = "Worker command is not current." });
    }

    try
    {
        var result = await processor.ExecuteAsync(command, certificateThumbprint, cancellationToken);
        if (result is null)
        {
            logger.LogWarning(WorkerEventLog.CommandRejected, "Worker command was rejected by integrity or replay protection.");
            return Results.Conflict(new { error = "Worker command was rejected." });
        }

        var level = result.Kind == TemporaryGroupMembershipResultKind.Verified ? LogLevel.Information : LogLevel.Warning;
        logger.Log(level, WorkerEventLog.CommandCompleted, "Worker command completed with result {ResultKind} and error code {ErrorCode}.", result.Kind, result.ErrorCode);
        return Results.Ok(result);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        logger.LogWarning(WorkerEventLog.CommandCancelled, "Worker command was cancelled.");
        return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
    }
    catch (Exception exception)
    {
        logger.LogError(WorkerEventLog.CommandFailed, exception, "Worker command failed unexpectedly.");
        return Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Worker command failed.");
    }
});

app.Logger.LogInformation(WorkerEventLog.ServiceStarted, "ADDS PIM Worker is listening on configured private HTTPS binding {Address}:{Port}.", settings.BindAddress, settings.Port);
app.Run();

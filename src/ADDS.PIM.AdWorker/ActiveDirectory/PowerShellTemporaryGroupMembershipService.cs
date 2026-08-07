using System.Diagnostics;
using System.Text.Json;
using ADDS.PIM.Application.Worker;
using Microsoft.Extensions.Options;

namespace ADDS.PIM.AdWorker.ActiveDirectory;

public sealed class PowerShellTemporaryGroupMembershipService(
    IOptions<ActiveDirectoryOptions> options,
    IHostEnvironment hostEnvironment,
    ILogger<PowerShellTemporaryGroupMembershipService> logger) : ITemporaryGroupMembershipService
{
    public async Task<TemporaryGroupMembershipResult> AddAndVerifyAsync(TemporaryGroupMembershipOperation operation, CancellationToken cancellationToken)
    {
        if (operation.TargetAccountObjectGuid == Guid.Empty || operation.TargetGroupObjectGuid == Guid.Empty || operation.RequestedTtlSeconds <= 0)
        {
            throw new ArgumentException("The AD operation is incomplete.", nameof(operation));
        }

        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.DomainController))
        {
            throw new InvalidOperationException("ActiveDirectory:DomainController is required.");
        }

        var scriptPath = Path.Combine(hostEnvironment.ContentRootPath, "Scripts", "Invoke-TtlMembership.ps1");
        var startInfo = new ProcessStartInfo(settings.PowerShellExecutablePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("AllSigned");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-DomainController");
        startInfo.ArgumentList.Add(settings.DomainController);
        startInfo.ArgumentList.Add("-TargetAccountObjectGuid");
        startInfo.ArgumentList.Add(operation.TargetAccountObjectGuid.ToString("D"));
        startInfo.ArgumentList.Add("-TargetGroupObjectGuid");
        startInfo.ArgumentList.Add(operation.TargetGroupObjectGuid.ToString("D"));
        startInfo.ArgumentList.Add("-RequestedTtlSeconds");
        startInfo.ArgumentList.Add(operation.RequestedTtlSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the fixed AD PowerShell operation.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            logger.LogWarning("Fixed AD PowerShell operation failed with exit code {ExitCode}.", process.ExitCode);
            return new TemporaryGroupMembershipResult(TemporaryGroupMembershipResultKind.PowerShellFailure, settings.DomainController, null, "PowerShellExecutionFailed");
        }

        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            var kind = root.GetProperty("kind").GetString();
            long? remaining = root.TryGetProperty("remainingTtlSeconds", out var ttl) && ttl.ValueKind == JsonValueKind.Number ? ttl.GetInt64() : null;
            return kind switch
            {
                "Verified" => new TemporaryGroupMembershipResult(TemporaryGroupMembershipResultKind.Verified, settings.DomainController, remaining, null),
                "ExistingMembership" => new TemporaryGroupMembershipResult(TemporaryGroupMembershipResultKind.ExistingMembership, settings.DomainController, remaining, "ExistingMembership"),
                "VerificationFailed" => new TemporaryGroupMembershipResult(TemporaryGroupMembershipResultKind.VerificationFailed, settings.DomainController, remaining, "VerificationFailed"),
                _ => new TemporaryGroupMembershipResult(TemporaryGroupMembershipResultKind.ActiveDirectoryFailure, settings.DomainController, null, "ActiveDirectoryOperationFailed")
            };
        }
        catch (JsonException)
        {
            return new TemporaryGroupMembershipResult(TemporaryGroupMembershipResultKind.PowerShellFailure, settings.DomainController, null, "InvalidPowerShellResult");
        }
    }
}

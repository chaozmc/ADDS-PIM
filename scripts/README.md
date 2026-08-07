# Setup scripts

These PowerShell scripts cover the manual/bootstrap deployment path for ADDS-PIM: publishing the Web/API applications, configuring IIS, installing the isolated AD Worker as a Windows service, provisioning MVP-scope authorization data, and registering the Web request-signing certificate. None of them are Authenticode-signed in this repository - sign them yourself with your own code-signing certificate if your environment's execution policy requires it (`AllSigned`).

They are independent, composable scripts rather than a single installer. There is no `ADDS.PIM.Setup` MSI/installer project yet; if one is added later, it would likely wrap this same logic behind a guided UI rather than replace it.

| Script | Purpose |
|---|---|
| `Publish-ADDS-PIM-WebApi.ps1` | `dotnet publish` the Web and API projects to a target folder. |
| `Install-ADDS-PIM-WebApi.ps1` | Configure IIS application pools/sites (gMSA identity, HTTPS bindings) for the published Web/API output. |
| `Install-ADDS-PIM-Worker.ps1` | End-to-end AD Worker install: database bootstrap + EF Core migrations, host configuration, Windows service creation under a gMSA. |
| `Install-AdWorkerHostConfiguration.ps1` | Writes just the Worker's registry-based host configuration (called by `Install-ADDS-PIM-Worker.ps1`, or standalone to reconfigure an existing install). |
| `Uninstall-ADDS-PIM-Worker.ps1` | Removes the Worker Windows service (and optionally its registry configuration). |
| `Register-ADDS-PIM-WebSigningCertificate.ps1` | One-time bootstrap (or rotation) of the Web-to-API request-signing certificate in the database. |
| `Initialize-ADDS-PIM-MvpAuthorization.ps1` | Seeds a directory scope, an actor/target account pair, and example TTL group policies directly in SQL Server, for standing up a demo/test environment without going through the admin UI. |
| `Test-AddsPimPrerequisites.ps1` | Checks domain membership, RSAT AD tools, and the AD PAM optional feature on the current host. |
| `Run-AdWorkerHostProcess.ps1` | Launches the Worker process with redirected stdout/stderr (used by the scheduled-task verification flow). |
| `Start-AdWorkerHostVerification.ps1` | Runs the Worker once under a scheduled task as the Worker's gMSA, to verify the gMSA can actually start the process before wiring up the real service. |
| `Invoke-AddsPimTtlWorkerTest.ps1` | One-shot scheduled-task invocation of the Worker's TTL membership grant, for smoke-testing an AD Worker install end to end. |

All hostnames, domain names, and gMSA account names in these scripts are either required parameters or use placeholder `example.org` / `EXAMPLE\...` defaults - replace them with your own environment's values on every run.

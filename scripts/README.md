# Setup scripts

## Full install: `installation/`

For a fresh Windows Server, run `installation\Install-ADDS-PIM.ps1` - the
single, interactive entry point that takes a host from bare prerequisites
(IIS, .NET Hosting Bundle, RSAT AD tools, optional SQL Express) through a
running, usable ADDS-PIM instance (certificates, database, IIS sites,
Worker service, `appsettings.Production.json`). It asks for every variable
it needs and saves your answers so a failed run can be resumed with
`-StartAtStep <n>` instead of starting over. See
`installation/README.md` for the full step breakdown. It does not call any
of the scripts below - it is self-contained by design, so nothing it does
is hidden in a script outside that folder.

## Individual scripts

The scripts below remain useful for surgical one-off repairs against an
already-installed environment (for example, re-granting database
permissions after a gMSA change), and as a composable alternative to the
single installer. None of them are Authenticode-signed in this repository
- sign them yourself with your own code-signing certificate if your
environment's execution policy requires it (`AllSigned`).

| Script | Purpose |
|---|---|
| `Publish-ADDS-PIM-WebApi.ps1` | `dotnet publish` the Web and API projects to a target folder. |
| `Install-ADDS-PIM-WebApi.ps1` | Configure IIS application pools/sites (gMSA identity, HTTPS bindings) for the published Web/API output. |
| `Install-ADDS-PIM-Worker.ps1` | End-to-end AD Worker install: database bootstrap + EF Core migrations, host configuration, Windows service creation under a gMSA. |
| `Install-AdWorkerHostConfiguration.ps1` | Writes just the Worker's registry-based host configuration (called by `Install-ADDS-PIM-Worker.ps1`, or standalone to reconfigure an existing install). |
| `Grant-ADDS-PIM-DatabasePermissions.ps1` | Standalone re-run of the Worker/API gMSA database login, role membership and table grants against an already-installed database, without touching migrations, the Worker service, or IIS. |
| `Update-ADDS-PIM-Database.ps1` | Comfortable re-run of "apply pending EF Core migrations, then re-grant gMSA permissions" against an already-installed environment (e.g. a separate int/test system) after pulling in a change that added a migration. Calls `Grant-ADDS-PIM-DatabasePermissions.ps1` itself; does not publish or restart Web/API. |
| `Initialize-ADDS-PIM-DirectoryScope.ps1` | One-time, idempotent creation of the `dbo.DirectoryScopes` row for this environment's configured `Directory:ScopeId` - required before any Person/DirectoryAccount/TargetGroup can be created through the admin UI. Seeds no other data. |
| `Uninstall-ADDS-PIM-Worker.ps1` | Removes the Worker Windows service (and optionally its registry configuration). |
| `Register-ADDS-PIM-WebSigningCertificate.ps1` | One-time bootstrap (or rotation) of the Web-to-API request-signing certificate in the database. |
| `Initialize-ADDS-PIM-MvpAuthorization.ps1` | Seeds a directory scope, an actor/target account pair, and example TTL group policies directly in SQL Server, for standing up a demo/test environment without going through the admin UI. |
| `Test-AddsPimPrerequisites.ps1` | Checks domain membership, RSAT AD tools, and the AD PAM optional feature on the current host. |
| `Run-AdWorkerHostProcess.ps1` | Launches the Worker process with redirected stdout/stderr (used by the scheduled-task verification flow). |
| `Start-AdWorkerHostVerification.ps1` | Runs the Worker once under a scheduled task as the Worker's gMSA, to verify the gMSA can actually start the process before wiring up the real service. |
| `Invoke-AddsPimTtlWorkerTest.ps1` | One-shot scheduled-task invocation of the Worker's TTL membership grant, for smoke-testing an AD Worker install end to end. |

All hostnames, domain names, and gMSA account names in these scripts are either required parameters or use placeholder `example.org` / `EXAMPLE\...` defaults - replace them with your own environment's values on every run.

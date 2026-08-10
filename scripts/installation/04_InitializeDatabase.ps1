#Requires -Version 5.1
<#
.SYNOPSIS
Step 4 of the ADDS-PIM installer: database schema, permissions, and the
DirectoryScope row required before the admin UI can do anything.

.DESCRIPTION
Applies EF Core migrations, runs the versioned
database\Install-ADDS-PIM-Database.sql bootstrap (before AND after
migrations - it is idempotent and grants the Worker/API gMSA their runtime
permissions on whatever tables currently exist), then creates the
dbo.DirectoryScopes row for this environment via
database\Initialize-ADDS-PIM-DirectoryScope.sql. Without that row, every
first admin UI action (onboarding a Person, registering a target group)
fails with an opaque foreign-key violation - the exact issue hit and fixed
on 2026-08-10.

These two .sql files are the versioned, authoritative bootstrap inputs
(dev-docs/10-operations/installation-and-setup.md: "Als Bootstrap-Input ist
ausschliesslich die versionierte Datei ... zu verwenden") - referencing
them here is not the same as calling another orchestration script; it is
the same data file the documentation requires.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [string] $SqlServerInstance,
    [Parameter(Mandatory)] [string] $WorkerGmsaAccount,
    [Parameter(Mandatory)] [string] $ApiGmsaAccount,
    [Parameter(Mandatory)] [guid] $DirectoryScopeId,
    [Parameter(Mandatory)] [string] $DomainDnsName,
    [Parameter(Mandatory)] [string] $ForestDnsName,
    [Parameter(Mandatory)] [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })] [string] $InfrastructureProjectPath,
    [ValidateNotNullOrEmpty()] [string] $DatabaseName = 'ADDS_PIM',
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string] $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)

. (Join-Path $PSScriptRoot '_Common.ps1')
Write-AddsPimStepHeader '04 - Database'

$bootstrapScript = Join-Path $RepositoryRoot 'database\Install-ADDS-PIM-Database.sql'
$directoryScopeScript = Join-Path $RepositoryRoot 'database\Initialize-ADDS-PIM-DirectoryScope.sql'
foreach ($path in @($bootstrapScript, $directoryScopeScript)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required database script not found: $path" }
}

$sqlConnectionString = "Server=$SqlServerInstance;Database=$DatabaseName;Integrated Security=True;TrustServerCertificate=True"

if ($PSCmdlet.ShouldProcess("$SqlServerInstance/$DatabaseName", 'Bootstrap database/login/roles before migrations')) {
    Invoke-AddsPimSqlCmd -Arguments @('-S', $SqlServerInstance, '-E', '-C', '-b', '-i', $bootstrapScript,
        '-v', "DatabaseName=$DatabaseName", "WorkerLogin=$WorkerGmsaAccount", "ApiLogin=$ApiGmsaAccount")
}

if ($PSCmdlet.ShouldProcess($DatabaseName, 'Apply EF Core migrations')) {
    Push-Location $RepositoryRoot
    try {
        & dotnet tool restore | Write-Host
        if ($LASTEXITCODE -ne 0) { throw "EF Core tool restore failed with exit code $LASTEXITCODE." }
        $migrationOutput = & dotnet tool run dotnet-ef database update --project $InfrastructureProjectPath --connection $sqlConnectionString 2>&1
        if ($LASTEXITCODE -ne 0) { throw "EF Core database migration failed with exit code $LASTEXITCODE. $($migrationOutput -join [Environment]::NewLine)" }
        $migrationOutput | Write-Host
    }
    finally { Pop-Location }
}

if ($PSCmdlet.ShouldProcess("$SqlServerInstance/$DatabaseName", 'Re-run bootstrap so newly-migrated tables get runtime grants')) {
    Invoke-AddsPimSqlCmd -Arguments @('-S', $SqlServerInstance, '-E', '-C', '-b', '-i', $bootstrapScript,
        '-v', "DatabaseName=$DatabaseName", "WorkerLogin=$WorkerGmsaAccount", "ApiLogin=$ApiGmsaAccount")
}

if ($PSCmdlet.ShouldProcess("$SqlServerInstance/$DatabaseName", "Create DirectoryScope $DirectoryScopeId")) {
    Invoke-AddsPimSqlCmd -Arguments @('-S', $SqlServerInstance, '-E', '-C', '-d', $DatabaseName, '-b', '-i', $directoryScopeScript,
        '-v', "DirectoryScopeId=$DirectoryScopeId", "DomainDnsName=$DomainDnsName", "ForestDnsName=$ForestDnsName")
}

Write-Host ''
Write-Host "Step 04 complete: database ready on $SqlServerInstance/$DatabaseName, DirectoryScope $DirectoryScopeId exists."

[PSCustomObject]@{
    SqlConnectionString = $sqlConnectionString
}

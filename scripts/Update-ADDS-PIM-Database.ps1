[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $SqlConnectionString,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $SqlServerInstance,

    [Parameter(Mandatory)]
    [ValidatePattern('^[^\\]+\\[^\\]+\$$')]
    [string] $WorkerGmsaAccount,

    [Parameter(Mandatory)]
    [ValidatePattern('^[^\\]+\\[^\\]+\$$')]
    [string] $ApiGmsaAccount,

    [ValidateNotNullOrEmpty()]
    [string] $DatabaseName = 'ADDS_PIM',

    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string] $InfrastructureProjectPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\ADDS.PIM.Infrastructure\ADDS.PIM.Infrastructure.csproj'),

    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string] $DatabaseBootstrapScript = (Join-Path (Split-Path -Parent $PSScriptRoot) 'database\Install-ADDS-PIM-Database.sql'),

    [switch] $SkipGrants
)

# Comfortable re-run of "apply pending EF Core migrations, then re-grant the
# Worker/API gMSA database permissions" against an already-installed
# environment (e.g. a separate int/test system) after pulling in a change
# that added a migration. Safe to run repeatedly:
#   - `dotnet-ef database update` only applies migrations not yet recorded
#     in __EFMigrationsHistory; running it again with nothing pending is a
#     no-op.
#   - Install-ADDS-PIM-Database.sql is entirely guarded (IF NOT EXISTS /
#     OBJECT_ID checks), so re-granting only fills in whatever is missing -
#     see Grant-ADDS-PIM-DatabasePermissions.ps1, which this script calls
#     for that half of the work rather than duplicating it.
#
# This does NOT publish/restart the Web or API sites - re-run
# Publish-ADDS-PIM-WebApi.ps1 (with both IIS app pools stopped first, since
# in-process hosting locks the DLLs) and cycle the app pools separately once
# the new code needs to be live.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-SqlCmd([string[]] $Arguments) {
    $sqlcmd = Get-Command sqlcmd.exe -ErrorAction Stop
    & $sqlcmd.Source @Arguments
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed with exit code $LASTEXITCODE." }
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$infrastructureProject = (Resolve-Path -LiteralPath $InfrastructureProjectPath).Path
$grantScript = Join-Path $PSScriptRoot 'Grant-ADDS-PIM-DatabasePermissions.ps1'
if (-not (Test-Path -LiteralPath $grantScript -PathType Leaf)) {
    throw "Grant-ADDS-PIM-DatabasePermissions.ps1 was not found next to this script at $grantScript."
}

if ($PSCmdlet.ShouldProcess("$SqlServerInstance/$DatabaseName", 'Apply pending EF Core migrations')) {
    Push-Location $repositoryRoot
    try {
        & dotnet tool restore
        if ($LASTEXITCODE -ne 0) { throw "EF Core tool restore failed with exit code $LASTEXITCODE." }

        Write-Output 'Migrations known to the project (not-yet-applied ones are marked "(Pending)"):'
        & dotnet tool run dotnet-ef migrations list --project $infrastructureProject --connection $SqlConnectionString --no-build
        if ($LASTEXITCODE -ne 0) { throw "Listing EF Core migrations failed with exit code $LASTEXITCODE." }

        $migrationOutput = & dotnet tool run dotnet-ef database update --project $infrastructureProject --connection $SqlConnectionString 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "EF Core database migration failed with exit code $LASTEXITCODE. $($migrationOutput -join [Environment]::NewLine)"
        }
        $migrationOutput | Write-Output
    }
    finally {
        Pop-Location
    }
}

if (-not $SkipGrants) {
    & $grantScript -WorkerGmsaAccount $WorkerGmsaAccount -ApiGmsaAccount $ApiGmsaAccount `
        -SqlServerInstance $SqlServerInstance -DatabaseName $DatabaseName `
        -DatabaseBootstrapScript $DatabaseBootstrapScript -WhatIf:$WhatIfPreference
}
else {
    Write-Output 'Skipped grant re-run (-SkipGrants was set). Run Grant-ADDS-PIM-DatabasePermissions.ps1 separately if permissions also need refreshing.'
}

if ($PSCmdlet.ShouldProcess("$SqlServerInstance/$DatabaseName", 'Report the latest applied migration')) {
    Invoke-SqlCmd @('-S', $SqlServerInstance, '-E', '-C', '-d', $DatabaseName,
        '-Q', 'SET NOCOUNT ON; SELECT TOP 1 MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC;')
}

Write-Output "Database update completed for $SqlServerInstance/$DatabaseName. Remember to publish and restart Web/API separately if new application code needs to go live."

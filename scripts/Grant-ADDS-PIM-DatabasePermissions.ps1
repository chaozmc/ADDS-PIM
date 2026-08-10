[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[^\\]+\\[^\\]+\$$')]
    [string] $WorkerGmsaAccount,

    [Parameter(Mandatory)]
    [ValidatePattern('^[^\\]+\\[^\\]+\$$')]
    [string] $ApiGmsaAccount,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $SqlServerInstance,

    [ValidateNotNullOrEmpty()]
    [string] $DatabaseName = 'ADDS_PIM',

    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string] $DatabaseBootstrapScript = (Join-Path (Split-Path -Parent $PSScriptRoot) 'database\Install-ADDS-PIM-Database.sql')
)

# Standalone re-run of the database permission bootstrap for an existing
# ADDS-PIM install, e.g. when the Worker/API gMSAs changed or an earlier
# install run never granted the API gMSA its SQL login, role membership or
# table grants. Safe to run repeatedly: every statement in
# Install-ADDS-PIM-Database.sql is guarded (IF NOT EXISTS / OBJECT_ID
# checks), so it only fills in whatever is missing.
#
# There is deliberately no -WebGmsaAccount parameter: Web never talks to SQL
# Server directly (only via the API), so it has no database login to grant.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-SqlCmd([string[]] $Arguments) {
    $sqlcmd = Get-Command sqlcmd.exe -ErrorAction Stop
    & $sqlcmd.Source @Arguments
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed with exit code $LASTEXITCODE." }
}

$bootstrapScript = (Resolve-Path -LiteralPath $DatabaseBootstrapScript).Path

if ($PSCmdlet.ShouldProcess("$SqlServerInstance/$DatabaseName", 'Grant Worker/API gMSA database permissions')) {
    Invoke-SqlCmd @('-S', $SqlServerInstance, '-E', '-C', '-b', '-i', $bootstrapScript,
        '-v', "DatabaseName=$DatabaseName", "WorkerLogin=$WorkerGmsaAccount", "ApiLogin=$ApiGmsaAccount")
}

Write-Output "Granted database permissions for Worker gMSA '$WorkerGmsaAccount' and API gMSA '$ApiGmsaAccount' on $SqlServerInstance/$DatabaseName."

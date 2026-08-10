[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [guid] $DirectoryScopeId,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $DomainDnsName,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $ForestDnsName,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $SqlServerInstance,

    [ValidateNotNullOrEmpty()]
    [string] $DatabaseName = 'ADDS_PIM',

    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string] $DirectoryScopeScript = (Join-Path (Split-Path -Parent $PSScriptRoot) 'database\Initialize-ADDS-PIM-DirectoryScope.sql')
)

# One-time, idempotent bootstrap that creates the dbo.DirectoryScopes row a
# fresh ADDS-PIM install needs before anything else can be created through
# the admin UI: every Person/DirectoryAccount/TargetGroup carries a required
# foreign key to DirectoryScopes.DirectoryScopeId. Pass the exact same
# DirectoryScopeId configured under Directory:ScopeId in this environment's
# Web and API appsettings.Production.json.
#
# This does NOT seed any Persons, target groups, policies, or entitlements -
# that is real operational data and belongs in the admin UI, not a script.
# For a disposable demo/test environment with sample data instead, see
# Initialize-ADDS-PIM-MvpAuthorization.ps1.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-SqlCmd([string[]] $Arguments) {
    $sqlcmd = Get-Command sqlcmd.exe -ErrorAction Stop
    & $sqlcmd.Source @Arguments
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed with exit code $LASTEXITCODE." }
}

$scriptPath = (Resolve-Path -LiteralPath $DirectoryScopeScript).Path

if ($PSCmdlet.ShouldProcess("$SqlServerInstance/$DatabaseName", "Create DirectoryScope $DirectoryScopeId")) {
    Invoke-SqlCmd @('-S', $SqlServerInstance, '-E', '-C', '-d', $DatabaseName, '-b', '-i', $scriptPath,
        '-v', "DirectoryScopeId=$DirectoryScopeId", "DomainDnsName=$DomainDnsName", "ForestDnsName=$ForestDnsName")
}

Write-Output "DirectoryScope $DirectoryScopeId ready on $SqlServerInstance/$DatabaseName."

#Requires -Version 5.1
<#
.SYNOPSIS
Step 6 of the ADDS-PIM installer: write appsettings.Production.json for
Web and API.

.DESCRIPTION
Nothing in the repository automated this before 2026-08-10 - the checked-in
appsettings.json intentionally contains only <...> placeholders (never real
values, see dev-docs "appsettings Production overlay" note), and every real
per-host value belongs in an untracked appsettings.Production.json next to
the published appsettings.json. ASP.NET Core's default
ASPNETCORE_ENVIRONMENT=Production under IIS merges it in automatically.
Writing this by hand was exactly what caused the original appsettings leak
this installer exists to prevent a repeat of.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [string] $WebPublishPath,
    [Parameter(Mandatory)] [string] $ApiPublishPath,
    [Parameter(Mandatory)] [string] $WebHostName,
    [Parameter(Mandatory)] [string] $ApiHostName,
    [Parameter(Mandatory)] [string] $WorkerHostName,
    [Parameter(Mandatory)] [int] $WorkerPort,
    [Parameter(Mandatory)] [guid] $DirectoryScopeId,
    [Parameter(Mandatory)] [string] $DomainDnsName,
    [Parameter(Mandatory)] [string] $ForestDnsName,
    [Parameter(Mandatory)] [string] $DomainController,
    [Parameter(Mandatory)] [guid] $UsersGroupObjectGuid,
    [Parameter(Mandatory)] [guid] $AdministratorsGroupObjectGuid,
    [Parameter(Mandatory)] [string] $SigningCertificateThumbprint,
    [Parameter(Mandatory)] [string] $SigningKeyId,
    [Parameter(Mandatory)] [string] $SqlConnectionString,
    [Parameter(Mandatory)] [string] $ApiWorkerClientCertificateThumbprint,
    [Parameter(Mandatory)] [string] $WorkerTlsCertificateThumbprint,
    [Parameter(Mandatory)] [string] $TotpProtectionCertificateThumbprint
)

. (Join-Path $PSScriptRoot '_Common.ps1')
Write-AddsPimStepHeader '06 - Write appsettings.Production.json'

$workerEndpoint = "https://$($WorkerHostName):$WorkerPort/internal/v1/temporary-group-memberships"

$webSettings = [ordered]@{
    Fido2 = [ordered]@{ RelyingPartyId = $WebHostName; Origin = "https://$WebHostName" }
    Api   = [ordered]@{ BaseAddress = "https://$ApiHostName/" }
    Directory = [ordered]@{ ScopeId = $DirectoryScopeId.ToString(); DomainDnsName = $DomainDnsName; ForestDnsName = $ForestDnsName }
    ApplicationAccess = [ordered]@{
        DomainController = $DomainController
        UsersGroupObjectGuid = $UsersGroupObjectGuid.ToString()
        AdministratorsGroupObjectGuid = $AdministratorsGroupObjectGuid.ToString()
    }
    OperatorTest = [ordered]@{
        SigningCertificateThumbprint = (ConvertTo-AddsPimNormalizedThumbprint $SigningCertificateThumbprint)
        SigningKeyId = $SigningKeyId
    }
}

$apiSettings = [ordered]@{
    Fido2 = [ordered]@{ RelyingPartyId = $WebHostName; Origin = "https://$WebHostName" }
    Directory = [ordered]@{ ScopeId = $DirectoryScopeId.ToString(); DomainDnsName = $DomainDnsName; ForestDnsName = $ForestDnsName }
    ApplicationAccess = [ordered]@{
        DomainController = $DomainController
        UsersGroupObjectGuid = $UsersGroupObjectGuid.ToString()
        AdministratorsGroupObjectGuid = $AdministratorsGroupObjectGuid.ToString()
    }
    ConnectionStrings = [ordered]@{ PimDatabase = $SqlConnectionString }
    WorkerClient = [ordered]@{
        Endpoint = $workerEndpoint
        ClientCertificateThumbprint = (ConvertTo-AddsPimNormalizedThumbprint $ApiWorkerClientCertificateThumbprint)
        ExpectedServerCertificateThumbprints = @((ConvertTo-AddsPimNormalizedThumbprint $WorkerTlsCertificateThumbprint))
    }
    TotpSecretProtection = [ordered]@{ CertificateThumbprint = (ConvertTo-AddsPimNormalizedThumbprint $TotpProtectionCertificateThumbprint) }
}

$webSettingsPath = Join-Path $WebPublishPath 'appsettings.Production.json'
$apiSettingsPath = Join-Path $ApiPublishPath 'appsettings.Production.json'

if ($PSCmdlet.ShouldProcess($webSettingsPath, 'Write Web appsettings.Production.json')) {
    ($webSettings | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $webSettingsPath -Encoding utf8
    Write-Output "Wrote $webSettingsPath"
}
if ($PSCmdlet.ShouldProcess($apiSettingsPath, 'Write API appsettings.Production.json')) {
    ($apiSettings | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $apiSettingsPath -Encoding utf8
    Write-Output "Wrote $apiSettingsPath"
}

Write-Output ''
Write-Output 'Step 06 complete. Neither file is tracked by git (see .gitignore: appsettings.Production.json).'

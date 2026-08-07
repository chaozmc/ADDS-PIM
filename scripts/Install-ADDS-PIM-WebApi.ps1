[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })] [string] $WebAssemblyPath,
    [Parameter(Mandatory)] [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })] [string] $ApiAssemblyPath,
    [Parameter(Mandatory)] [ValidatePattern('^[^\\]+\\[^\\]+\$$')] [string] $WebGmsaAccount,
    [Parameter(Mandatory)] [ValidatePattern('^[^\\]+\\[^\\]+\$$')] [string] $ApiGmsaAccount,
    [Parameter(Mandatory)] [ValidatePattern('^[0-9A-Fa-f ]+$')] [string] $WebTlsCertificateThumbprint,
    [Parameter(Mandatory)] [ValidatePattern('^[0-9A-Fa-f ]+$')] [string] $ApiTlsCertificateThumbprint,
    [Parameter(Mandatory)] [ValidatePattern('^[0-9A-Fa-f ]+$')] [string] $ApiWorkerClientCertificateThumbprint,
    [Parameter(Mandatory)] [ValidatePattern('^[0-9A-Fa-f ]+$')] [string] $WorkerServerCertificateThumbprint,
    [string] $WebHostName = 'pim.example.org',
    [string] $ApiHostName = 'pim-backend.example.org',
    [string] $WorkerEndpoint = 'https://pim-worker.example.org:8990/internal/v1/temporary-group-memberships',
    [string] $WorkerHostName = 'pim-worker.example.org',
    [string] $WebSiteName = 'ADDS.PIM.Web',
    [string] $ApiSiteName = 'ADDS.PIM.Api'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Elevated { if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run Web/API setup from an elevated Windows PowerShell session.' } }
function Normalize-Thumbprint([string] $Value) { ($Value -replace '\s').ToUpperInvariant() }
function Get-MachineCertificate([string] $Thumbprint, [string] $ExpectedDnsName) {
    $certificate = Get-Item -LiteralPath "Cert:\LocalMachine\My\$(Normalize-Thumbprint $Thumbprint)" -ErrorAction SilentlyContinue
    if ($null -eq $certificate -or -not $certificate.HasPrivateKey) { throw "Certificate $Thumbprint is missing from LocalMachine\My or has no private key." }
    if ($certificate.NotAfter -le (Get-Date)) { throw "Certificate $Thumbprint is expired." }
    if ($ExpectedDnsName -and $certificate.Subject -notmatch [regex]::Escape($ExpectedDnsName)) { throw "Certificate $Thumbprint does not identify $ExpectedDnsName." }
    $certificate
}
function Assert-Gmsa([string] $Account) {
    $originalWhatIfPreference = $WhatIfPreference
    try {
        $WhatIfPreference = $false
        Import-Module ActiveDirectory -ErrorAction Stop
        if (-not (Test-ADServiceAccount -Identity (($Account -split '\\', 2)[1]))) { throw "The gMSA $Account is not installed or cannot retrieve its managed password." }
    }
    finally { $WhatIfPreference = $originalWhatIfPreference }
}
function Set-AppPoolGmsa([string] $Name, [string] $Account) {
    if (-not (Test-Path "IIS:\AppPools\$Name")) { New-WebAppPool -Name $Name | Out-Null }
    Set-ItemProperty "IIS:\AppPools\$Name" -Name processModel.identityType -Value 3
    Set-ItemProperty "IIS:\AppPools\$Name" -Name processModel.userName -Value $Account
    Set-ItemProperty "IIS:\AppPools\$Name" -Name processModel.password -Value ''
    Set-ItemProperty "IIS:\AppPools\$Name" -Name managedRuntimeVersion -Value ''
}
function Ensure-ApiEventLogSource {
    $sourceName = 'ADDS.PIM.Api'; $logName = 'ADDS-PIM'
    if ([System.Diagnostics.EventLog]::SourceExists($sourceName)) {
        $registeredLog = [System.Diagnostics.EventLog]::LogNameFromSourceName($sourceName, '.')
        if ($registeredLog -ne $logName) { throw "The Event Log source $sourceName is already registered in $registeredLog, not $logName." }
        return
    }
    New-EventLog -LogName $logName -Source $sourceName
}

Assert-Elevated
Import-Module WebAdministration -ErrorAction Stop
Ensure-ApiEventLogSource
Assert-Gmsa $WebGmsaAccount; Assert-Gmsa $ApiGmsaAccount
$webCertificate = Get-MachineCertificate $WebTlsCertificateThumbprint $WebHostName
$apiCertificate = Get-MachineCertificate $ApiTlsCertificateThumbprint $ApiHostName
$null = Get-MachineCertificate $ApiWorkerClientCertificateThumbprint ''
$null = Get-MachineCertificate $WorkerServerCertificateThumbprint $WorkerHostName
$aspNetCoreModulePath = Join-Path ${env:ProgramFiles} 'IIS\Asp.Net Core Module\V2\aspnetcorev2.dll'
if (-not (Test-Path -LiteralPath $aspNetCoreModulePath -PathType Leaf)) { throw "The ASP.NET Core IIS module is missing at $aspNetCoreModulePath. Install the .NET 10 ASP.NET Core Hosting Bundle." }

$webPath = Split-Path -Parent (Resolve-Path -LiteralPath $WebAssemblyPath)
$apiPath = Split-Path -Parent (Resolve-Path -LiteralPath $ApiAssemblyPath)
if ($PSCmdlet.ShouldProcess('IIS', 'Configure ADDS-PIM Web/API application pools and HTTPS sites')) {
    Set-AppPoolGmsa $WebSiteName $WebGmsaAccount; Set-AppPoolGmsa $ApiSiteName $ApiGmsaAccount
    foreach ($site in @(@($WebSiteName, $webPath, $WebHostName, $webCertificate), @($ApiSiteName, $apiPath, $ApiHostName, $apiCertificate))) {
        if (Test-Path "IIS:\Sites\$($site[0])") { throw "IIS site $($site[0]) already exists. Refusing to overwrite an existing site." }
        New-Website -Name $site[0] -PhysicalPath $site[1] -ApplicationPool $site[0] -Port 443 -HostHeader $site[2] -Ssl | Out-Null
        $binding = Get-WebBinding -Name $site[0] -Protocol https
        $binding.AddSslCertificate($site[3].Thumbprint, 'My')
    }
}
Write-Output "Web/API IIS setup completed. Worker service and worker registry were not modified. Configure Web/API application settings with WorkerEndpoint=$WorkerEndpoint and the listed certificate thumbprints before starting traffic."

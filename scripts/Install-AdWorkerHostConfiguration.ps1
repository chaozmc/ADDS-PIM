[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^(?!0\.0\.0\.0$)(?!::$)(?!127\.0\.0\.1$)(?!::1$).+')]
    [string] $BindAddress,

    [Parameter(Mandatory)]
    [ValidateRange(1, 65535)]
    [int] $Port,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9A-Fa-f ]+$')]
    [string] $ServerCertificateThumbprint,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string[]] $AllowedApiClientCertificateThumbprint,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $SqlConnectionString,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $DomainController,

    [ValidateRange(30, 600)]
    [int] $CommandMaxAgeSeconds = 300
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this configuration script in an elevated PowerShell session.'
}

$registryPath = 'HKLM:\SOFTWARE\ADDS-PIM\Worker'
$eventLogName = 'ADDS-PIM'
$eventSource = 'ADDS.PIM.AdWorker'

if ($PSCmdlet.ShouldProcess($registryPath, 'Write Worker host configuration')) {
    $null = New-Item -Path $registryPath -Force
    New-ItemProperty -Path $registryPath -Name BindAddress -PropertyType String -Value $BindAddress -Force | Out-Null
    New-ItemProperty -Path $registryPath -Name Port -PropertyType DWord -Value $Port -Force | Out-Null
    New-ItemProperty -Path $registryPath -Name ServerCertificateThumbprint -PropertyType String -Value ($ServerCertificateThumbprint -replace '\s') -Force | Out-Null
    New-ItemProperty -Path $registryPath -Name AllowedApiClientCertificateThumbprints -PropertyType MultiString -Value ($AllowedApiClientCertificateThumbprint | ForEach-Object { $_ -replace '\s' }) -Force | Out-Null
    New-ItemProperty -Path $registryPath -Name SqlConnectionString -PropertyType String -Value $SqlConnectionString -Force | Out-Null
    New-ItemProperty -Path $registryPath -Name DomainController -PropertyType String -Value $DomainController -Force | Out-Null
    New-ItemProperty -Path $registryPath -Name CommandMaxAgeSeconds -PropertyType DWord -Value $CommandMaxAgeSeconds -Force | Out-Null
}

if (-not [System.Diagnostics.EventLog]::SourceExists($eventSource)) {
    if ($PSCmdlet.ShouldProcess($eventSource, "Create Windows Event Log source in $eventLogName")) {
        [System.Diagnostics.EventLog]::CreateEventSource($eventSource, $eventLogName)
    }
}

Write-Output "Worker configuration written to $registryPath. Restart the ADDS.PIM.AdWorker service to apply it."

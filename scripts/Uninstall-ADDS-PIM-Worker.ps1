[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateNotNullOrEmpty()]
    [string] $ServiceName = 'ADDS.PIM.AdWorker',

    [switch] $PreserveConfiguration,

    [ValidateRange(1, 120)]
    [int] $StopTimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$principal = [Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run Worker uninstall from an elevated PowerShell session.'
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $service) {
    if ($service.Status -ne 'Stopped' -and $PSCmdlet.ShouldProcess($ServiceName, 'Stop Worker service')) {
        Stop-Service -Name $ServiceName -ErrorAction Stop
        $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds($StopTimeoutSeconds))
    }

    if ($PSCmdlet.ShouldProcess($ServiceName, 'Delete Worker service')) {
        & sc.exe delete $ServiceName | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not delete Worker service $ServiceName." }
    }
}

if (-not $PreserveConfiguration) {
    $registryPath = 'HKLM:\SOFTWARE\ADDS-PIM\Worker'
    if ((Test-Path -LiteralPath $registryPath -PathType Container) -and
        $PSCmdlet.ShouldProcess($registryPath, 'Remove Worker registry configuration')) {
        Remove-Item -LiteralPath $registryPath -Recurse -Force
    }
}

Write-Output 'Worker service removal completed. Certificates, gMSA, AD delegation, SQL database, and Event Log history were preserved.'

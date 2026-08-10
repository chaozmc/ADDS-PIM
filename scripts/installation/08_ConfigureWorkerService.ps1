#Requires -Version 5.1
<#
.SYNOPSIS
Step 8 of the ADDS-PIM installer: Worker registry configuration, Event Log
source, and Windows service under the Worker gMSA.

.DESCRIPTION
Writes HKLM\SOFTWARE\ADDS-PIM\Worker, creates the ADDS.PIM.AdWorker Event
Log source, then creates the Windows service in two stages for gMSA
compatibility: created disabled under LocalSystem, never started in that
state, switched to the gMSA via the Windows service provider, only then
set to automatic startup. A failed identity switch removes the
still-disabled service rather than leaving a half-configured one behind.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })] [string] $WorkerAssemblyPath,
    [Parameter(Mandatory)] [string] $WorkerGmsaAccount,
    [Parameter(Mandatory)] [ValidatePattern('^(?!0\.0\.0\.0$)(?!::$)(?!127\.0\.0\.1$)(?!::1$).+')] [string] $BindAddress,
    [Parameter(Mandatory)] [ValidateRange(1, 65535)] [int] $Port,
    [Parameter(Mandatory)] [string] $ServerCertificateThumbprint,
    [Parameter(Mandatory)] [string[]] $AllowedApiClientCertificateThumbprint,
    [Parameter(Mandatory)] [string] $SqlConnectionString,
    [Parameter(Mandatory)] [string] $DomainController,
    [ValidateRange(30, 600)] [int] $CommandMaxAgeSeconds = 300,
    [ValidateNotNullOrEmpty()] [string] $ServiceName = 'ADDS.PIM.AdWorker',
    [switch] $SkipServiceStart
)

. (Join-Path $PSScriptRoot '_Common.ps1')
Assert-AddsPimElevated
Write-AddsPimStepHeader '08 - Configure Worker service'

$serverCertificate = Get-AddsPimLocalMachineCertificate -Thumbprint $ServerCertificateThumbprint
foreach ($thumbprint in $AllowedApiClientCertificateThumbprint) {
    $null = Get-AddsPimLocalMachineCertificate -Thumbprint $thumbprint
}
Assert-AddsPimGmsaUsable -Account $WorkerGmsaAccount

$registryPath = 'HKLM:\SOFTWARE\ADDS-PIM\Worker'
$eventLogName = 'ADDS-PIM'
$eventSource = 'ADDS.PIM.AdWorker'

if ($PSCmdlet.ShouldProcess($registryPath, 'Write Worker host configuration')) {
    $null = New-Item -Path $registryPath -Force
    New-ItemProperty -Path $registryPath -Name BindAddress -PropertyType String -Value $BindAddress -Force | Out-Null
    New-ItemProperty -Path $registryPath -Name Port -PropertyType DWord -Value $Port -Force | Out-Null
    New-ItemProperty -Path $registryPath -Name ServerCertificateThumbprint -PropertyType String -Value $serverCertificate.Thumbprint -Force | Out-Null
    New-ItemProperty -Path $registryPath -Name AllowedApiClientCertificateThumbprints -PropertyType MultiString `
        -Value ($AllowedApiClientCertificateThumbprint | ForEach-Object { ConvertTo-AddsPimNormalizedThumbprint $_ }) -Force | Out-Null
    New-ItemProperty -Path $registryPath -Name SqlConnectionString -PropertyType String -Value $SqlConnectionString -Force | Out-Null
    New-ItemProperty -Path $registryPath -Name DomainController -PropertyType String -Value $DomainController -Force | Out-Null
    New-ItemProperty -Path $registryPath -Name CommandMaxAgeSeconds -PropertyType DWord -Value $CommandMaxAgeSeconds -Force | Out-Null
    Write-Output "Worker configuration written to $registryPath."
}

if (-not [System.Diagnostics.EventLog]::SourceExists($eventSource)) {
    if ($PSCmdlet.ShouldProcess($eventSource, "Create Windows Event Log source in $eventLogName")) {
        [System.Diagnostics.EventLog]::CreateEventSource($eventSource, $eventLogName)
    }
}

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$resolvedAssembly = (Resolve-Path -LiteralPath $WorkerAssemblyPath).Path
$binaryPath = '"{0}" "{1}"' -f $dotnet, $resolvedAssembly

if ($null -eq $existingService) {
    if ($PSCmdlet.ShouldProcess($ServiceName, "Create Windows service under $WorkerGmsaAccount")) {
        New-Service -Name $ServiceName -DisplayName 'ADDS PIM AD Worker' -BinaryPathName $binaryPath `
            -StartupType Disabled -Description 'ADDS PIM isolated Active Directory Worker.' | Out-Null
        try {
            $serviceInstance = Get-CimInstance -ClassName Win32_Service -Filter "Name='$ServiceName'"
            $changeResult = Invoke-CimMethod -InputObject $serviceInstance -MethodName Change -Arguments @{
                StartName = $WorkerGmsaAccount; StartPassword = $null; StartMode = 'Automatic'
            }
            if ($changeResult.ReturnValue -ne 0) {
                throw "Could not switch Worker service $ServiceName to gMSA $WorkerGmsaAccount (Win32_Service.Change return value $($changeResult.ReturnValue))."
            }
            & sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/15000/''/0 | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Could not configure Worker service recovery actions.' }
        }
        catch {
            & sc.exe delete $ServiceName | Out-Null
            throw
        }
        Write-Output "Worker service $ServiceName created under $WorkerGmsaAccount."
    }
}
else {
    $serviceConfig = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
    if ($serviceConfig.PathName -ne $binaryPath -or $serviceConfig.StartName -ne $WorkerGmsaAccount) {
        throw "Existing service $ServiceName differs from the requested binary path or gMSA. Refusing to overwrite it."
    }
    Write-Output "Worker service $ServiceName already exists with matching configuration."
}

if (-not $SkipServiceStart -and $PSCmdlet.ShouldProcess($ServiceName, 'Start Worker service')) {
    if ((Get-Service -Name $ServiceName).Status -ne 'Running') {
        Start-Service -Name $ServiceName
        Start-Sleep -Seconds 2
        if ((Get-Service -Name $ServiceName).Status -ne 'Running') { throw "Worker service $ServiceName did not reach Running state." }
    }
    Write-Output "Worker service $ServiceName is running."
}

Write-Output ''
Write-Output 'Step 08 complete. Verify mTLS readiness from the API host before exposing the endpoint.'

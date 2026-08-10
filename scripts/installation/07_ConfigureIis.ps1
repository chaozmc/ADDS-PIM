#Requires -Version 5.1
<#
.SYNOPSIS
Step 7 of the ADDS-PIM installer: configure IIS app pools and HTTPS sites
for Web and API.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })] [string] $WebPublishPath,
    [Parameter(Mandatory)] [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })] [string] $ApiPublishPath,
    [Parameter(Mandatory)] [string] $WebGmsaAccount,
    [Parameter(Mandatory)] [string] $ApiGmsaAccount,
    [Parameter(Mandatory)] [string] $WebTlsCertificateThumbprint,
    [Parameter(Mandatory)] [string] $ApiTlsCertificateThumbprint,
    [Parameter(Mandatory)] [string] $WebHostName,
    [Parameter(Mandatory)] [string] $ApiHostName,
    [ValidateNotNullOrEmpty()] [string] $WebSiteName = 'ADDS.PIM.Web',
    [ValidateNotNullOrEmpty()] [string] $ApiSiteName = 'ADDS.PIM.Api'
)

. (Join-Path $PSScriptRoot '_Common.ps1')
Assert-AddsPimElevated
Write-AddsPimStepHeader '07 - Configure IIS'
Import-Module WebAdministration -ErrorAction Stop

$aspNetCoreModulePath = Join-Path ${env:ProgramFiles} 'IIS\Asp.Net Core Module\V2\aspnetcorev2.dll'
if (-not (Test-Path -LiteralPath $aspNetCoreModulePath -PathType Leaf)) {
    throw "The ASP.NET Core IIS module is missing at $aspNetCoreModulePath. Run 01_HostPrerequisites.ps1 first."
}

$sourceName = 'ADDS.PIM.Api'; $logName = 'ADDS-PIM'
if ([System.Diagnostics.EventLog]::SourceExists($sourceName)) {
    $registeredLog = [System.Diagnostics.EventLog]::LogNameFromSourceName($sourceName, '.')
    if ($registeredLog -ne $logName) { throw "Event Log source $sourceName is already registered in $registeredLog, not $logName." }
}
elseif ($PSCmdlet.ShouldProcess($sourceName, "Create Windows Event Log source in $logName")) {
    New-EventLog -LogName $logName -Source $sourceName
}

$webCertificate = Get-AddsPimLocalMachineCertificate -Thumbprint $WebTlsCertificateThumbprint -ExpectedDnsName $WebHostName
$apiCertificate = Get-AddsPimLocalMachineCertificate -Thumbprint $ApiTlsCertificateThumbprint -ExpectedDnsName $ApiHostName

function Set-AddsPimAppPoolGmsa {
    param([Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [string] $Account)
    if (-not (Test-Path "IIS:\AppPools\$Name")) { New-WebAppPool -Name $Name | Out-Null }
    Set-ItemProperty "IIS:\AppPools\$Name" -Name processModel.identityType -Value 3
    Set-ItemProperty "IIS:\AppPools\$Name" -Name processModel.userName -Value $Account
    Set-ItemProperty "IIS:\AppPools\$Name" -Name processModel.password -Value ''
    Set-ItemProperty "IIS:\AppPools\$Name" -Name managedRuntimeVersion -Value ''
}

if ($PSCmdlet.ShouldProcess('IIS', 'Configure ADDS-PIM Web/API application pools and HTTPS sites')) {
    Set-AddsPimAppPoolGmsa -Name $WebSiteName -Account $WebGmsaAccount
    Set-AddsPimAppPoolGmsa -Name $ApiSiteName -Account $ApiGmsaAccount

    foreach ($site in @(
        @($WebSiteName, $WebPublishPath, $WebHostName, $webCertificate, $true),
        @($ApiSiteName, $ApiPublishPath, $ApiHostName, $apiCertificate, $false)
    )) {
        if (Test-Path "IIS:\Sites\$($site[0])") {
            Write-Warning "IIS site $($site[0]) already exists; leaving it unchanged."
            continue
        }
        New-Website -Name $site[0] -PhysicalPath $site[1] -ApplicationPool $site[0] -Port 443 -HostHeader $site[2] -Ssl | Out-Null
        Set-WebBinding -Name $site[0] -BindingInformation "*:443:$($site[2])" -PropertyName SslFlags -Value 1
        $binding = Get-WebBinding -Name $site[0] -Protocol https
        $binding.AddSslCertificate($site[3].Thumbprint, 'My')

        # Per ADR-0001: only the Web app authenticates end users via IIS
        # Windows Authentication/Kerberos. The API authenticates Web as a
        # technical caller via request signing/mTLS, not IIS Windows Auth
        # passthrough, so it keeps Anonymous Authentication at the IIS level.
        $useWindowsAuth = $site[4]
        if ($useWindowsAuth) {
            Set-WebConfigurationProperty -PSPath "IIS:\Sites\$($site[0])" -Filter 'system.webServer/security/authentication/windowsAuthentication' `
                -Name useAppPoolCredentials -Value $true
        }
        Set-WebConfigurationProperty -PSPath "IIS:\Sites\$($site[0])" -Filter 'system.webServer/security/authentication/windowsAuthentication' `
            -Name enabled -Value $useWindowsAuth
        Set-WebConfigurationProperty -PSPath "IIS:\Sites\$($site[0])" -Filter 'system.webServer/security/authentication/anonymousAuthentication' `
            -Name enabled -Value (-not $useWindowsAuth)
        Write-Output "Created IIS site $($site[0]) at https://$($site[2])/ -> $($site[1])"
    }
}

Write-Output ''
Write-Output 'Step 07 complete. Windows Authentication and app pool identities should be spot-checked once traffic is expected.'

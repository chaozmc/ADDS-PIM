#Requires -Version 5.1
<#
.SYNOPSIS
Step 1 of the ADDS-PIM installer: host-level prerequisites.

.DESCRIPTION
Installs the IIS role/features needed to host ASP.NET Core apps under
Windows Authentication, the current .NET ASP.NET Core Hosting Bundle, the
RSAT Active Directory PowerShell module (required by every later step that
validates a gMSA or looks up AD objects), and optionally a local SQL
Server Express instance. Safe to re-run; each piece is skippable.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateNotNullOrEmpty()] [string] $DotnetChannel = '10.0',
    [string] $HostingBundleUrl,
    [switch] $InstallSqlExpress,
    [ValidateNotNullOrEmpty()] [string] $SqlExpressBootstrapperUrl = 'https://aka.ms/sql2025express',
    [ValidateNotNullOrEmpty()] [string] $SqlInstanceName = 'SQLEXPRESS',
    [ValidateNotNullOrEmpty()] [string] $DownloadDirectory = (Join-Path $env:TEMP 'ADDS-PIM-Install'),
    [switch] $SkipIisFeatures,
    [switch] $SkipHostingBundle,
    [switch] $SkipRsatAdTools
)

. (Join-Path $PSScriptRoot '_Common.ps1')
Assert-AddsPimElevated
Write-AddsPimStepHeader '01 - Host prerequisites'

[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$osProductType = (Get-CimInstance -ClassName Win32_OperatingSystem).ProductType
if ($osProductType -eq 1) {
    throw 'This host is a Windows client SKU, not Windows Server. Install-WindowsFeature is unavailable here; run this installer on the intended Windows Server host.'
}
if (-not (Get-Command Install-WindowsFeature -ErrorAction SilentlyContinue)) {
    Import-Module ServerManager -ErrorAction Stop
}

if (-not (Test-Path -LiteralPath $DownloadDirectory)) {
    if ($PSCmdlet.ShouldProcess($DownloadDirectory, 'Create download directory')) {
        $null = New-Item -ItemType Directory -Path $DownloadDirectory -Force
    }
}

function Test-AddsPimValidExeDownload {
    param([Parameter(Mandatory)] [string] $Path, [Parameter(Mandatory)] [string] $Description)
    $file = Get-Item -LiteralPath $Path -ErrorAction Stop
    if ($file.Length -lt 1MB) {
        throw "The downloaded $Description ($($file.Length) bytes) is far too small to be a real installer - the download likely returned an error page instead of the file. Check the URL and network/proxy access."
    }
    $header = New-Object byte[] 2
    $stream = [System.IO.File]::OpenRead($Path)
    try { $null = $stream.Read($header, 0, 2) } finally { $stream.Dispose() }
    if (-not ($header[0] -eq 0x4D -and $header[1] -eq 0x5A)) {
        throw "The downloaded $Description is not a valid Windows executable (missing 'MZ' header)."
    }
}

function Save-AddsPimFileFast {
    param([Parameter(Mandatory)] [string] $Uri, [Parameter(Mandatory)] [string] $OutFile)
    if (Get-Command Start-BitsTransfer -ErrorAction SilentlyContinue) {
        Start-BitsTransfer -Source $Uri -Destination $OutFile -DisplayName 'ADDS-PIM prerequisite download'
        return
    }
    $previousProgressPreference = $ProgressPreference
    $ProgressPreference = 'SilentlyContinue'
    try { Invoke-WebRequest -Uri $Uri -OutFile $OutFile -UseBasicParsing }
    finally { $ProgressPreference = $previousProgressPreference }
}

function Invoke-AddsPimDownloadedInstaller {
    param(
        [Parameter(Mandatory)] [string] $Uri,
        [Parameter(Mandatory)] [string] $OutFile,
        [Parameter(Mandatory)] [string[]] $ArgumentList,
        [Parameter(Mandatory)] [string] $Description
    )
    Write-Output "Downloading $Description from $Uri ..."
    Save-AddsPimFileFast -Uri $Uri -OutFile $OutFile
    Test-AddsPimValidExeDownload -Path $OutFile -Description $Description
    Write-Output "Running $Description installer (this can take several minutes) ..."
    $process = Start-Process -FilePath $OutFile -ArgumentList $ArgumentList -Wait -PassThru
    if ($process.ExitCode -eq 3010) { Write-Warning "$Description installed but requires a reboot before it takes full effect." }
    elseif ($process.ExitCode -ne 0) { throw "$Description installer failed with exit code $($process.ExitCode)." }
    else { Write-Output "$Description installed successfully." }
}

function Resolve-AddsPimHostingBundleUrl {
    param([Parameter(Mandatory)] [string] $Channel)
    # There is no stable aka.ms short link for the Windows hosting bundle;
    # resolve the current per-channel download URL from Microsoft's own
    # release-metadata feed instead (verified 2026-08-10 after the aka.ms
    # alias turned out to just redirect to a Bing search page).
    $releasesMetadataUrl = "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/$Channel/releases.json"
    Write-Host "Resolving latest .NET $Channel Windows Hosting Bundle URL from $releasesMetadataUrl ..."
    $metadata = Invoke-RestMethod -Uri $releasesMetadataUrl -UseBasicParsing
    $latestRelease = $metadata.releases | Select-Object -First 1
    $hostingBundleFile = $latestRelease.'aspnetcore-runtime'.files | Where-Object { $_.name -eq 'dotnet-hosting-win.exe' } | Select-Object -First 1
    if (-not $hostingBundleFile) { throw "Could not find a 'dotnet-hosting-win.exe' entry for .NET $Channel in $releasesMetadataUrl. Pass -HostingBundleUrl explicitly instead." }
    Write-Host "Resolved .NET $($latestRelease.'aspnetcore-runtime'.version) hosting bundle: $($hostingBundleFile.url)"
    return [string] $hostingBundleFile.url
}

# 1. IIS role/features for ASP.NET Core Module V2 hosting under Windows Auth.
$iisFeatures = @(
    'Web-Server', 'Web-Common-Http', 'Web-Default-Doc', 'Web-Http-Errors',
    'Web-Static-Content', 'Web-Http-Logging', 'Web-Security', 'Web-Filtering',
    'Web-Windows-Auth', 'Web-Stat-Compression', 'Web-Mgmt-Console'
)
if ($SkipIisFeatures) {
    Write-Output 'Skipping IIS role/feature installation (-SkipIisFeatures).'
}
elseif ($PSCmdlet.ShouldProcess('IIS', "Install role and features: $($iisFeatures -join ', ')")) {
    $result = Install-WindowsFeature -Name $iisFeatures -IncludeManagementTools
    if (-not $result.Success) { throw "IIS feature installation did not complete successfully. ExitCode=$($result.ExitCode)" }
    if ($result.RestartNeeded -eq 'Yes') { Write-Warning 'IIS feature installation reports a pending restart is needed.' }
    Write-Output 'IIS role and required features installed.'
}

# 2. RSAT Active Directory PowerShell module - required by every later step
#    that validates a gMSA (Test-ADServiceAccount) or resolves AD objects.
if ($SkipRsatAdTools) {
    Write-Output 'Skipping RSAT AD PowerShell tools installation (-SkipRsatAdTools).'
}
elseif (Get-Module -ListAvailable -Name ActiveDirectory) {
    Write-Output 'RSAT AD PowerShell module already present.'
}
elseif ($PSCmdlet.ShouldProcess('RSAT-AD-PowerShell', 'Install feature')) {
    $result = Install-WindowsFeature -Name RSAT-AD-PowerShell
    if (-not $result.Success) { throw "RSAT-AD-PowerShell installation did not complete successfully. ExitCode=$($result.ExitCode)" }
    Write-Output 'RSAT AD PowerShell module installed.'
}

# 3. .NET ASP.NET Core Hosting Bundle - installs the ASP.NET Core Module
#    (ANCM) into IIS and the shared runtime.
if ($SkipHostingBundle) {
    Write-Output 'Skipping .NET Hosting Bundle installation (-SkipHostingBundle).'
}
else {
    if (-not $HostingBundleUrl) { $HostingBundleUrl = Resolve-AddsPimHostingBundleUrl -Channel $DotnetChannel }
    if ($PSCmdlet.ShouldProcess('.NET ASP.NET Core Hosting Bundle', "Download from $HostingBundleUrl and install")) {
        $hostingBundlePath = Join-Path $DownloadDirectory 'dotnet-hosting-bundle.exe'
        Invoke-AddsPimDownloadedInstaller -Uri $HostingBundleUrl -OutFile $hostingBundlePath `
            -ArgumentList @('/quiet', '/norestart') -Description '.NET ASP.NET Core Hosting Bundle'
        Write-Output 'Restarting IIS so it picks up the ASP.NET Core Module ...'
        & iisreset /restart | Out-Null
    }
}

# 4. SQL Server Express is optional - many environments point at an
#    already-existing SQL Server/Express instance elsewhere.
$shouldInstallSqlExpress = $InstallSqlExpress.IsPresent
if (-not $PSBoundParameters.ContainsKey('InstallSqlExpress')) {
    $shouldInstallSqlExpress = Confirm-AddsPimYesNo -Prompt 'Install SQL Server Express locally on this host?'
}
if (-not $shouldInstallSqlExpress) {
    Write-Output 'Skipping SQL Server Express installation. Point this install at an existing SQL Server/Express instance instead.'
}
elseif ($PSCmdlet.ShouldProcess("SQL Server Express instance '$SqlInstanceName'", "Download bootstrapper from $SqlExpressBootstrapperUrl and install")) {
    $sqlBootstrapperPath = Join-Path $DownloadDirectory 'SQLEXPR-bootstrapper.exe'
    $sqlArguments = @(
        '/ACTION=Install', '/IACCEPTSQLSERVERLICENSETERMS', '/QUIETSIMPLE',
        "/INSTANCENAME=$SqlInstanceName", '/FEATURES=SQLENGINE',
        '/SQLSYSADMINACCOUNTS=BUILTIN\Administrators', '/TCPENABLED=1', '/NPENABLED=0', '/UpdateEnabled=1'
    )
    # Windows/Integrated Security only - ADDS-PIM refuses SQL logins with
    # passwords, so SECURITYMODE is deliberately left unset (mixed mode off).
    Invoke-AddsPimDownloadedInstaller -Uri $SqlExpressBootstrapperUrl -OutFile $sqlBootstrapperPath `
        -ArgumentList $sqlArguments -Description "SQL Server Express ($SqlInstanceName)"
}

Write-Output ''
Write-Output 'Step 01 complete: IIS, hosting bundle, RSAT AD tools ready.'

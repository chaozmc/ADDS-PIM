[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string] $WorkerAssemblyPath,

    [Parameter(Mandatory)]
    [ValidatePattern('^[^\\]+\\[^\\]+\$$')]
    [string] $WorkerGmsaAccount,

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
    [ValidatePattern('^[0-9A-Fa-f ]+$')]
    [string] $WorkerClientCertificateThumbprint,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string[]] $AllowedApiClientCertificateThumbprint,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $SqlConnectionString,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $DomainController,

    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string] $DatabaseBootstrapScript,

    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string] $InfrastructureProjectPath,

    [ValidateNotNullOrEmpty()]
    [string] $DatabaseName = 'ADDS_PIM',

    [ValidateNotNullOrEmpty()]
    [string] $SqlServerInstance = 'localhost',

    [ValidateNotNullOrEmpty()]
    [string] $ServiceName = 'ADDS.PIM.AdWorker',

    [ValidateRange(30, 600)]
    [int] $CommandMaxAgeSeconds = 300,

    [switch] $SkipDatabase,
    [switch] $SkipServiceStart
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Elevated {
    $principal = [Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run Worker setup from an elevated PowerShell session.'
    }
}

function Normalize-Thumbprint([string] $Thumbprint) {
    return $Thumbprint -replace '\s'
}

function Assert-LocalMachineCertificate([string] $Thumbprint, [string] $Purpose) {
    $normalized = Normalize-Thumbprint $Thumbprint
    $certificate = Get-Item -LiteralPath "Cert:\LocalMachine\My\$normalized" -ErrorAction SilentlyContinue
    if ($null -eq $certificate -or -not $certificate.HasPrivateKey) {
        throw "$Purpose certificate $normalized is missing from LocalMachine\My or has no private key."
    }

    return $certificate
}

function Assert-GmsaPrerequisite([string] $Account) {
    $samAccountName = ($Account -split '\\', 2)[1]
    if (-not (Get-Module -ListAvailable -Name ActiveDirectory)) {
        throw 'RSAT Active Directory tools are required to validate the installed Worker gMSA.'
    }

    Import-Module ActiveDirectory -ErrorAction Stop
    if (-not (Test-ADServiceAccount -Identity $samAccountName)) {
        throw "The gMSA $Account is not installed or cannot retrieve its managed password on this host."
    }
}

function Invoke-SqlCmd([string[]] $Arguments) {
    $sqlcmd = Get-Command sqlcmd.exe -ErrorAction Stop
    & $sqlcmd.Source @Arguments
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed with exit code $LASTEXITCODE." }
}

Assert-Elevated
$workerAssembly = (Resolve-Path -LiteralPath $WorkerAssemblyPath).Path
$bootstrapScript = (Resolve-Path -LiteralPath $DatabaseBootstrapScript).Path
$infrastructureProject = (Resolve-Path -LiteralPath $InfrastructureProjectPath).Path
$repositoryRoot = Split-Path -Parent $PSScriptRoot

$serverCertificate = Assert-LocalMachineCertificate $ServerCertificateThumbprint 'Worker server'
$workerClientCertificate = Assert-LocalMachineCertificate $WorkerClientCertificateThumbprint 'Worker client'
foreach ($thumbprint in $AllowedApiClientCertificateThumbprint) {
    $null = Assert-LocalMachineCertificate $thumbprint 'Allowed API client'
}
Assert-GmsaPrerequisite $WorkerGmsaAccount

if (-not $SkipDatabase) {
    if ($PSCmdlet.ShouldProcess("$SqlServerInstance/$DatabaseName", 'Create database and Worker runtime login')) {
        Invoke-SqlCmd @('-S', $SqlServerInstance, '-E', '-b', '-i', $bootstrapScript,
            '-v', "DatabaseName=$DatabaseName", "WorkerLogin=$WorkerGmsaAccount")
    }

    if ($PSCmdlet.ShouldProcess($DatabaseName, 'Apply versioned EF Core migrations')) {
        Push-Location $repositoryRoot
        try {
            & dotnet tool restore
            if ($LASTEXITCODE -ne 0) { throw "EF Core tool restore failed with exit code $LASTEXITCODE." }
            $migrationOutput = & dotnet tool run dotnet-ef database update --project $infrastructureProject --connection $SqlConnectionString 2>&1
            if ($LASTEXITCODE -ne 0) {
                throw "EF Core database migration failed with exit code $LASTEXITCODE. $($migrationOutput -join [Environment]::NewLine)"
            }
            $migrationOutput | Write-Output
        }
        finally {
            Pop-Location
        }
        Invoke-SqlCmd @('-S', $SqlServerInstance, '-E', '-b', '-i', $bootstrapScript,
            '-v', "DatabaseName=$DatabaseName", "WorkerLogin=$WorkerGmsaAccount")
    }
}

$configurationScript = Join-Path $PSScriptRoot 'Install-AdWorkerHostConfiguration.ps1'
& $configurationScript -BindAddress $BindAddress -Port $Port `
    -ServerCertificateThumbprint $serverCertificate.Thumbprint `
    -WorkerClientCertificateThumbprint $workerClientCertificate.Thumbprint `
    -AllowedApiClientCertificateThumbprint $AllowedApiClientCertificateThumbprint `
    -SqlConnectionString $SqlConnectionString -DomainController $DomainController `
    -CommandMaxAgeSeconds $CommandMaxAgeSeconds -WhatIf:$WhatIfPreference

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$binaryPath = '"{0}" "{1}"' -f $dotnet, $workerAssembly
if ($null -eq $existingService) {
    if ($PSCmdlet.ShouldProcess($ServiceName, "Create Windows service under $WorkerGmsaAccount")) {
        New-Service -Name $ServiceName -DisplayName 'ADDS PIM AD Worker' -BinaryPathName $binaryPath `
            -StartupType Disabled -Description 'ADDS PIM isolated Active Directory Worker.' | Out-Null
        try {
            $serviceInstance = Get-CimInstance -ClassName Win32_Service -Filter "Name='$ServiceName'"
            $changeResult = Invoke-CimMethod -InputObject $serviceInstance -MethodName Change -Arguments @{
                StartName = $WorkerGmsaAccount
                StartPassword = $null
                StartMode = 'Automatic'
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
    }
}
else {
    $serviceConfig = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
    if ($serviceConfig.PathName -ne $binaryPath -or $serviceConfig.StartName -ne $WorkerGmsaAccount) {
        throw "Existing service $ServiceName differs from the requested binary path or gMSA. Refusing to overwrite it."
    }
}

if (-not $SkipServiceStart -and $PSCmdlet.ShouldProcess($ServiceName, 'Start Worker service')) {
    Start-Service -Name $ServiceName
    Start-Sleep -Seconds 2
    if ((Get-Service -Name $ServiceName).Status -ne 'Running') { throw "Worker service $ServiceName did not reach Running state." }
}

Write-Output "Worker setup completed. Verify mTLS readiness from the API host before exposing the endpoint."

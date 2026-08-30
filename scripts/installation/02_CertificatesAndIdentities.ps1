#Requires -Version 5.1
<#
.SYNOPSIS
Step 2 of the ADDS-PIM installer: validate gMSAs and prepare certificates.

.DESCRIPTION
Confirms the Active Directory Privileged Access Management optional
feature is enabled in the forest (ADDS-PIM's time-limited memberships
depend on it) and validates the three gMSAs are installed and usable on
this host (never creates or delegates them - that stays
separately-approved AD administration). For the four "bring your own"
certificates (Web TLS, API
TLS, Worker TLS, API-to-Worker mTLS client) prompts for an existing
thumbprint or generates a self-signed one if left blank. Always generates
the Web signing certificate and the TOTP protection certificate unless an
existing thumbprint is supplied for either - both are legitimately
self-signed per dev-docs/10-operations/web-signing-certificate-operations.md,
since the API trusts one explicitly registered public key, not a general PKI
chain.

.OUTPUTS
A PSCustomObject with every collected thumbprint, for later steps.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [string] $WebGmsaAccount,
    [Parameter(Mandatory)] [string] $ApiGmsaAccount,
    [Parameter(Mandatory)] [string] $WorkerGmsaAccount,
    [Parameter(Mandatory)] [string] $WebHostName,
    [Parameter(Mandatory)] [string] $ApiHostName,
    [Parameter(Mandatory)] [string] $WorkerHostName,
    [Parameter(Mandatory)] [string] $DomainController,

    [string] $WebTlsCertificateThumbprint,
    [string] $ApiTlsCertificateThumbprint,
    [string] $WorkerTlsCertificateThumbprint,
    [string] $ApiWorkerClientCertificateThumbprint,
    [string] $WebSigningCertificateThumbprint,
    [string] $TotpProtectionCertificateThumbprint,

    [switch] $NonInteractive
)

. (Join-Path $PSScriptRoot '_Common.ps1')
Assert-AddsPimElevated
Write-AddsPimStepHeader '02 - gMSAs and certificates'

Write-Host "Validating the Active Directory forest via $DomainController ..."
Assert-AddsPimPamFeatureEnabled -DomainController $DomainController

Write-Host 'Validating gMSAs ...'
Assert-AddsPimGmsaUsable -Account $WebGmsaAccount
Assert-AddsPimGmsaUsable -Account $ApiGmsaAccount
Assert-AddsPimGmsaUsable -Account $WorkerGmsaAccount
Write-Host 'All three gMSAs are installed and usable on this host.'

function Resolve-AddsPimTlsCertificate {
    param(
        [Parameter(Mandatory)] [string] $Purpose,
        [Parameter(Mandatory)] [string] $DnsName,
        [string] $ExistingThumbprint,
        [Parameter(Mandatory)] [string[]] $KeyUsage
    )
    if ($ExistingThumbprint) {
        $certificate = Get-AddsPimLocalMachineCertificate -Thumbprint $ExistingThumbprint -ExpectedDnsName $DnsName
        Write-Host "$Purpose - using existing certificate $($certificate.Thumbprint)."
        return $certificate.Thumbprint
    }
    if (-not $NonInteractive) {
        $entered = Read-AddsPimOptionalValue -Prompt "$Purpose - existing certificate thumbprint for $DnsName"
        if ($entered) {
            $certificate = Get-AddsPimLocalMachineCertificate -Thumbprint $entered -ExpectedDnsName $DnsName
            Write-Host "$Purpose - using existing certificate $($certificate.Thumbprint)."
            return $certificate.Thumbprint
        }
    }
    Write-Warning "$Purpose - generating a self-signed certificate for $DnsName. This is fine for a test host; a production deployment should use a certificate issued by the environment's internal CA instead."
    if (-not (Confirm-AddsPimYesNo -Prompt "Generate a self-signed certificate for $DnsName now?" -DefaultYes $true)) {
        throw "$Purpose certificate is required; re-run with an existing thumbprint."
    }
    if ($PSCmdlet.ShouldProcess($DnsName, "Generate self-signed $Purpose certificate")) {
        $certificate = New-AddsPimSelfSignedCertificate -Subject "CN=$DnsName" -FriendlyName "ADDS-PIM $Purpose ($DnsName)" `
            -KeyUsage $KeyUsage -DnsName $DnsName -ValidYears 2
        Write-Host "$Purpose - generated self-signed certificate $($certificate.Thumbprint)."
        return $certificate.Thumbprint
    }
    return $null
}

$webTls = Resolve-AddsPimTlsCertificate -Purpose 'Web TLS' -DnsName $WebHostName -ExistingThumbprint $WebTlsCertificateThumbprint -KeyUsage @('DigitalSignature', 'ServerAuthentication')
$apiTls = Resolve-AddsPimTlsCertificate -Purpose 'API TLS' -DnsName $ApiHostName -ExistingThumbprint $ApiTlsCertificateThumbprint -KeyUsage @('DigitalSignature', 'ServerAuthentication')
$workerTls = Resolve-AddsPimTlsCertificate -Purpose 'Worker TLS' -DnsName $WorkerHostName -ExistingThumbprint $WorkerTlsCertificateThumbprint -KeyUsage @('DigitalSignature', 'ServerAuthentication')
$apiWorkerClient = Resolve-AddsPimTlsCertificate -Purpose 'API-to-Worker mTLS client' -DnsName $ApiHostName -ExistingThumbprint $ApiWorkerClientCertificateThumbprint -KeyUsage @('DigitalSignature', 'ClientAuthentication')

function Resolve-AddsPimApplicationCertificate {
    param(
        [Parameter(Mandatory)] [string] $Purpose,
        [Parameter(Mandatory)] [string] $Subject,
        [string] $ExistingThumbprint,
        [Parameter(Mandatory)] [string[]] $KeyUsage
    )
    if ($ExistingThumbprint) {
        $certificate = Get-AddsPimLocalMachineCertificate -Thumbprint $ExistingThumbprint
        Write-Host "$Purpose - using existing certificate $($certificate.Thumbprint)."
        return $certificate.Thumbprint
    }
    if ($PSCmdlet.ShouldProcess($Subject, "Generate self-signed $Purpose certificate")) {
        $certificate = New-AddsPimSelfSignedCertificate -Subject $Subject -FriendlyName $Subject.Substring(3) -KeyUsage $KeyUsage -ValidYears 1
        Write-Host "$Purpose - generated self-signed certificate $($certificate.Thumbprint)."
        return $certificate.Thumbprint
    }
    return $null
}

$webSigning = Resolve-AddsPimApplicationCertificate -Purpose 'Web signing' -Subject 'CN=ADDS-PIM Web API Signing' `
    -ExistingThumbprint $WebSigningCertificateThumbprint -KeyUsage @('DigitalSignature')
$totpProtection = Resolve-AddsPimApplicationCertificate -Purpose 'TOTP secret protection' -Subject 'CN=ADDS-PIM TOTP Secret Protection' `
    -ExistingThumbprint $TotpProtectionCertificateThumbprint -KeyUsage @('KeyEncipherment')

Write-Host ''
Write-Host 'Granting private-key read access to the owning gMSA for each certificate ...'
if ($PSCmdlet.ShouldProcess('Certificates', 'Grant private-key ACLs to gMSAs')) {
    if ($webTls) { Grant-AddsPimPrivateKeyRead -Thumbprint $webTls -Account $WebGmsaAccount }
    if ($webSigning) { Grant-AddsPimPrivateKeyRead -Thumbprint $webSigning -Account $WebGmsaAccount }
    if ($apiTls) { Grant-AddsPimPrivateKeyRead -Thumbprint $apiTls -Account $ApiGmsaAccount }
    if ($apiWorkerClient) { Grant-AddsPimPrivateKeyRead -Thumbprint $apiWorkerClient -Account $ApiGmsaAccount }
    if ($totpProtection) { Grant-AddsPimPrivateKeyRead -Thumbprint $totpProtection -Account $ApiGmsaAccount }
    if ($workerTls) { Grant-AddsPimPrivateKeyRead -Thumbprint $workerTls -Account $WorkerGmsaAccount }
}

Write-Host ''
Write-Host 'Step 02 complete.'

[PSCustomObject]@{
    WebTlsCertificateThumbprint          = $webTls
    ApiTlsCertificateThumbprint          = $apiTls
    WorkerTlsCertificateThumbprint       = $workerTls
    ApiWorkerClientCertificateThumbprint = $apiWorkerClient
    WebSigningCertificateThumbprint      = $webSigning
    TotpProtectionCertificateThumbprint  = $totpProtection
}

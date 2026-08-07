[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $SqlConnectionString,

    [Parameter(Mandatory, HelpMessage = 'Thumbprint (in Cert:\LocalMachine\My) of the Web application signature certificate to register for Web-to-API request signing.')]
    [ValidatePattern('^[0-9A-Fa-f ]{40,}$')]
    [string] $Thumbprint,

    [Parameter(Mandatory, HelpMessage = 'KeyId label for this signing key, e.g. web-home-01. This is the value the API records as the technical client on every audit event signed with this key.')]
    [ValidatePattern('^[A-Za-z0-9._-]{1,128}$')]
    [string] $KeyId,

    [Nullable[datetimeoffset]] $ValidFromUtc,
    [Nullable[datetimeoffset]] $ValidUntilUtc,

    [ValidatePattern('^[A-Za-z0-9._-]{1,128}$')]
    [string] $DeactivateKeyId
)

<#
.SYNOPSIS
Registers the public part of a Web application-signature certificate in
dbo.WebSigningCertificates so the API can verify Web-to-API request
signatures.

.DESCRIPTION
Without -DeactivateKeyId, this is the one-time bootstrap step for the very
first signing key: it writes directly to SQL Server because every protected
administration endpoint already requires a validly signed request, so a
first key cannot be registered through the application itself.

With -DeactivateKeyId, the same call also deactivates (IsActive = 0, not
deleted) the named existing key in the same transaction. Use this for a
routine certificate renewal once the old certificate has already expired (or
is about to), so a new active key always exists before the old one stops
being accepted; the row itself is preserved for audit history per
docs/10-operations/web-signing-certificate-operations.md. If the old
certificate is still comfortably valid, prefer the documented overlap-based
rotation instead: register the new key without -DeactivateKeyId, roll the new
KeyId/thumbprint out to every Web server, verify signing works, and only then
deactivate the old key.

Only the public certificate (no private key) is stored. The corresponding
private key must remain in Cert:\LocalMachine\My on the Web server(s) and
must not be exported.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Normalize-Thumbprint([string] $Value) { ($Value -replace '\s').ToUpperInvariant() }

function Add-Parameter([System.Data.SqlClient.SqlCommand] $Command, [string] $Name, $Value) {
    $parameter = $Command.Parameters.AddWithValue($Name, $Value)
    if ($Value -is [string]) { $parameter.Size = [Math]::Max(1, $Value.Length) }
}

function Invoke-Scalar([System.Data.SqlClient.SqlConnection] $Connection, [System.Data.SqlClient.SqlTransaction] $Transaction, [string] $Sql, [hashtable] $Parameters) {
    $command = $Connection.CreateCommand(); $command.CommandText = $Sql; $command.Transaction = $Transaction
    foreach ($entry in $Parameters.GetEnumerator()) { Add-Parameter $command $entry.Key $entry.Value }
    try { return $command.ExecuteScalar() } finally { $command.Dispose() }
}

function Invoke-NonQuery([System.Data.SqlClient.SqlConnection] $Connection, [System.Data.SqlClient.SqlTransaction] $Transaction, [string] $Sql, [hashtable] $Parameters) {
    $command = $Connection.CreateCommand(); $command.CommandText = $Sql; $command.Transaction = $Transaction
    foreach ($entry in $Parameters.GetEnumerator()) { Add-Parameter $command $entry.Key $entry.Value }
    try { [void] $command.ExecuteNonQuery() } finally { $command.Dispose() }
}

if ($DeactivateKeyId -and $DeactivateKeyId -eq $KeyId) { throw "DeactivateKeyId cannot be the same as the KeyId being registered." }

$normalizedThumbprint = Normalize-Thumbprint $Thumbprint

$certificate = Get-Item -LiteralPath "Cert:\LocalMachine\My\$normalizedThumbprint" -ErrorAction SilentlyContinue
if ($null -eq $certificate) { throw "No certificate with thumbprint $normalizedThumbprint was found in Cert:\LocalMachine\My on this host. Run this script on the host holding the Web signing certificate." }
if (-not $certificate.HasPrivateKey) { throw "Certificate $normalizedThumbprint has no accessible private key on this host. Registering a key without its matching private key would make it unusable for signing; verify the thumbprint." }
if ($certificate.NotAfter -le (Get-Date)) { throw "Certificate $normalizedThumbprint is already expired ($($certificate.NotAfter)). Use a currently valid signing certificate." }
if (-not ($certificate.Extensions | Where-Object { $_ -is [System.Security.Cryptography.X509Certificates.X509KeyUsageExtension] -and $_.KeyUsages.HasFlag([System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature) })) {
    throw "Certificate $normalizedThumbprint does not have the DigitalSignature key usage required for request signing."
}

$effectiveValidFromUtc = if ($ValidFromUtc) { $ValidFromUtc.Value.ToUniversalTime() } else { [datetimeoffset]::UtcNow }
$effectiveValidUntilUtc = if ($ValidUntilUtc) { $ValidUntilUtc.Value.ToUniversalTime() } else { [datetimeoffset]$certificate.NotAfter.ToUniversalTime() }
if ($effectiveValidUntilUtc -le $effectiveValidFromUtc) { throw "ValidUntilUtc ($effectiveValidUntilUtc) must be after ValidFromUtc ($effectiveValidFromUtc)." }

$publicCertificateDer = $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert)
$createdBy = [Security.Principal.WindowsIdentity]::GetCurrent().Name

Write-Output "About to register the following Web signing key:"
Write-Output "  KeyId:            $KeyId"
Write-Output "  Thumbprint:       $normalizedThumbprint"
Write-Output "  Subject:          $($certificate.Subject)"
Write-Output "  Purpose:          ApiRequestSigning"
Write-Output "  Valid from (UTC): $effectiveValidFromUtc"
Write-Output "  Valid until (UTC): $effectiveValidUntilUtc"
Write-Output "  Registered by:    $createdBy"
if ($DeactivateKeyId) { Write-Output "  Also deactivating existing KeyId: $DeactivateKeyId (row is kept, not deleted)" }

$shouldProcessAction = if ($DeactivateKeyId) { "Register signing key '$KeyId' ($normalizedThumbprint) and deactivate '$DeactivateKeyId'" } else { "Register signing key '$KeyId' ($normalizedThumbprint)" }
if (-not $PSCmdlet.ShouldProcess("dbo.WebSigningCertificates", $shouldProcessAction)) { return }

$connection = [System.Data.SqlClient.SqlConnection]::new($SqlConnectionString)
$connection.Open()
$transaction = $connection.BeginTransaction()
try {
    $existingByKeyId = Invoke-Scalar $connection $transaction 'SELECT WebSigningCertificateId FROM dbo.WebSigningCertificates WHERE KeyId=@keyId' @{ '@keyId' = $KeyId }
    if ($existingByKeyId) { throw "KeyId '$KeyId' is already registered. This script only performs the initial bootstrap; for rotation, register a new KeyId and deactivate the old one per web-signing-certificate-operations.md." }

    $existingByThumbprint = Invoke-Scalar $connection $transaction 'SELECT WebSigningCertificateId FROM dbo.WebSigningCertificates WHERE Thumbprint=@thumbprint' @{ '@thumbprint' = $normalizedThumbprint }
    if ($existingByThumbprint) { throw "Certificate $normalizedThumbprint is already registered under a different KeyId." }

    if ($DeactivateKeyId) {
        $deactivationTarget = Invoke-Scalar $connection $transaction 'SELECT WebSigningCertificateId FROM dbo.WebSigningCertificates WHERE KeyId=@keyId' @{ '@keyId' = $DeactivateKeyId }
        if (-not $deactivationTarget) { throw "DeactivateKeyId '$DeactivateKeyId' does not exist in dbo.WebSigningCertificates. Nothing was registered or deactivated." }
    }

    Invoke-NonQuery $connection $transaction @'
INSERT dbo.WebSigningCertificates (WebSigningCertificateId, KeyId, Thumbprint, PublicCertificateDer, Purpose, IsActive, ValidFromUtc, ValidUntilUtc, CreatedUtc, CreatedBy)
VALUES (@id, @keyId, @thumbprint, @der, @purpose, 1, @validFrom, @validUntil, @now, @createdBy)
'@ @{
        '@id'         = [guid]::NewGuid()
        '@keyId'      = $KeyId
        '@thumbprint' = $normalizedThumbprint
        '@der'        = $publicCertificateDer
        '@purpose'    = 'ApiRequestSigning'
        '@validFrom'  = $effectiveValidFromUtc
        '@validUntil' = $effectiveValidUntilUtc
        '@now'        = [datetimeoffset]::UtcNow
        '@createdBy'  = $createdBy
    }

    if ($DeactivateKeyId) {
        Invoke-NonQuery $connection $transaction 'UPDATE dbo.WebSigningCertificates SET IsActive = 0 WHERE KeyId = @keyId' @{ '@keyId' = $DeactivateKeyId }
    }

    $transaction.Commit()
    Write-Output "Registered Web signing key '$KeyId' for thumbprint $normalizedThumbprint."
    if ($DeactivateKeyId) { Write-Output "Deactivated existing key '$DeactivateKeyId'; the row was kept for audit history." }
    Write-Output "Set SigningKeyId=$KeyId and SigningCertificateThumbprint=$normalizedThumbprint in the Web application configuration before routing signed traffic through this key."
}
catch {
    $transaction.Rollback()
    throw
}
finally {
    $connection.Dispose()
}

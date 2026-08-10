#Requires -Version 5.1
<#
.SYNOPSIS
Step 5 of the ADDS-PIM installer: register the Web signing certificate's
public key with the API (ADR-0011).

.DESCRIPTION
Every protected /admin/* endpoint requires a validly signed request, which
in turn requires an already-registered signing key - so the very first key
in a fresh environment cannot be registered through the application itself
(chicken-and-egg). This writes directly to dbo.WebSigningCertificates,
storing only the public certificate (DER), never the private key.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [string] $SqlConnectionString,
    [Parameter(Mandatory)] [string] $Thumbprint,
    [Parameter(Mandatory)] [string] $KeyId
)

. (Join-Path $PSScriptRoot '_Common.ps1')
Write-AddsPimStepHeader '05 - Register Web signing certificate'

$normalizedThumbprint = ConvertTo-AddsPimNormalizedThumbprint $Thumbprint
$certificate = Get-Item -LiteralPath "Cert:\LocalMachine\My\$normalizedThumbprint" -ErrorAction SilentlyContinue
if ($null -eq $certificate) { throw "No certificate with thumbprint $normalizedThumbprint was found in Cert:\LocalMachine\My on this host." }
if (-not $certificate.HasPrivateKey) { throw "Certificate $normalizedThumbprint has no accessible private key on this host." }
if ($certificate.NotAfter -le (Get-Date)) { throw "Certificate $normalizedThumbprint is already expired ($($certificate.NotAfter))." }

function Invoke-AddsPimScalar {
    param($Connection, $Transaction, [string] $Sql, [hashtable] $Parameters)
    $command = $Connection.CreateCommand(); $command.CommandText = $Sql; $command.Transaction = $Transaction
    foreach ($entry in $Parameters.GetEnumerator()) { $command.Parameters.AddWithValue($entry.Key, $entry.Value) | Out-Null }
    try { return $command.ExecuteScalar() } finally { $command.Dispose() }
}
function Invoke-AddsPimNonQuery {
    param($Connection, $Transaction, [string] $Sql, [hashtable] $Parameters)
    $command = $Connection.CreateCommand(); $command.CommandText = $Sql; $command.Transaction = $Transaction
    foreach ($entry in $Parameters.GetEnumerator()) { $command.Parameters.AddWithValue($entry.Key, $entry.Value) | Out-Null }
    try { [void] $command.ExecuteNonQuery() } finally { $command.Dispose() }
}

$publicCertificateDer = $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert)
$now = [datetimeoffset]::UtcNow
$validUntil = [datetimeoffset] $certificate.NotAfter.ToUniversalTime()
$createdBy = [Security.Principal.WindowsIdentity]::GetCurrent().Name

if (-not $PSCmdlet.ShouldProcess('dbo.WebSigningCertificates', "Register signing key '$KeyId' ($normalizedThumbprint)")) { return }

$connection = [System.Data.SqlClient.SqlConnection]::new($SqlConnectionString)
$connection.Open()
$transaction = $connection.BeginTransaction()
try {
    $existingByKeyId = Invoke-AddsPimScalar $connection $transaction 'SELECT WebSigningCertificateId FROM dbo.WebSigningCertificates WHERE KeyId=@keyId' @{ '@keyId' = $KeyId }
    if ($existingByKeyId) {
        Write-Output "KeyId '$KeyId' is already registered; nothing to do."
    }
    else {
        Invoke-AddsPimNonQuery $connection $transaction @'
INSERT dbo.WebSigningCertificates (WebSigningCertificateId, KeyId, Thumbprint, PublicCertificateDer, Purpose, IsActive, ValidFromUtc, ValidUntilUtc, CreatedUtc, CreatedBy)
VALUES (@id, @keyId, @thumbprint, @der, @purpose, 1, @validFrom, @validUntil, @now, @createdBy)
'@ @{
            '@id' = [guid]::NewGuid(); '@keyId' = $KeyId; '@thumbprint' = $normalizedThumbprint
            '@der' = $publicCertificateDer; '@purpose' = 'ApiRequestSigning'
            '@validFrom' = $now; '@validUntil' = $validUntil; '@now' = $now; '@createdBy' = $createdBy
        }
        Write-Output "Registered Web signing key '$KeyId' for thumbprint $normalizedThumbprint (valid until $validUntil)."
    }
    $transaction.Commit()
}
catch { $transaction.Rollback(); throw }
finally { $connection.Dispose() }

Write-Output ''
Write-Output 'Step 05 complete.'

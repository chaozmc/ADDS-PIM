[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $SqlConnectionString,
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $ActorSamAccountName,
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $TargetSamAccountName,
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $DomainDnsName,
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $ForestDnsName,
    [Parameter(Mandatory)] [guid] $DirectoryScopeId,
    [string] $DomainController,
    [string] $OperatorsGroup = 'PIM-Test-TTL-Operators',
    [string] $AdminsGroup = 'PIM-Test-TTL-Admins',
    [string] $Tier0Group = 'PIM-Test-TTL-Tier0',
    [ValidateRange(1800, 604800)] [int] $OperatorsMaximumTtlSeconds = 28800,
    [ValidateRange(1800, 604800)] [int] $AdminsMaximumTtlSeconds = 14400,
    [ValidateRange(1800, 604800)] [int] $Tier0MaximumTtlSeconds = 7200,
    [ValidateSet('None', 'Fido2', 'Totp', 'Fido2OrTotp')] [string] $OperatorsMfa = 'None',
    [ValidateSet('None', 'Fido2', 'Totp', 'Fido2OrTotp')] [string] $AdminsMfa = 'Fido2OrTotp',
    [ValidateSet('None', 'Fido2', 'Totp', 'Fido2OrTotp')] [string] $Tier0Mfa = 'Fido2OrTotp'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Add-Parameter([System.Data.SqlClient.SqlCommand] $Command, [string] $Name, $Value) {
    $parameter = $Command.Parameters.AddWithValue($Name, $Value)
    if ($Value -is [string]) { $parameter.Size = [Math]::Max(1, $Value.Length) }
}

function Get-DisplayValue($DirectoryObject) {
    if ([string]::IsNullOrWhiteSpace([string]$DirectoryObject.DisplayName)) { return $DirectoryObject.SamAccountName }
    return $DirectoryObject.DisplayName
}

function Get-FactorPolicy([string] $Value) {
    switch ($Value) { 'None' { return 0 }; 'Fido2' { return 1 }; 'Totp' { return 2 }; 'Fido2OrTotp' { return 3 } }
}

function Invoke-Scalar([System.Data.SqlClient.SqlConnection] $Connection, [string] $Sql, [hashtable] $Parameters) {
    $command = $Connection.CreateCommand(); $command.CommandText = $Sql; $command.Transaction = $script:SqlTransaction
    foreach ($entry in $Parameters.GetEnumerator()) { Add-Parameter $command $entry.Key $entry.Value }
    try { return $command.ExecuteScalar() } finally { $command.Dispose() }
}

function Invoke-NonQuery([System.Data.SqlClient.SqlConnection] $Connection, [string] $Sql, [hashtable] $Parameters) {
    $command = $Connection.CreateCommand(); $command.CommandText = $Sql; $command.Transaction = $script:SqlTransaction
    foreach ($entry in $Parameters.GetEnumerator()) { Add-Parameter $command $entry.Key $entry.Value }
    try { [void] $command.ExecuteNonQuery() } finally { $command.Dispose() }
}

function Get-OrCreateAccount([System.Data.SqlClient.SqlConnection] $Connection, [guid] $ScopeId, $DirectoryObject, [datetimeoffset] $Now) {
    $existing = Invoke-Scalar $Connection 'SELECT AccountId FROM dbo.DirectoryAccounts WHERE DirectoryScopeId=@scope AND ObjectGuid=@objectGuid' @{ '@scope'=$ScopeId; '@objectGuid'=$DirectoryObject.ObjectGUID }
    if ($existing) { return [guid]$existing }
    $id = [guid]::NewGuid()
    Invoke-NonQuery $Connection @'
INSERT dbo.DirectoryAccounts (AccountId,DirectoryScopeId,ObjectGuid,ObjectSid,SamAccountName,UserPrincipalName,DistinguishedName,DomainQualifiedName,DisplayName,IsEnabledInDirectory,IsWithinAllowedScope,LastVerifiedUtc,IsActive,CreatedUtc,ModifiedUtc)
VALUES (@id,@scope,@objectGuid,@sid,@sam,@upn,@dn,@qualified,@display,@enabled,1,@now,1,@now,@now)
'@ @{ '@id'=$id; '@scope'=$ScopeId; '@objectGuid'=$DirectoryObject.ObjectGUID; '@sid'=$DirectoryObject.ObjectSid.Value; '@sam'=$DirectoryObject.SamAccountName; '@upn'=$DirectoryObject.UserPrincipalName; '@dn'=$DirectoryObject.DistinguishedName; '@qualified'="$DomainDnsName\$($DirectoryObject.SamAccountName)"; '@display'=(Get-DisplayValue $DirectoryObject); '@enabled'=[bool]$DirectoryObject.Enabled; '@now'=$Now }
    return $id
}

Import-Module ActiveDirectory -ErrorAction Stop
$adParameters = @{ Properties = @('ObjectGUID','ObjectSid','UserPrincipalName','DistinguishedName','Enabled','DisplayName') }
if ($DomainController) { $adParameters.Server = $DomainController }
$actor = Get-ADUser -Identity $ActorSamAccountName @adParameters
$target = Get-ADUser -Identity $TargetSamAccountName @adParameters
$groupParameters = @{ Properties = @('ObjectGUID','ObjectSid','DistinguishedName','DisplayName') }
if ($DomainController) { $groupParameters.Server = $DomainController }
$groups = @(
    [pscustomobject]@{ Name='TestGroup1'; Group=(Get-ADGroup -Identity $OperatorsGroup @groupParameters); MaximumTtlSeconds=$OperatorsMaximumTtlSeconds; AllowedSecondFactorTypes=(Get-FactorPolicy $OperatorsMfa) },
    [pscustomobject]@{ Name='TestGroup2'; Group=(Get-ADGroup -Identity $AdminsGroup @groupParameters); MaximumTtlSeconds=$AdminsMaximumTtlSeconds; AllowedSecondFactorTypes=(Get-FactorPolicy $AdminsMfa) },
    [pscustomobject]@{ Name='TestGroup3'; Group=(Get-ADGroup -Identity $Tier0Group @groupParameters); MaximumTtlSeconds=$Tier0MaximumTtlSeconds; AllowedSecondFactorTypes=(Get-FactorPolicy $Tier0Mfa) }) | ForEach-Object { $_ | Add-Member -NotePropertyName RequiresSecondFactor -NotePropertyValue ($_.AllowedSecondFactorTypes -ne 0) -PassThru }

if (-not $PSCmdlet.ShouldProcess("DirectoryScope $DirectoryScopeId", "Provision $ActorSamAccountName -> $TargetSamAccountName MVP entitlements")) { return }
$connection = [System.Data.SqlClient.SqlConnection]::new($SqlConnectionString)
$connection.Open(); $script:SqlTransaction = $connection.BeginTransaction()
try {
    $now = [datetimeoffset]::UtcNow
    $scope = Invoke-Scalar $connection 'SELECT DirectoryScopeId FROM dbo.DirectoryScopes WHERE DirectoryScopeId=@id' @{ '@id'=$DirectoryScopeId }
    if (-not $scope) { Invoke-NonQuery $connection 'INSERT dbo.DirectoryScopes (DirectoryScopeId,StableScopeIdentifier,DisplayName,IsActive,CreatedUtc,ModifiedUtc) VALUES (@id,@stable,@display,1,@now,@now)' @{ '@id'=$DirectoryScopeId; '@stable'="configured:$ForestDnsName/$DomainDnsName"; '@display'="$ForestDnsName ($DomainDnsName)"; '@now'=$now } }
    $actorAccountId = Get-OrCreateAccount $connection $DirectoryScopeId $actor $now
    $targetAccountId = Get-OrCreateAccount $connection $DirectoryScopeId $target $now
    $personId = Invoke-Scalar $connection 'SELECT PersonId FROM dbo.PersonAccountLinks WHERE AccountId=@accountId AND IsActive=1' @{ '@accountId'=$actorAccountId }
    if (-not $personId) { $personId=[guid]::NewGuid(); Invoke-NonQuery $connection 'INSERT dbo.Persons (PersonId,DisplayName,IsActive,ValidFromUtc,CreatedUtc,ModifiedUtc) VALUES (@id,@display,1,@now,@now,@now)' @{ '@id'=$personId; '@display'=(Get-DisplayValue $actor); '@now'=$now } }
    foreach($link in @([pscustomobject]@{ AccountId=$actorAccountId; Authenticate=$true; Target=$false },[pscustomobject]@{ AccountId=$targetAccountId; Authenticate=$false; Target=$true })) {
        $linkId = Invoke-Scalar $connection 'SELECT PersonAccountLinkId FROM dbo.PersonAccountLinks WHERE AccountId=@accountId' @{ '@accountId'=$link.AccountId }
        if ($linkId) { Invoke-NonQuery $connection 'UPDATE dbo.PersonAccountLinks SET PersonId=@person,MayAuthenticate=@auth,MayReceivePrivileges=@target,IsActive=1,ModifiedBy=@by,ModifiedUtc=@now WHERE PersonAccountLinkId=@id' @{ '@person'=$personId; '@auth'=$link.Authenticate; '@target'=$link.Target; '@by'='Setup'; '@now'=$now; '@id'=$linkId } }
        else { Invoke-NonQuery $connection 'INSERT dbo.PersonAccountLinks (PersonAccountLinkId,PersonId,AccountId,MayAuthenticate,MayReceivePrivileges,IsActive,ValidFromUtc,CreatedBy,ModifiedBy,CreatedUtc,ModifiedUtc) VALUES (@id,@person,@account,@auth,@target,1,@now,@by,@by,@now,@now)' @{ '@id'=[guid]::NewGuid(); '@person'=$personId; '@account'=$link.AccountId; '@auth'=$link.Authenticate; '@target'=$link.Target; '@now'=$now; '@by'='Setup' } }
    }
    foreach($definition in $groups) {
        $group = $definition.Group; $targetGroupId = Invoke-Scalar $connection 'SELECT TargetGroupId FROM dbo.TargetGroups WHERE DirectoryScopeId=@scope AND ObjectGuid=@guid' @{ '@scope'=$DirectoryScopeId; '@guid'=$group.ObjectGUID }
        if (-not $targetGroupId) { $targetGroupId=[guid]::NewGuid(); $policyId=[guid]::NewGuid(); Invoke-NonQuery $connection 'INSERT dbo.GroupPolicies (GroupPolicyId,MinimumTtlSeconds,MaximumTtlSeconds,DefaultTtlSeconds,AllowedTtlStepSeconds,RequiresSecondFactor,AllowedSecondFactorTypes,RequiresTicket,RequiresApproval,IsActive,CreatedUtc,ModifiedUtc) VALUES (@id,1800,@max,3600,1800,@mfa,@factors,0,0,1,@now,@now)' @{ '@id'=$policyId; '@max'=$definition.MaximumTtlSeconds; '@mfa'=$definition.RequiresSecondFactor; '@factors'=$definition.AllowedSecondFactorTypes; '@now'=$now }; Invoke-NonQuery $connection 'INSERT dbo.TargetGroups (TargetGroupId,DirectoryScopeId,ObjectGuid,ObjectSid,SamAccountName,DistinguishedName,DomainQualifiedName,DisplayName,GroupPolicyId,IsEnabledForRequests,IsWithinAllowedScope,LastVerifiedUtc,CreatedUtc,ModifiedUtc) VALUES (@id,@scope,@guid,@sid,@sam,@dn,@qualified,@display,@policy,1,1,@now,@now,@now)' @{ '@id'=$targetGroupId; '@scope'=$DirectoryScopeId; '@guid'=$group.ObjectGUID; '@sid'=$group.ObjectSid.Value; '@sam'=$group.SamAccountName; '@dn'=$group.DistinguishedName; '@qualified'="$DomainDnsName\$($group.SamAccountName)"; '@display'=(Get-DisplayValue $group); '@policy'=$policyId; '@now'=$now } }
        else { $policyId = Invoke-Scalar $connection 'SELECT GroupPolicyId FROM dbo.TargetGroups WHERE TargetGroupId=@id' @{ '@id'=$targetGroupId }; Invoke-NonQuery $connection 'UPDATE dbo.GroupPolicies SET MinimumTtlSeconds=1800,MaximumTtlSeconds=@max,DefaultTtlSeconds=3600,AllowedTtlStepSeconds=1800,RequiresSecondFactor=@mfa,AllowedSecondFactorTypes=@factors,RequiresTicket=0,RequiresApproval=0,IsActive=1,ModifiedUtc=@now WHERE GroupPolicyId=@id' @{ '@id'=$policyId; '@max'=$definition.MaximumTtlSeconds; '@mfa'=$definition.RequiresSecondFactor; '@factors'=$definition.AllowedSecondFactorTypes; '@now'=$now } }
        $entitlementId=Invoke-Scalar $connection 'SELECT EntitlementId FROM dbo.DirectEntitlements WHERE PersonId=@person AND TargetAccountId=@target AND TargetGroupId=@group' @{ '@person'=$personId; '@target'=$targetAccountId; '@group'=$targetGroupId }
        if (-not $entitlementId) { Invoke-NonQuery $connection 'INSERT dbo.DirectEntitlements (EntitlementId,PersonId,TargetAccountId,TargetGroupId,IsActive,ValidFromUtc,CreatedBy,ModifiedBy,CreatedUtc,ModifiedUtc) VALUES (@id,@person,@target,@group,1,@now,@by,@by,@now,@now)' @{ '@id'=[guid]::NewGuid(); '@person'=$personId; '@target'=$targetAccountId; '@group'=$targetGroupId; '@now'=$now; '@by'='Setup' } }
    }
    $script:SqlTransaction.Commit(); Write-Output "MVP authorization provisioned. DirectoryScopeId=$DirectoryScopeId; Actor=$($actor.SamAccountName); Target=$($target.SamAccountName)."
} catch { $script:SqlTransaction.Rollback(); throw } finally { $connection.Dispose(); Remove-Variable -Name SqlTransaction -Scope Script -ErrorAction SilentlyContinue }

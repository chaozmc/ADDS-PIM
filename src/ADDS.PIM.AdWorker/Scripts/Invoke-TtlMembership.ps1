[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $DomainController,
    [Parameter(Mandatory)] [guid] $TargetAccountObjectGuid,
    [Parameter(Mandatory)] [guid] $TargetGroupObjectGuid,
    [Parameter(Mandatory)] [long] $RequestedTtlSeconds
)

$ErrorActionPreference = 'Stop'
Import-Module ActiveDirectory -ErrorAction Stop

function Write-Result([string] $Kind, [Nullable[long]] $RemainingTtlSeconds) {
    [ordered]@{ kind = $Kind; remainingTtlSeconds = $RemainingTtlSeconds } |
        ConvertTo-Json -Compress
}

function Get-DirectTtlMembership([Microsoft.ActiveDirectory.Management.ADGroup] $Group, [Microsoft.ActiveDirectory.Management.ADUser] $User) {
    $members = (Get-ADGroup -Identity $Group -Server $DomainController -Properties member -ShowMemberTimeToLive).member
    foreach ($value in $members) {
        $text = [string]$value
        if ($text -match '^<TTL=(?<ttl>\d+)>,(?<dn>.+)$') {
            if ($Matches.dn.Equals($User.DistinguishedName, [StringComparison]::OrdinalIgnoreCase)) {
                return [pscustomobject]@{ IsPresent = $true; RemainingTtlSeconds = [long]$Matches.ttl }
            }
        }
        elseif ($text.Equals($User.DistinguishedName, [StringComparison]::OrdinalIgnoreCase)) {
            return [pscustomobject]@{ IsPresent = $true; RemainingTtlSeconds = $null }
        }
    }
    return [pscustomobject]@{ IsPresent = $false; RemainingTtlSeconds = $null }
}

try {
    $user = Get-ADUser -Identity $TargetAccountObjectGuid -Server $DomainController
    $group = Get-ADGroup -Identity $TargetGroupObjectGuid -Server $DomainController
    $membership = Get-DirectTtlMembership $group $user
    if ($membership.IsPresent) {
        Write-Result 'ExistingMembership' $membership.RemainingTtlSeconds
        exit 0
    }

    Add-ADGroupMember -Identity $group -Members $user -MemberTimeToLive (New-TimeSpan -Seconds $RequestedTtlSeconds) -Server $DomainController
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Seconds 2
        $membership = Get-DirectTtlMembership $group $user
        if ($membership.IsPresent -and $null -ne $membership.RemainingTtlSeconds) {
            $remaining = $membership.RemainingTtlSeconds
            if ($remaining -gt 0 -and $remaining -le $RequestedTtlSeconds -and $remaining -ge ($RequestedTtlSeconds - 45)) {
                Write-Result 'Verified' $remaining
                exit 0
            }
        }
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    Write-Result 'VerificationFailed' $null
    exit 0
}
catch {
    Write-Error $_
    exit 1
}

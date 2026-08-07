[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ExpectedDomain,
    [string] $DomainController
)

$ErrorActionPreference = 'Stop'
$checks = [System.Collections.Generic.List[object]]::new()
function Add-Check([string] $Name, [bool] $Passed, [string] $Detail) { $checks.Add([ordered]@{ name = $Name; passed = $Passed; detail = $Detail }) }

$currentDomain = $env:USERDNSDOMAIN
Add-Check 'DomainMembership' ($currentDomain -and $currentDomain.Equals($ExpectedDomain, [StringComparison]::OrdinalIgnoreCase)) "Current domain: $currentDomain"
$module = Get-Module -ListAvailable -Name ActiveDirectory | Select-Object -First 1
Add-Check 'ActiveDirectoryPowerShellModule' ($null -ne $module) ($(if ($module) { $module.Path } else { 'Install RSAT Active Directory tools.' }))
if ($module) {
    try {
        Import-Module ActiveDirectory -ErrorAction Stop
        $domain = Get-ADDomain -Server $DomainController -ErrorAction Stop
        Add-Check 'DirectoryQuery' $true "DNS root: $($domain.DNSRoot); forest: $($domain.Forest)"
        Add-Check 'ExpectedDomain' ($domain.DNSRoot.Equals($ExpectedDomain, [StringComparison]::OrdinalIgnoreCase)) "Resolved domain: $($domain.DNSRoot)"
        $pam = Get-ADOptionalFeature -Filter "Name -eq 'Privileged Access Management Feature'" -Server $DomainController -ErrorAction Stop
        Add-Check 'PamOptionalFeature' ($null -ne $pam -and $pam.EnabledScopes.Count -gt 0) ($(if ($pam) { "Enabled scopes: $($pam.EnabledScopes -join ', ')" } else { 'PAM optional feature was not found.' }))
    } catch { Add-Check 'DirectoryQuery' $false $_.Exception.Message }
}
[ordered]@{ generatedUtc = [DateTimeOffset]::UtcNow.ToString('O'); expectedDomain = $ExpectedDomain; domainController = $DomainController; passed = ($checks | Where-Object { -not $_.passed }).Count -eq 0; checks = $checks } | ConvertTo-Json -Depth 4

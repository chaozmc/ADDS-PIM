#Requires -Version 5.1
<#
.SYNOPSIS
Step 9 of the ADDS-PIM installer: post-install verification and summary.

.DESCRIPTION
Checks the API liveness endpoint, IIS app pool state, and the Worker
service state. This is a smoke test, not a functional proof - it confirms
the processes are up, not that a real signed request/TTL grant/read-back
succeeds end to end. That still needs a real browser session per
dev-docs/10-operations/reproducible-single-host-installation.md.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ApiHostName,
    [Parameter(Mandatory)] [string] $DirectoryScopeId,
    [ValidateNotNullOrEmpty()] [string] $WebSiteName = 'ADDS.PIM.Web',
    [ValidateNotNullOrEmpty()] [string] $ApiSiteName = 'ADDS.PIM.Api',
    [ValidateNotNullOrEmpty()] [string] $WorkerServiceName = 'ADDS.PIM.AdWorker'
)

. (Join-Path $PSScriptRoot '_Common.ps1')
Write-AddsPimStepHeader '09 - Verify'

$problems = [System.Collections.Generic.List[string]]::new()

try {
    Import-Module WebAdministration -ErrorAction Stop
    foreach ($poolName in @($WebSiteName, $ApiSiteName)) {
        $state = (Get-WebAppPoolState -Name $poolName -ErrorAction Stop).Value
        if ($state -eq 'Started') { Write-Output "IIS app pool '$poolName': Started" }
        else { $problems.Add("IIS app pool '$poolName' is '$state', expected 'Started'.") }
    }
}
catch { $problems.Add("Could not read IIS app pool state: $_") }

try {
    $service = Get-Service -Name $WorkerServiceName -ErrorAction Stop
    if ($service.Status -eq 'Running') { Write-Output "Worker service '$WorkerServiceName': Running" }
    else { $problems.Add("Worker service '$WorkerServiceName' is '$($service.Status)', expected 'Running'.") }
}
catch { $problems.Add("Could not read Worker service state: $_") }

try {
    # Windows PowerShell 5.1's Invoke-WebRequest has no -SkipCertificateCheck
    # (that is PowerShell 7+ only); the install's own certificates - freshly
    # issued or self-signed - are not necessarily yet trusted by this
    # process, so bypass validation for this one local smoke-test call only.
    $previousCallback = [System.Net.ServicePointManager]::ServerCertificateValidationCallback
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
    try {
        $response = Invoke-WebRequest -Uri "https://$ApiHostName/health/live" -UseBasicParsing -ErrorAction Stop
    }
    finally {
        [System.Net.ServicePointManager]::ServerCertificateValidationCallback = $previousCallback
    }
    if ($response.StatusCode -eq 204) { Write-Output "API liveness (https://$ApiHostName/health/live): 204" }
    else { $problems.Add("API liveness returned HTTP $($response.StatusCode), expected 204.") }
}
catch { $problems.Add("API liveness check failed: $_") }

Write-Output ''
if ($problems.Count -gt 0) {
    Write-Warning 'Verification found problems:'
    $problems | ForEach-Object { Write-Warning "  - $_" }
}
else {
    Write-Output 'All automated checks passed.'
}

Write-Output ''
Write-Output '=== Installation summary ==='
Write-Output "DirectoryScope $DirectoryScopeId exists in the database, but no Persons, target groups,"
Write-Output 'policies, or entitlements have been created - that is real operational data and belongs'
Write-Output 'in the admin UI, not this installer. Next steps for an operator:'
Write-Output '  1. Sign in to the Web site in a Kerberos-capable intranet browser session.'
Write-Output '  2. In /admin/persons, onboard the first Person from their AD account.'
Write-Output '  3. In /admin/groups, register a target group with a TTL policy.'
Write-Output '  4. In /admin/entitlements, grant that Person a direct entitlement to the group.'
Write-Output '  5. Submit a real membership request end to end and confirm it reaches Succeeded'
Write-Output '     only after the Worker executes it and the AD read-back verifies the TTL.'

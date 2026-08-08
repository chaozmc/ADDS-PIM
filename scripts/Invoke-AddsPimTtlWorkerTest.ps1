[CmdletBinding()]
param(
    [Parameter(Mandatory)] [guid] $TargetAccountObjectGuid,
    [Parameter(Mandatory)] [guid] $TargetGroupObjectGuid,
    [Parameter(Mandatory)] [string] $DomainController,
    [Parameter(Mandatory)] [string] $WorkerGmsa,
    [ValidateRange(60, 3600)] [long] $RequestedTtlSeconds = 900
)

$ErrorActionPreference = 'Stop'
$taskName = 'ADDS-PIM-OneTime-TtlTest'
$workerAssembly = Join-Path $PSScriptRoot '..\src\ADDS.PIM.AdWorker\bin\Debug\net10.0\ADDS.PIM.AdWorker.dll'
$workerAssembly = (Resolve-Path $workerAssembly).Path
$dotnetExecutable = (Get-Command dotnet.exe -ErrorAction Stop).Source
$resultDirectory = Join-Path $env:ProgramData 'ADDS-PIM\TestResults'
$resultFile = Join-Path $resultDirectory 'ldap-ttl-test.json'

try {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
    & icacls $resultDirectory /grant "${WorkerGmsa}:(M)" | Out-Null
    Remove-Item -LiteralPath $resultFile -Force -ErrorAction SilentlyContinue
    $arguments = "`"$workerAssembly`" --ldap-ttl-test --domain-controller $DomainController --target-account-object-guid $TargetAccountObjectGuid --target-group-object-guid $TargetGroupObjectGuid --requested-ttl-seconds $RequestedTtlSeconds --result-file `"$resultFile`""
    $action = New-ScheduledTaskAction -Execute $dotnetExecutable -Argument $arguments
    $principal = New-ScheduledTaskPrincipal -UserId $WorkerGmsa -LogonType Password -RunLevel Highest
    Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Force | Out-Null
    Start-ScheduledTask -TaskName $taskName
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        Start-Sleep -Seconds 2
        $task = Get-ScheduledTask -TaskName $taskName
        $info = Get-ScheduledTaskInfo -TaskName $taskName
    } while ($task.State -eq 'Running' -and [DateTime]::UtcNow -lt $deadline)

    if ($task.State -eq 'Running') { throw 'Worker test timed out.' }
    if ($info.LastTaskResult -ne 0) { throw "Worker test failed with task result $($info.LastTaskResult): $(Get-Content -Raw -LiteralPath $resultFile -ErrorAction SilentlyContinue)" }
}
finally {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
}

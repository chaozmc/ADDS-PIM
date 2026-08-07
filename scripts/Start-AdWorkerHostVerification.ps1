[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $WorkerAssemblyPath,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $StandardOutputPath,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $StandardErrorPath,

    [Parameter(Mandatory)]
    [ValidatePattern('^[^\\]+\\[^\\]+\$$')]
    [string] $WorkerGmsa,

    [string] $TaskName = 'ADDS-PIM-Worker-Kestrel-Verification'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this verification script in an elevated PowerShell session.'
}

$runnerPath = Join-Path $PSScriptRoot 'Run-AdWorkerHostProcess.ps1'
$powerShellPath = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
if (-not (Test-Path -LiteralPath $runnerPath)) {
    throw 'The signed Worker host runner script was not found.'
}

$arguments = "-NoProfile -NonInteractive -ExecutionPolicy AllSigned -File `"$runnerPath`" -WorkerAssemblyPath `"$WorkerAssemblyPath`" -StandardOutputPath `"$StandardOutputPath`" -StandardErrorPath `"$StandardErrorPath`""
$action = New-ScheduledTaskAction -Execute $powerShellPath -Argument $arguments -WorkingDirectory (Split-Path -Parent $WorkerAssemblyPath)
$principal = New-ScheduledTaskPrincipal -UserId $WorkerGmsa -LogonType Password -RunLevel Highest
$task = New-ScheduledTask -Action $action -Principal $principal
Register-ScheduledTask -TaskName $TaskName -InputObject $task -Force | Out-Null
Start-ScheduledTask -TaskName $TaskName

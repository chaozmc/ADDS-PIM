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
    [string] $StandardErrorPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$dotnetPath = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath) -or -not (Test-Path -LiteralPath $WorkerAssemblyPath)) {
    throw 'The configured Worker process files were not found.'
}

$process = Start-Process -FilePath $dotnetPath -ArgumentList @($WorkerAssemblyPath) -WorkingDirectory (Split-Path -Parent $WorkerAssemblyPath) -RedirectStandardOutput $StandardOutputPath -RedirectStandardError $StandardErrorPath -PassThru -Wait
exit $process.ExitCode

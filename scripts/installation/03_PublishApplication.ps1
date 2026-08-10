#Requires -Version 5.1
<#
.SYNOPSIS
Step 3 of the ADDS-PIM installer: publish Web, API, and the AD Worker.

.OUTPUTS
A PSCustomObject with the publish output paths for later steps.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateNotNullOrEmpty()] [string] $PublishRoot = 'C:\ADDS-PIM\Publish',
    [ValidateNotNullOrEmpty()] [string] $WorkerPublishRoot = 'C:\ADDS-PIM\Worker',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string] $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)

. (Join-Path $PSScriptRoot '_Common.ps1')
Write-AddsPimStepHeader '03 - Publish application'

$webProject = Join-Path $RepositoryRoot 'src\ADDS.PIM.Web\ADDS.PIM.Web.csproj'
$apiProject = Join-Path $RepositoryRoot 'src\ADDS.PIM.Api\ADDS.PIM.Api.csproj'
$workerProject = Join-Path $RepositoryRoot 'src\ADDS.PIM.AdWorker\ADDS.PIM.AdWorker.csproj'
$webTarget = Join-Path $PublishRoot 'ADDS.PIM.Web'
$apiTarget = Join-Path $PublishRoot 'ADDS.PIM.Api'

foreach ($path in @($webProject, $apiProject, $workerProject)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Project file not found: $path" }
}
foreach ($target in @($webTarget, $apiTarget, $WorkerPublishRoot)) {
    if (-not (Test-Path -LiteralPath $target)) {
        if ($PSCmdlet.ShouldProcess($target, 'Create publish directory')) { $null = New-Item -ItemType Directory -Path $target -Force }
    }
}

foreach ($deployment in @(
    @($webProject, $webTarget, 'Web'),
    @($apiProject, $apiTarget, 'API'),
    @($workerProject, $WorkerPublishRoot, 'AD Worker')
)) {
    if ($PSCmdlet.ShouldProcess($deployment[1], "Publish $($deployment[2])")) {
        Write-Host "Publishing $($deployment[2]) to $($deployment[1]) ..."
        & dotnet publish $deployment[0] --configuration $Configuration --output $deployment[1] | Write-Host
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($deployment[0])." }
    }
}

Write-Host ''
Write-Host "Step 03 complete: Web -> $webTarget, API -> $apiTarget, Worker -> $WorkerPublishRoot"

[PSCustomObject]@{
    WebPublishPath    = $webTarget
    ApiPublishPath    = $apiTarget
    WorkerPublishPath = $WorkerPublishRoot
    WorkerAssemblyPath = (Join-Path $WorkerPublishRoot 'ADDS.PIM.AdWorker.dll')
    InfrastructureProjectPath = (Join-Path $RepositoryRoot 'src\ADDS.PIM.Infrastructure\ADDS.PIM.Infrastructure.csproj')
}

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $PublishRoot,
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$webProject = Join-Path $repositoryRoot 'src\ADDS.PIM.Web\ADDS.PIM.Web.csproj'
$apiProject = Join-Path $repositoryRoot 'src\ADDS.PIM.Api\ADDS.PIM.Api.csproj'
$webTarget = Join-Path $PublishRoot 'ADDS.PIM.Web'
$apiTarget = Join-Path $PublishRoot 'ADDS.PIM.Api'

foreach ($target in @($webTarget, $apiTarget)) {
    if (-not (Test-Path -LiteralPath $target)) {
        if ($PSCmdlet.ShouldProcess($target, 'Create application publish directory')) {
            $null = New-Item -ItemType Directory -Path $target -Force
        }
    }
}

foreach ($deployment in @(@($webProject, $webTarget), @($apiProject, $apiTarget))) {
    if ($PSCmdlet.ShouldProcess($deployment[1], "Publish $($deployment[0])")) {
        & dotnet publish $deployment[0] --configuration $Configuration --no-restore --output $deployment[1]
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($deployment[0])." }
    }
}

Write-Output "Published Web to $webTarget and API to $apiTarget."

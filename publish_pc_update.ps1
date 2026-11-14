param(
    [Parameter(Mandatory=$true)][string]$Version,
    [string]$Notes = 'ปรับปรุง Nexus Auto-Rejoin PC'
)
$ErrorActionPreference = 'Stop'
& python (Join-Path $PSScriptRoot 'Auto-Rejoin-PC\checks\PublishRelease.py') --repo $PSScriptRoot --version $Version --notes $Notes
if ($LASTEXITCODE -ne 0) { throw 'Release preparation failed' }
& git -C $PSScriptRoot add -- Auto-Rejoin-PC
if ($LASTEXITCODE -ne 0) { throw 'Git staging failed' }
& git -C $PSScriptRoot diff --cached --quiet
if ($LASTEXITCODE -eq 1) {
    & git -C $PSScriptRoot commit -m "Release PC $Version"
    if ($LASTEXITCODE -ne 0) { throw 'Git commit failed' }
}
& git -c credential.interactive=never -C $PSScriptRoot push origin HEAD:main
if ($LASTEXITCODE -ne 0) { throw 'Git push failed' }

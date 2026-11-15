param([switch]$RealPreflight, [string]$BuildOutput = 'NexusRobloxManager.updated.exe')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
$checkExe = Join-Path $projectRoot 'NexusRejoinChecks.exe'
Push-Location $projectRoot
try {
    & .\Build.bat $BuildOutput
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    & $compilerPath /nologo /target:exe "/out:$checkExe" "/r:$BuildOutput" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll checks\RejoinChecks.cs checks\UiLayoutChecks.cs checks\GuardChecks.cs checks\VisibilityChecks.cs checks\AccountRejoinChecks.cs checks\UpdateChecks.cs checks\RealLoginTimeoutChecks.cs checks\RealProcessShutdownChecks.cs
    if ($LASTEXITCODE -ne 0) { throw 'Check compilation failed' }
    if ($RealPreflight) { & $checkExe --real-preflight } else { & $checkExe }
    if ($LASTEXITCODE -ne 0) { throw 'Rejoin checks failed' }
}
finally {
    if (Test-Path -LiteralPath $checkExe) { Remove-Item -LiteralPath $checkExe }
    Pop-Location
}

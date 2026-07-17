<#
.SYNOPSIS
Builds the PulseBar per-user installer. Stages the same publish output as the
portable build, then compiles packaging/installer/PulseBar.iss with Inno Setup 6.

.NOTES
Requires Inno Setup 6 (ISCC.exe). Install: winget install JRSoftware.InnoSetup
#>
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$stage = Join-Path $repoRoot "artifacts\portable\PulseBar"

if (-not (Test-Path (Join-Path $stage "PulseBar.exe"))) {
    Write-Host "Stage not found; running the portable build first..."
    & (Join-Path $repoRoot "packaging\portable\build-portable.ps1") -Configuration $Configuration
}

$iscc = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
if (-not $iscc) {
    $candidates = @(
        "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    $iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
else {
    $iscc = $iscc.Source
}

if (-not $iscc) {
    Write-Error "Inno Setup 6 (ISCC.exe) not found. Install it with: winget install JRSoftware.InnoSetup"
    exit 1
}

& $iscc (Join-Path $PSScriptRoot "PulseBar.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC failed." }

$setup = Join-Path $repoRoot "artifacts\PulseBar-setup-win-x64.exe"
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$setup.sha256" -Value "$hash  $(Split-Path $setup -Leaf)"
Write-Host "Done: $setup"
Write-Host "SHA-256: $hash"

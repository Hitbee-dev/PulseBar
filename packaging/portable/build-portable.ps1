<#
.SYNOPSIS
Builds the PulseBar portable ZIP (win-x64, self-contained) plus the WSL bridge
helper (linux-x64, single file), and emits a SHA-256 checksum.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File packaging/portable/build-portable.ps1
#>
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$stage = Join-Path $repoRoot "artifacts\portable\PulseBar"
$zipPath = Join-Path $repoRoot "artifacts\PulseBar-portable-win-x64.zip"

if (Test-Path (Join-Path $repoRoot "artifacts")) {
    Remove-Item (Join-Path $repoRoot "artifacts") -Recurse -Force
}

Write-Host "Publishing PulseBar.App (win-x64, self-contained)..."
dotnet publish (Join-Path $repoRoot "src\PulseBar.App\PulseBar.App.csproj") `
    -c $Configuration -r win-x64 --self-contained true -o $stage
if ($LASTEXITCODE -ne 0) { throw "App publish failed." }

Write-Host "Publishing PulseBar.Bridge (linux-x64 single file, for WSL OTel helper)..."
dotnet publish (Join-Path $repoRoot "src\PulseBar.Bridge\PulseBar.Bridge.csproj") `
    -c $Configuration -r linux-x64 --self-contained true -p:PublishSingleFile=true `
    -o (Join-Path $stage "wsl-bridge")
if ($LASTEXITCODE -ne 0) { throw "Bridge (linux) publish failed." }

Copy-Item (Join-Path $repoRoot "README.md") $stage
Copy-Item (Join-Path $repoRoot "docs") (Join-Path $stage "docs") -Recurse

Write-Host "Creating ZIP..."
Compress-Archive -Path $stage -DestinationPath $zipPath -Force

$hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$zipPath.sha256" -Value "$hash  $(Split-Path $zipPath -Leaf)"

Write-Host "Done:"
Write-Host "  $zipPath"
Write-Host "  SHA-256: $hash"

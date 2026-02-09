# Build switchyard for Windows.
param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

if (-not $Version) {
    try {
        $Version = git describe --tags --always --dirty 2>$null
    } catch {
        $Version = "dev"
    }
    if (-not $Version) { $Version = "dev" }
}

$BuildDir = "bin"
New-Item -ItemType Directory -Force -Path $BuildDir | Out-Null

Write-Host "Building switchyard $Version..."
go build `
    -trimpath `
    -ldflags "-s -w -X main.version=$Version" `
    -o "$BuildDir/switchyard.exe" `
    ./cmd/switchyard

Write-Host "Built: $BuildDir/switchyard.exe"

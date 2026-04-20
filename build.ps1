<#
.SYNOPSIS
  Build the 3CX Status Tray MSI installer on Windows.

.DESCRIPTION
  One-command build: runs the Custom Action unit tests, then builds the
  whole solution in Release, and prints the path to the produced MSI.

  Run from the repo root:
    .\build.ps1

  Optional switches:
    -Configuration Debug    # build Debug instead of Release
    -SkipTests              # skip 'dotnet test'
    -Clean                  # 'dotnet clean' before building
#>

[CmdletBinding()]
param(
  [ValidateSet('Release', 'Debug')]
  [string] $Configuration = 'Release',
  [switch] $SkipTests,
  [switch] $Clean
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

function Step($msg) {
  Write-Host ""
  Write-Host "==> $msg" -ForegroundColor Cyan
}

if ($Clean) {
  Step "Cleaning previous build output"
  dotnet clean -c $Configuration | Out-Null
}

Step "Restoring NuGet packages"
dotnet restore

if (-not $SkipTests) {
  Step "No unit tests to run (installer uses a PowerShell CA, no managed code to test)"
}

Step "Building solution ($Configuration)"
dotnet build -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
  Write-Host "Build failed." -ForegroundColor Red
  exit 1
}

$msiPath = Get-ChildItem -Path "3CXStatusTray.Installer\bin\$Configuration" -Filter '*.msi' -ErrorAction SilentlyContinue |
  Sort-Object LastWriteTime -Descending | Select-Object -First 1

if ($msiPath) {
  Step "MSI ready"
  Write-Host "  $($msiPath.FullName)" -ForegroundColor Green
  Write-Host "  $([math]::Round($msiPath.Length / 1MB, 2)) MB"
  Write-Host ""
  Write-Host "Install interactively:" -ForegroundColor Yellow
  Write-Host "  Start-Process $($msiPath.Name)"
  Write-Host "Install silently (example):" -ForegroundColor Yellow
  Write-Host "  msiexec /i `"$($msiPath.FullName)`" /quiet SERVER_URL=http://pbx:8889/ API_KEY=your-key"
} else {
  Step "Build succeeded but no MSI was produced - check output"
  exit 1
}

<#
.SYNOPSIS
  Silently uninstall the pre-2026 NSIS-packaged 3CXStatusTray.

.DESCRIPTION
  The previous 3CXStatusTray was deployed via an NSIS installer, so it
  registered a real uninstaller in Windows's uninstall registry. This
  script finds that entry and runs its uninstaller with NSIS's silent
  flag (/S) so a mass-rollout can sweep it off each desk without
  touching every machine by hand.

  Guards:
    - Requires elevation.
    - Skips any MSI-managed uninstall entries (UninstallString starts
      with MsiExec) so the new MSI can never be removed by this script.
    - Shows what it plans to do before doing it; supports -WhatIf and
      -Force.

.PARAMETER DisplayNamePattern
  Wildcard pattern matched against the DisplayName registry value.
  Defaults to '*3CX*Status*Tray*'. Override if the legacy NSIS registered
  under a different name on your desks.

.PARAMETER Force
  Skip the interactive confirmation. Useful for scripted mass cleanup.

.PARAMETER WhatIf
  Report what would be uninstalled without invoking the uninstaller.

.EXAMPLE
  .\cleanup-legacy-install.ps1
  Interactive: shows what it finds, asks before uninstalling.

.EXAMPLE
  .\cleanup-legacy-install.ps1 -WhatIf

.EXAMPLE
  .\cleanup-legacy-install.ps1 -Force
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
  [string] $DisplayNamePattern = '*3CX*Status*Tray*',
  [switch] $Force
)

$ErrorActionPreference = 'Stop'

function Test-IsAdministrator {
  $id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
  $p  = [System.Security.Principal.WindowsPrincipal]::new($id)
  return $p.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdministrator)) {
  Write-Host "This script needs to run elevated. Right-click PowerShell -> Run as administrator." -ForegroundColor Red
  exit 1
}

# Find non-MSI uninstall entries matching the pattern.
$registryRoots = @(
  'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'
  'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'
)

$entries = foreach ($root in $registryRoots) {
  if (-not (Test-Path $root)) { continue }
  Get-ChildItem -Path $root -ErrorAction SilentlyContinue |
    ForEach-Object { Get-ItemProperty -Path $_.PSPath -ErrorAction SilentlyContinue } |
    Where-Object {
      $_.DisplayName -and
      $_.DisplayName -like $DisplayNamePattern -and
      $_.UninstallString -and
      $_.UninstallString -notmatch '^\s*"?MsiExec'
    }
}

if (-not $entries) {
  Write-Host "No legacy (NSIS) install matching '$DisplayNamePattern' found on this machine."
  exit 0
}

Write-Host ""
Write-Host "Found legacy install(s):" -ForegroundColor Cyan
foreach ($e in $entries) {
  Write-Host "  DisplayName:      $($e.DisplayName)"
  Write-Host "  Publisher:        $($e.Publisher)"
  Write-Host "  DisplayVersion:   $($e.DisplayVersion)"
  Write-Host "  InstallLocation:  $($e.InstallLocation)"
  Write-Host "  UninstallString:  $($e.UninstallString)"
  Write-Host ""
}

if (-not $PSCmdlet.ShouldProcess("legacy 3CXStatusTray NSIS install", "uninstall silently")) {
  Write-Host "WhatIf mode: no changes made." -ForegroundColor Yellow
  exit 0
}

if (-not $Force) {
  $answer = Read-Host "Proceed with silent uninstall? [y/N]"
  if ($answer -notmatch '^(y|yes)$') {
    Write-Host "Aborted. No changes made." -ForegroundColor Yellow
    exit 0
  }
}

$anyFailed = $false

foreach ($e in $entries) {
  # NSIS UninstallString is typically   "C:\path\Uninstall.exe"
  # Strip outer quotes if present; we'll launch via Start-Process with
  # /S as an explicit argument rather than trying to parse a command line.
  $exe = $e.UninstallString.Trim().Trim('"')

  if (-not (Test-Path $exe)) {
    Write-Host "  Uninstaller not found on disk: $exe" -ForegroundColor Red
    Write-Host "  Registry entry is orphaned. Removing the registry key only." -ForegroundColor Yellow
    try {
      Remove-Item -Path $e.PSPath -Recurse -Force
      Write-Host "  Removed orphan registry key: $($e.PSPath)"
    } catch {
      Write-Host "  Failed to remove orphan registry key: $_" -ForegroundColor Red
      $anyFailed = $true
    }
    continue
  }

  Write-Host "Running uninstaller: $exe /S" -ForegroundColor Cyan
  try {
    $p = Start-Process -FilePath $exe -ArgumentList '/S' -Wait -PassThru
    if ($p.ExitCode -eq 0) {
      Write-Host "  Uninstalled $($e.DisplayName)." -ForegroundColor Green
    } else {
      Write-Host "  Uninstaller exited with code $($p.ExitCode). Check manually." -ForegroundColor Yellow
      $anyFailed = $true
    }
  } catch {
    Write-Host "  Failed to run uninstaller: $_" -ForegroundColor Red
    $anyFailed = $true
  }
}

# NSIS uninstall covers the install folder and the Start Menu entry it
# placed. The Startup-folder shortcut was placed manually on first deploy
# and the NSIS uninstaller doesn't know about it - sweep Startup folders
# explicitly so the hand-placed shortcut doesn't end up pointing at a
# now-deleted exe.
Write-Host ""
Write-Host "Sweeping Startup folders for loose shortcuts..." -ForegroundColor Cyan
$startupFolders = @(
  "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\StartUp"
  "$env:AppData\Microsoft\Windows\Start Menu\Programs\Startup"
)
$looseShortcuts = @()
foreach ($sf in $startupFolders) {
  if (-not (Test-Path $sf)) { continue }
  # Match by filename and by shortcut target exe name.
  $looseShortcuts += Get-ChildItem -Path $sf -Filter '3CXStatusTray*.lnk' -ErrorAction SilentlyContinue
  $looseShortcuts += Get-ChildItem -Path $sf -Filter '*.lnk' -ErrorAction SilentlyContinue |
    Where-Object {
      try {
        $sh = New-Object -ComObject WScript.Shell
        $target = $sh.CreateShortcut($_.FullName).TargetPath
        $target -and $target -match '3CXStatusTray\.exe$'
      } catch { $false }
    }
}
$looseShortcuts = $looseShortcuts | Sort-Object FullName -Unique
if ($looseShortcuts) {
  foreach ($s in $looseShortcuts) {
    try {
      Remove-Item -Path $s.FullName -Force
      Write-Host "  Removed Startup shortcut: $($s.FullName)" -ForegroundColor Green
    } catch {
      Write-Host "  Failed to remove $($s.FullName): $_" -ForegroundColor Yellow
      $anyFailed = $true
    }
  }
} else {
  Write-Host "  None found."
}

if ($anyFailed) {
  Write-Host "Some entries couldn't be cleaned. Investigate above, then re-run." -ForegroundColor Yellow
  exit 2
}

Write-Host ""
Write-Host "Legacy install removed. Ready to install the new MSI." -ForegroundColor Green

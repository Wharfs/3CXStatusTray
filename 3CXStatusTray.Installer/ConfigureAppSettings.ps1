<#
.SYNOPSIS
  Write 3CXStatusTray's appsettings.json at install time from MSI properties.

.DESCRIPTION
  Called as a deferred Custom Action by the MSI. Takes the four tunable
  values captured by the installer (WebApi URL, API key, extension id,
  poll interval) and writes a fresh appsettings.json to the install
  folder. Also records the same values under HKLM\SOFTWARE\3CXStatusTray\Config
  so the next install's <RegistrySearch> can pre-populate the dialog
  fields (that's the "upgrade memory" feature from the design doc).

  Script determines its own install folder via $PSScriptRoot rather than
  receiving it as an argument - the MSI [INSTALLFOLDER] token ends with
  a backslash, which interacts badly with CreateProcess argument quoting
  and leaves PowerShell with a trailing '"' that makes all path ops throw
  'Illegal characters in path'. $PSScriptRoot is always clean.

  Run by the MSI as LocalSystem - don't depend on user profile, cwd,
  env vars, or network paths.
#>

[CmdletBinding()]
param(
  [string] $ServerUrl     = 'http://localhost:8889/',
  [string] $ApiKey        = '',
  [string] $ExtensionId   = '100',
  [int]    $PollIntervalMs = 5000
)

$ErrorActionPreference = 'Stop'
$InstallFolder = $PSScriptRoot

Write-Output "ConfigureAppSettings: InstallFolder=$InstallFolder  ServerUrl=$ServerUrl  ExtensionId=$ExtensionId  PollIntervalMs=$PollIntervalMs"

$settings = [ordered]@{
  Settings = [ordered]@{
    ServerURLBasePath             = $ServerUrl
    ApiKey                        = $ApiKey
    PollIntervalMilliseconds      = $PollIntervalMs
    ExtensionId                   = $ExtensionId
    BalloonTipDisplayMilliseconds = 10000
    Icons = [ordered]@{
      Available   = 'app-on.ico'
      OutOfOffice = 'app-off.ico'
      Default     = 'app-default.ico'
    }
    ProfileShortCodes = [ordered]@{
      Available   = 'available'
      OutOfOffice = 'out_of_office'
    }
  }
}

$json = $settings | ConvertTo-Json -Depth 5
$appSettingsPath = Join-Path $InstallFolder 'appsettings.json'
Set-Content -Path $appSettingsPath -Value $json -Encoding UTF8
Write-Output "ConfigureAppSettings: wrote $appSettingsPath"

# Upgrade-memory registry values. Read back next install via
# <RegistrySearch Type="raw"> in Product.wxs.
#
# EVERYTHING stored as REG_SZ (string) on purpose. Set-ItemProperty
# without -Type infers type from value; .NET [int] -> REG_DWORD. WiX
# Type="raw" reads REG_DWORD back as '#N' (the MSI-native integer
# format), which then doesn't round-trip cleanly through a PowerShell
# [int] param on the next install - cf. the 1253924 ms bug caused by
# exactly this. Force -Type String on everything so what we write is
# what the next install reads back, no type gymnastics.
$regPath = 'HKLM:\SOFTWARE\3CXStatusTray\Config'
if (-not (Test-Path $regPath)) {
  New-Item -Path $regPath -Force | Out-Null
}
Set-ItemProperty -Path $regPath -Name 'ServerUrl'      -Value "$ServerUrl"     -Type String
Set-ItemProperty -Path $regPath -Name 'ApiKey'         -Value "$ApiKey"        -Type String
Set-ItemProperty -Path $regPath -Name 'ExtensionId'    -Value "$ExtensionId"   -Type String
Set-ItemProperty -Path $regPath -Name 'PollIntervalMs' -Value "$PollIntervalMs" -Type String
Write-Output "ConfigureAppSettings: wrote upgrade-memory registry under $regPath"

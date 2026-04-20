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

  Run by the MSI as LocalSystem - don't depend on user profile, cwd,
  env vars, or network paths.
#>

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)] [string] $InstallFolder,
  [string] $ServerUrl     = 'http://localhost:8889/',
  [string] $ApiKey        = '',
  [string] $ExtensionId   = '100',
  [int]    $PollIntervalMs = 5000
)

$ErrorActionPreference = 'Stop'

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

# Upgrade-memory registry values. Read back next install via
# <RegistrySearch> in Product.wxs.
$regPath = 'HKLM:\SOFTWARE\3CXStatusTray\Config'
if (-not (Test-Path $regPath)) {
  New-Item -Path $regPath -Force | Out-Null
}
Set-ItemProperty -Path $regPath -Name 'ServerUrl'     -Value $ServerUrl
Set-ItemProperty -Path $regPath -Name 'ApiKey'        -Value $ApiKey
Set-ItemProperty -Path $regPath -Name 'ExtensionId'   -Value $ExtensionId
Set-ItemProperty -Path $regPath -Name 'PollIntervalMs' -Value $PollIntervalMs

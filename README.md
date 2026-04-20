# 3CX Status Tray

Windows notification-area applet that shows, and lets anyone control, a small office's shared "phones on / phones off" state. Pairs with [3CXStatusWebApi](https://github.com/Wharfs/3CXStatusWebApi) to provide a one-click way for any desk in the office to send inbound calls to voicemail and then turn them back on later — without logging into an admin console or dialling feature codes.

| Icon | Meaning |
|------|---------|
| Green | Phones are taking calls. |
| Red | Phones are set to Out of Office; inbound calls go to voicemail. |
| Grey | Unknown state (service unreachable or returning an unexpected profile). |

**Double-click the icon** to flip every extension between the two states. Trays on every other desk pick the change up on their next 5-second poll. **Right-click** for an Exit menu.

## Why this exists

Small office, around 10 people, about 8 of whom answer phones. Lunch is taken ad-hoc, not on a fixed schedule, so the "everyone is at lunch now, send calls to voicemail" moment is decided socially on the fly. The problem in 2022 was twofold:

1. **3CX had no native way to do this** that a non-admin could trigger from their desk, and
2. **Even if it did, nobody would know** whether the phones were currently on or off — you can't tell by looking at empty desks because some people eat at their desks.

So a desktop tray icon that's simultaneously:

- a **shared action** — any desk can toggle the office between "taking calls" and "voicemail only",
- a **shared status indicator** — the colour is the office's single source of truth, visible from every desk at once, and
- **eventually-consistent across desks** — when one person flips it, everyone else sees the change in about five seconds.

...turns out to be the right shape for the problem. The app is used daily at lunchtime and has been running in production since 2022.

3CX v20's Web Client added a native "Override Office Hours → All departments → duration" dialog that covers the *action* side of this, but it's admin-only and only lives inside the Web Client — not on every user's desk. There's also a long-standing 3CX limitation that means a BLF key on a deskphone cannot reflect another extension's DND or forwarding-profile state, which rules out a phone-side indicator. See the top-level [CLAUDE.md](../CLAUDE.md) in the parent folder (if cloned alongside its sibling repo) for the deep-dive research notes.

## How it works

```
┌──────────────────────┐    poll every 5s       ┌──────────────────┐   COM    ┌───────┐
│  Tray (one per desk) │ ─────────────────────▶│ 3CXStatusWebApi  │ ────────▶│  3CX  │
│  x N desks           │ ◀─────────────────────│   (on PBX host)  │          │  PBX  │
└──────────────────────┘    toggle on click     └──────────────────┘          └───────┘
```

- The tray on each desk polls `GET /status/extension/{ExtensionId}` every 5 seconds (configurable) and colours itself from the response.
- Double-click calls `GET /status/extensions/profile/{shortcode}` to flip every extension in one atomic call.
- The tray icon lives in each user's notification area; the installer drops a Start Menu entry and an all-users Startup shortcut so it launches for every user who logs in at the desk.
- Config lives in `appsettings.json` next to the exe — WebApi URL, optional API key, extension ID to monitor, poll interval, and icon/short-code mappings. Written at install time from the MSI dialog fields.

The tray is intentionally thin. It holds no state beyond "what was the last status I saw?", logs HTTP failures via `ILogger`, and falls back to the grey "unknown" icon on any kind of service unreachability.

## Installation

Ship the `.msi` to each desk — via file share, email, Intune/SCCM, USB, whatever suits. Prerequisites: Windows 10 or 11, [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0) (the installer checks for this and bails with a clean error if it's missing), and a reachable [3CXStatusWebApi](https://github.com/Wharfs/3CXStatusWebApi) deployment.

Pre-built MSIs are attached to each [GitHub Release](https://github.com/Wharfs/3CXStatusTray/releases).

### Interactive install

Double-click the MSI. The wizard prompts for:

| Field | Meaning | Default |
|-------|---------|---------|
| 3CX Status WebApi URL | Where the server-side companion lives. | `http://localhost:8889/` |
| API Key (optional) | `X-API-Key` header value. Leave blank if the WebApi isn't using key auth. | empty |

Finish with "Launch 3CX Status Tray" ticked and the tray appears immediately; otherwise it launches automatically on next login via the all-users Startup shortcut the installer placed.

### Silent install / scripted rollout

```powershell
msiexec /i 3CXStatusTray-2.0.0.msi /quiet /norestart `
  SERVER_URL=http://pbx.office.local:8889/ `
  API_KEY=your-shared-key `
  EXTENSION_ID=100 `
  POLL_INTERVAL_MS=5000
```

Properties (uppercase is load-bearing):

| Property | Exposed in | Default |
|----------|-----------|---------|
| `SERVER_URL` | Interactive dialog + silent | `http://localhost:8889/` |
| `API_KEY` | Interactive dialog + silent | empty |
| `EXTENSION_ID` | Silent only | `100` |
| `POLL_INTERVAL_MS` | Silent only | `5000` |

Upgrades over an existing install pre-populate the dialog with the previous values (stored under `HKLM\SOFTWARE\3CXStatusTray\Config`), so admins don't have to re-type anything. Uninstall removes the install folder, both shortcuts, and all registry values cleanly.

### Migrating from the pre-2026 NSIS install

The original 3CXStatusTray was deployed via an NSIS installer. MSI won't auto-remove an NSIS install on upgrade, so before rolling out the new MSI to desks that have the old version, run `tools/cleanup-legacy-install.ps1` on each desk from an elevated PowerShell. It finds the NSIS uninstall registry entry by display-name pattern (default `*3CX*Status*Tray*`), invokes the NSIS uninstaller silently, sweeps any hand-placed Startup-folder shortcut, and leaves the desk ready for the new MSI. MSI-managed installs are always skipped, so it can't accidentally remove the new version if re-run later.

```powershell
# From an elevated PowerShell
.\tools\cleanup-legacy-install.ps1            # interactive: shows what it finds, asks before removing
.\tools\cleanup-legacy-install.ps1 -WhatIf    # dry run, changes nothing
.\tools\cleanup-legacy-install.ps1 -Force     # scripted mass-rollout; no prompts
```

## Usage

Day-to-day, users don't really think about the tray. It sits in the notification area and updates its colour as the shared state changes.

- **Look at the icon.** Green = phones are on. Red = phones are off (Out of office).
- **Double-click** to flip the whole office. Everyone else's tray updates within five seconds.
- **Exit** the tray via right-click → Exit if you really need to. A confirmation dialog explains what you'll lose (the indicator and the toggle for this desk).

Per-desk personalisation is intentionally absent — the point is that every desk shares the state, so every desk should see and be able to change it identically.

## Configuration

Config lives in `appsettings.json` in the install folder (`C:\Program Files\3CXStatusTray\` by default). Editing it requires admin rights, which is deliberate — the tray is a shared-office-state tool, and letting any logged-in user change the server URL or API key would defeat the point.

```json
{
  "Settings": {
    "ServerURLBasePath": "http://localhost:8889/",
    "ApiKey": "",
    "PollIntervalMilliseconds": 5000,
    "ExtensionId": "100",
    "BalloonTipDisplayMilliseconds": 10000,
    "Icons": {
      "Available": "app-on.ico",
      "OutOfOffice": "app-off.ico",
      "Default": "app-default.ico"
    },
    "ProfileShortCodes": {
      "Available": "available",
      "OutOfOffice": "out_of_office"
    }
  }
}
```

The simplest way to change settings across all desks is to rebuild the MSI with new defaults, or to run it silently with the new properties on each desk via your normal software-distribution channel. The tray doesn't watch the file for changes — restart the applet (Exit, then re-launch from the Start Menu) after editing.

## Building from source

On a Windows box with the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) installed (the tray targets `net8.0-windows` / Windows Forms, so this won't build on Linux or macOS):

```powershell
git clone https://github.com/Wharfs/3CXStatusTray.git
cd 3CXStatusTray
.\build.ps1
```

`build.ps1` restores, builds Release, and produces `3CXStatusTray.Installer\bin\Release\3CXStatusTray-<version>.msi`. Use `-Clean` to wipe previous output first, or `-Configuration Debug` for a debug build.

Version lives in `3CXStatusTray.Installer/3CXStatusTray.Installer.wixproj` → `<Version>`. Bump it before tagging a release.

### Cutting a release

A push of a `v*.*.*` git tag triggers `.github/workflows/release.yml` on a `windows-latest` runner, which builds the MSI and attaches it to an auto-generated GitHub Release:

```bash
# 1. Bump <Version> in 3CXStatusTray.Installer/3CXStatusTray.Installer.wixproj
git commit -am "release: 2.0.1"

# 2. Tag and push. The tag must exactly match the wixproj Version;
#    the workflow verifies this and fails early on mismatch.
git tag v2.0.1
git push origin main
git push origin v2.0.1
```

Release notes are auto-generated from commit messages since the previous tag — conventional-commit-style messages (`fix(tray): ...`, `feat(installer): ...`) render cleanly.

## Design notes

`docs/installer-design.md` captures the decisions behind the MSI — WiX v6, the custom config dialog, silent-install property names, the upgrade-memory mechanism, code-signing deferral, and what's explicitly out of scope.

## License

MIT. See `LICENSE.md`.

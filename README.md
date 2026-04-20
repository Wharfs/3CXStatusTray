# 3CXStatusTray

Windows notification-area applet that shows and controls the shared "phones on / phones off" state of a small office running a 3CX PBX.

- **Green icon** — phones are taking calls as normal.
- **Red icon** — phones are set to Out of Office; inbound calls go to voicemail.
- **Double-click the icon** — flips every extension between the two states. The tray on every other desk updates within 5 seconds.

Designed for a small shared-desk office where people take lunch at different times and the room needs a single visible signal of "are we taking calls right now?" that every desk can both see *and* act on. See the parent repo's `CLAUDE.md` for the full use-case rationale.

Pairs with [3CXStatusWebApi](https://github.com/Wharfs/3CXStatusWebApi), a small HTTP service that runs on the 3CX server itself and wraps the 3CX Call Control API. This tray is a thin client on top of that.

## Prerequisites

- Windows 10 / 11.
- [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0). The installer checks for this and bails with a clean error if it's missing.
- A reachable `3CXStatusWebApi` deployment (on the 3CX server or anywhere the desks can reach over HTTP).

## Install

Grab `3CXStatusTray-<version>.msi` from the repo's [Releases](https://github.com/Wharfs/3CXStatusTray/releases) (or build one — see below).

### Interactive install

Double-click the MSI. The wizard prompts for:

- **3CX Status WebApi URL** (e.g. `http://pbx.office.local:8889/`).
- **API Key** (optional — leave blank if the WebApi isn't using key auth).

On completion tick "Launch 3CX Status Tray" to start the applet immediately; otherwise it launches automatically on next login via a shortcut in the all-users Startup folder.

### Silent / scripted install

```powershell
msiexec /i 3CXStatusTray-2.0.0.msi /quiet /norestart `
  SERVER_URL=http://pbx.office.local:8889/ `
  API_KEY=your-shared-key `
  EXTENSION_ID=100 `
  POLL_INTERVAL_MS=5000
```

`SERVER_URL` and `API_KEY` appear in the interactive dialog; `EXTENSION_ID` and `POLL_INTERVAL_MS` are silent-only overrides. All four default to sensible values if omitted.

Upgrading: run a newer MSI over an existing install and the dialog pre-populates with the previous values (stored under `HKLM\SOFTWARE\3CXStatusTray\Config`). Uninstall removes the install folder, shortcuts, and registry values cleanly.

## Build

On Windows (the tray targets `net8.0-windows` / Windows Forms, so it doesn't build on Linux or macOS):

```powershell
git clone https://github.com/Wharfs/3CXStatusTray.git
cd 3CXStatusTray
.\build.ps1
```

`build.ps1` restores, builds Release, and produces `3CXStatusTray.Installer\bin\Release\3CXStatusTray-<version>.msi`. Use `-Clean` to wipe previous output first.

Version lives in `3CXStatusTray.Installer/3CXStatusTray.Installer.wixproj` → `<Version>`.

## Design docs

`docs/installer-design.md` captures the decisions behind the MSI (WiX v6, dialog sequence, silent-install property names, upgrade-memory mechanism, out-of-scope items).

## License

MIT. See `LICENSE.md`.

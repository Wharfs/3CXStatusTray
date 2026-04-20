# 3CXStatusTray MSI Installer — Design

**Status:** Approved 2026-04-20. Implementation plan to follow.
**Scope:** Workstation installer only. The 3CXStatusWebApi server-side deployment is unchanged (scp + systemd as before).

## Goal

Replace the current "copy the publish output onto each desk and hope the config's right" workflow with a proper Windows MSI:

- Add/Remove Programs entry, standard uninstall, clean upgrade path.
- Install-time dialog prompts for the two essential config values (WebApi URL and API key).
- Silent install via `msiexec /quiet` with property overrides — scriptable for batch rollout.
- Tray autostarts for every user who logs in at that desk.

## Non-goals

- Packaging the WebApi server half.
- Code signing (deferred; admin clicks through SmartScreen once on an 8-desk rollout).
- Group Policy deployment helpers or SCCM templates.
- Per-user install variants (per-machine only).
- Custom branding / dialog themes (standard WiX UI).

## Tool choice

**WiX Toolset v6** (`WixToolset.Sdk/6.0.2`, the latest as of April 2026). SDK-style .NET project, so `dotnet build` produces the MSI — no external toolchain install beyond the .NET 8 SDK we already depend on.

Why not NSIS: NSIS produces a `.exe` installer rather than a native MSI. Worse corporate-deployment story (no Add/Remove Programs upgrade semantics, no `msiexec` scripting, no GPO-friendliness). WiX was chosen on that basis.

## Project layout

New project added to the existing `3CXStatusTray.sln`:

```
3CXStatusTray.Installer/
  3CXStatusTray.Installer.wixproj         MSI build project (WixToolset.Sdk v6)
  Product.wxs                             Product, files, shortcuts, upgrade memory
  Dialogs/ConfigDialog.wxs                Custom install-time dialog (URL + API key)
  CustomActions/
    WriteAppSettings.csproj               Managed Custom Action project (net8.0)
    WriteAppSettings.cs                   Reads MSI properties, writes appsettings.json
```

A `<ProjectReference>` from the installer project to the tray project wires up the build order — `dotnet build 3CXStatusTray.sln -c Release` produces both the tray `.exe` and the `.msi` in one pass.

## Installed file layout

Default install folder (admin-changeable in the dialog):

```
C:\Program Files\3CXStatusTray\
  3CXStatusTray.exe                 Framework-dependent publish (net8.0-windows)
  appsettings.json                  Written by Custom Action from MSI properties
  app-default.ico / app-on.ico / app-off.ico
  [other .NET publish files]
```

## Install-time dialog sequence

Base: WiX's built-in `WixUI_InstallDir` sequence, extended with one custom dialog.

1. Welcome.
2. License (shows `LICENSE.md`).
3. **Configure** (custom dialog):
   - "3CX Status WebApi URL" text field, default `http://localhost:8889/`.
   - "API Key (optional)" text field, default blank. Help line: "Leave blank if the WebApi isn't using key auth."
4. Install folder (default `C:\Program Files\3CXStatusTray\`).
5. Ready → Install.
6. Finish, with a "Launch 3CX Status Tray now" checkbox.

## Silent install

```cmd
msiexec /i 3CXStatusTray-1.0.0.msi /quiet /norestart ^
  SERVER_URL=http://pbx.office.local:8889/ ^
  API_KEY=your-key-here ^
  EXTENSION_ID=100 ^
  POLL_INTERVAL_MS=5000
```

Only `SERVER_URL` and `API_KEY` appear in the interactive dialog. `EXTENSION_ID` and `POLL_INTERVAL_MS` are silent-only overrides, falling back to the tray's `Settings` class defaults when not specified. This matches the rule agreed in brainstorming: "minimal prompted, full overridable silently".

## MSI public property names

| Property         | Dialog field                   | Default (if unset at install)    |
| ---------------- | ------------------------------ | -------------------------------- |
| `SERVER_URL`     | 3CX Status WebApi URL          | `http://localhost:8889/`          |
| `API_KEY`        | API Key (optional)             | `""` (empty)                     |
| `EXTENSION_ID`   | — (silent only)                | `100`                            |
| `POLL_INTERVAL_MS`| — (silent only)               | `5000`                           |

Uppercase is load-bearing — MSI treats mixed-case property names as private and silently ignores them on the command line.

## Config generation — Custom Action

A managed Custom Action DLL (`WriteAppSettings.dll`) runs **deferred**, after file copy, with elevated rights.

It does two things:

1. Serialises the four property values (plus the tray's other built-in defaults) into `C:\Program Files\3CXStatusTray\appsettings.json` using `System.Text.Json`. The JSON shape matches the existing `Settings` class the tray binds against.
2. Writes the same four values to `HKLM\SOFTWARE\3CXStatusTray\Config\` so the next MSI run can read them back — this is the **upgrade memory** mechanism.

Deferred execution means the CA can't read MSI properties directly — they're marshalled via `CustomActionData` (the standard WiX pattern).

## Upgrade memory

`<RegistrySearch>` elements at the top of `Product.wxs` read `HKLM\SOFTWARE\3CXStatusTray\Config\{ServerUrl|ApiKey|ExtensionId|PollIntervalMs}` into the four MSI properties **before** the UI sequence starts.

Effects:

- **Interactive upgrade:** dialog pre-populates with the existing values; admin clicks Next to keep them, or edits to change.
- **Silent upgrade with no properties:** reuses previous values.
- **Silent upgrade with explicit properties:** new properties override the previous values.

This is the same pattern every well-behaved Windows installer uses; admin never has to re-type the `ApiKey` for an upgrade.

## MajorUpgrade

```xml
<MajorUpgrade AllowSameVersionUpgrades="yes"
              DowngradeErrorMessage="A newer version of 3CX Status Tray is already installed." />
```

Shared `UpgradeCode` GUID across all versions (fixed in `Product.wxs`). Versioning lives in `<Version>` in the `wixproj`, starting at `1.0.0`; bump per release.

## Shortcuts

All owned by the MSI — removed cleanly on uninstall.

- `%ProgramData%\Microsoft\Windows\Start Menu\Programs\3CX Status Tray\3CX Status Tray.lnk` — Start Menu entry.
- `%ProgramData%\Microsoft\Windows\Start Menu\Programs\StartUp\3CX Status Tray.lnk` — autostart for every user who logs in at that desk. Admin can delete it locally if a specific desk shouldn't autostart.
- **No desktop shortcut** in v1.

## .NET runtime handling

Published **framework-dependent** (`--self-contained false`). The MSI includes a `<LaunchCondition>` checking for the **.NET 8 Desktop Runtime** (`REGISTRY_NETDESKTOP_8` or equivalent `DetectNETCoreSdk` search), showing a clean error message + a link to the Microsoft download page if missing.

Trade-off vs self-contained: ~10 MB MSI instead of ~90 MB, in exchange for a one-time .NET runtime install on desks that don't already have it. Corporate desks typically do via Windows Update.

## Uninstall behaviour

Standard MSI uninstall removes:

- The install folder and everything in it (including `appsettings.json`).
- Both shortcuts (Start Menu, Startup).
- The entire `HKLM\SOFTWARE\3CXStatusTray\` registry subtree.
- The Add/Remove Programs entry.

Clean slate — reinstall later will prompt afresh for config.

## Build output

```
3CXStatusTray.Installer\bin\Release\3CXStatusTray-<version>.msi
```

Distributed manually (file share, email, USB, whatever the office uses). No auto-update channel.

## Testing

Automated:

- Unit tests for the `WriteAppSettings` Custom Action's JSON serialisation logic (pure function operating on a properties-dictionary → JSON string). Runnable via `dotnet test` on any OS.

Manual (on a Windows box, because the MSI itself is Windows-only to install):

- Fresh install via UI dialog → tray icon appears, icons display correctly, appsettings.json contains the prompted values.
- Fresh install silently via `msiexec /quiet SERVER_URL=... API_KEY=...` → appsettings.json reflects the properties.
- Upgrade from 1.0.0 → 1.0.1 via UI → values pre-populated from previous install.
- Upgrade silently with no properties → previous values preserved.
- Uninstall → install folder, shortcuts, registry entries all gone.
- Launch condition: install on a box without .NET 8 Desktop Runtime → clean error message, no partial install.

## Out of scope but flagged for later

- Code signing. £150/yr OV cert from a CA, added as a post-build step.
- Auto-update via a feed (Squirrel.Windows or similar). Probably never needed for 8 internal desks.
- Per-user install variant. Fork the wxs if this becomes a need.
- Desktop shortcut option. One flag flip in Product.wxs.
- Group Policy `.admx` template for fleet-config. Overkill for 8 desks.

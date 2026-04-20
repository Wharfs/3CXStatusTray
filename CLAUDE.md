# 3CXStatusTray

This is one half of a two-component system. Project-wide context, use case, 3CX v20 platform facts, modernisation tracks, and known issues are all in the **parent folder's CLAUDE.md** — read that first.

Parent `CLAUDE.md` lives at `../CLAUDE.md` when the user has both repos cloned side-by-side under `Code/3cx/`. If you only see this single repo cloned standalone, the context is:

- This is a .NET 8 Windows Forms tray applet, one copy per desk in a small office.
- It polls a sibling service `github.com/Wharfs/3CXStatusWebApi` every 5s to get the current forwarding profile of a configured extension, and shows a red/green/default tray icon accordingly.
- Double-clicking toggles every extension between "Available" and "Out of office".
- The tray icon is simultaneously a *shared action*, *shared status indicator*, and *eventually-consistent* across desks. Do not refactor into a per-user setting.
- Do NOT push to origin without the user pushing manually.

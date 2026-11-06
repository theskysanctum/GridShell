# GridShell

A custom Windows Start Menu / taskbar replacement built around a fullscreen, controller-first
launcher grid — plus a dual-pane file explorer, a download manager with Real-Debrid support,
and deep emulation tooling, all written in C#/.NET and WPF.

GridShell can run alongside your normal desktop (the default), or be registered as your actual
Windows shell for a fully custom, Explorer-free experience (opt-in, with a watchdog safety net
that falls back to `explorer.exe` if anything goes wrong).

## Features

- **Launcher grid** — a fullscreen, Windows-8-Start-Screen-style grid of Apps / Web / Games
  tiles, fully navigable by keyboard, mouse, or game controller (D-pad/stick navigation,
  face-button actions).
- **Taskbar sidebar** — window list, clock, battery (with a click-through to Bluetooth device
  battery levels), reserving real desktop space via the Windows AppBar API.
- **On-screen keyboard** — a controller-navigable keyboard for text entry anywhere in Windows.
- **File Explorer** — a dual-pane, controller-navigable file manager with copy/cut/paste/
  delete/rename/new-folder, archive extract/compress (7-Zip/PeaZip), and disc-image tooling
  (chdman for CHD, extract-xiso for Xbox ISOs).
- **Download Manager** — paste a link or magnet, resolved through [Real-Debrid](https://real-debrid.com)
  when configured, downloaded via a bundled [aria2](https://aria2.github.io/) daemon for
  multi-connection speed. Includes a small Chrome extension that routes browser downloads
  straight into it.
- **Emulation** — 190+ platforms mapped to either RetroArch or the best standalone emulator for
  that system, each verified to actually work and launch in fullscreen. One-click [RetroAchievements](https://retroachievements.org)
  login that configures itself across RetroArch, Dolphin, and PCSX2.
- **Installed-game detection** — scans Steam, Epic Games, and GOG Galaxy for already-installed
  games and adds tiles for them automatically.
- **Appearance** — theme, accent color, tile size, and a system-wide font picker (including
  your own custom font file).

## Project layout

```
src/
  GridShell/           The taskbar/shell process (always running)
  GridShell.Grid/       The launcher grid, Settings, on-screen keyboard, File Explorer,
                         Download Manager (its own long-lived process)
  GridShell.Core/       Shared library — no UI dependencies
  GridShell.Watchdog/    Safety net for shell registration (Phase 3, see below)
```

## Building

Requires .NET 10 SDK and Windows 10 (19041+) or Windows 11.

```bash
dotnet build GridShell.slnx
```

Each project's `bin/Debug/net10.0-windows10.0.19041.0/` folder contains the built executable.
`GridShell.exe` is the taskbar process; `GridShell.Grid.exe` is pre-warmed by it automatically.

## Running

GridShell can run two ways:

1. **Alongside your normal desktop** (safe, reversible, no setup) — just run `GridShell.exe`.
   It hides the real taskbar while running; closing
   it (or the "Restore Explorer" option) restores normal Windows behavior instantly.
2. **As your actual Windows shell** — registers GridShell in place of `explorer.exe` at login,
   via `GridShell.Watchdog`, which relaunches GridShell if it crashes and falls back to
   `explorer.exe` after repeated failures. **Only do this in a VM or a secondary account first**
   — it changes how your whole session boots. Settings has a toggle for this with the relevant
   warnings.

## License

GridShell's own code is MIT-licensed — see [LICENSE](LICENSE). It bundles a handful of
third-party tools and fonts as separate files under their own licenses (chdman, extract-xiso,
aria2, and the bundled font files) — see the `*.LICENSE.*` files alongside each one.

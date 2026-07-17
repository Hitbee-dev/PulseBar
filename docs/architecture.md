# Architecture

## Overview

PulseBar is a single WPF desktop app plus a lightweight bridge executable.

```
PulseBar.App (WPF, DI Host)
├─ PulseBar.Core        — models, provider contract, configuration, localization
├─ PulseBar.Windows     — system metrics (GetSystemTimes, GlobalMemoryStatusEx, PDH), taskbar interop
├─ PulseBar.Providers.Codex   — codex app-server JSON-RPC client
├─ PulseBar.Providers.Claude  — statusline cache reader + OTLP receiver
└─ PulseBar.Storage     — SQLite (snapshots, token usage events)

PulseBar.Bridge (console) — invoked by Claude Code statusline; writes normalized cache atomically
```

## Key rules

- Providers never reference UI types.
- Provider failures must never take down system metrics collection.
- No PDH / process I/O / SQLite work on the UI thread.
- All external values carry `DataFreshness` and `DataScope`.

Details are filled in per implementation phase. See the development spec at the repo root.

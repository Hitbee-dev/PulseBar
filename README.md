# PulseBar

**English** | [한국어](README.ko.md)

A lightweight Windows taskbar widget that shows your **system resources** and your **AI coding tool usage** — Claude Code and Codex — at a glance, updated live, right next to the system tray.

![PulseBar overlay on the Windows taskbar](docs/images/overlay.png)

One glance answers three questions developers ask all day:

1. *Is my machine struggling?* — CPU · RAM · GPU · VRAM · disk · network, refreshed every second
2. *How much Claude quota do I have left?* — official 5-hour / weekly usage with reset times
3. *How much Codex quota do I have left?* — official rate-limit buckets, plan, credits

No admin rights. No Electron. No credential scraping. No cloud. Just a small WPF app (idle CPU < 0.5%).

---

## Reading the bar

```
CPU 11  RAM 62  GPU 39  VRAM 3.5G  D 0  ↓5K ↑14K
Claude 5h 69 · W 33  |  Codex W 12
```

| Token | Meaning |
|---|---|
| `CPU 11` | Total CPU usage, % |
| `RAM 62` | Physical memory in use, % |
| `GPU 39` | Busiest-engine GPU utilization of your primary adapter, % (matches Task Manager) |
| `VRAM 3.5G` | Dedicated video memory in use |
| `D 0` | Disk activity, % |
| `↓5K ↑14K` | Network down / up, bytes per second |
| `Claude 5h 69 · W 33` | Claude subscription usage: 69% of the 5-hour window, 33% of the weekly window |
| `Codex W 12` | Codex rate limit: 12% of the weekly window used |

Values that can't be collected show `—` instead of a fake `0`. Data older than 10 minutes is explicitly marked stale (`오래됨` / stale) — PulseBar never presents old numbers as current.

**Left-click** the bar for the detail popup. **Right-click** for the menu (refresh, settings, login, integrations, autostart, logs, exit). **Hover** for a tooltip with reset times and token totals.

## Detail popup

| System tab | AI usage tab |
|---|---|
| ![System tab with 60-second CPU graph](docs/images/detail-system.png) | ![AI usage tab with provider cards](docs/images/detail-ai.png) |

- **System** — 60-second CPU mini graph, plus RAM (used/total), GPU (with adapter name), VRAM (used/total), disk read/write throughput, network up/down.
- **AI usage** — one card per provider: every rate-limit window with its **used % and exact reset time**, account, plan, credits, token activity, data freshness ("last updated just now / N min ago"), and one-click actions: **Codex login**, **Open Claude Code**, **Connect Claude usage**, **Enable Fable token telemetry**.

## Features in depth

### System monitoring
- 1-second sampling of CPU (`GetSystemTimes` deltas), RAM (`GlobalMemoryStatusEx`), disk, network, GPU and VRAM.
- Uses **English PDH counter APIs** (`PdhAddEnglishCounterW`) — works on any Windows display language.
- GPU utilization is aggregated per adapter using the busiest-engine policy (like Task Manager) — you'll never see a nonsense 300%.
- VRAM totals and adapter names come from DXGI; multi-GPU systems auto-select the primary (largest dedicated memory) adapter.
- Collection runs entirely off the UI thread; a failing counter degrades to `—`, never crashes the app.

### Claude Code subscription usage
- Reads the **official statusline JSON** that Claude Code feeds to statusline commands — the same numbers Claude shows you, including `five_hour` / `seven_day` used-percentages and reset timestamps.
- A tiny bridge (`PulseBar.Bridge`) normalizes that JSON into a local cache file. It never touches Claude credentials and always exits 0 so it can never break your Claude session.
- **Already using a statusline HUD?** PulseBar never overwrites it. With one consent click it *wraps* your existing command: your HUD keeps rendering exactly as before, and PulseBar taps the same JSON on the way through. Your original command is preserved verbatim in a sidecar script and your `settings.json` is backed up first.
- When Claude Code isn't running, the last values are shown with a stale marker instead of silently going wrong.

### Fable 5 local token telemetry (opt-in)
- Uses Claude Code's **official OpenTelemetry export** to count actual tokens per model on *this PC*: input, output, cache-read, cache-creation — today and last 7 days.
- Strictly opt-in via the tray menu. The receiver binds **127.0.0.1 only**, requires a DPAPI-protected bearer secret, and stores **token counts and request metadata only — never prompts, responses, or transcripts**.
- Works for Claude Code running in WSL too: a helper process inside your distro forwards normalized events over a local queue.
- Deliberately displayed as "**Fable tokens · this PC's Claude Code**" — local telemetry is *not* your server-side quota, and PulseBar never pretends it is.

### Codex usage
- Talks to the **official `codex app-server` JSON-RPC interface** — no scraping, no cookie theft, no parsing `~/.codex`.
- Shows every rate-limit bucket the server reports, classified by **window duration** (300 min → 5h, 10080 min → weekly), never by hardcoded names — new server buckets appear automatically.
- Plan, credits, lifetime/daily token activity when the server provides them.
- **Browser login built in**: the official `account/login/start` flow opens your browser; PulseBar refreshes the moment login completes.
- Keeps one long-lived app-server process; reconnects with 1 → 2 → 5 min backoff. Detects account switches and discards stale snapshots.

### Windows-native and WSL, together
- Auto-detects `claude`/`codex` in Windows **and** in every WSL distribution on first run.
- **Version-aware detection**: when multiple installs coexist (snap, nvm, `~/.local/bin`…), PulseBar probes each `--version` and picks the newest — old binaries that break against current server APIs are skipped.
- All WSL processes are launched quote-free and PATH-corrected so node-shim CLIs (nvm installs) just work.

### Quality of life
- Docks left of the system tray; survives Explorer restarts (`TaskbarCreated`), DPI changes, and re-asserts topmost after fullscreen apps.
- Floating fallback mode (draggable, per-monitor position memory) when docking isn't possible (vertical/auto-hide taskbars).
- Korean / English UI, switchable live in settings.
- Settings export/import (imported files are validated), per-user autostart toggle (HKCU, no admin), rolling logs with automatic cleanup.

## Getting started

**Requirements**: Windows 10/11 x64. The portable build is self-contained — no .NET runtime install needed. No administrator rights, ever.

### 1. Get PulseBar

```powershell
# Portable: build the ZIP yourself (until binary releases are published)
git clone https://github.com/Hitbee-dev/PulseBar.git
cd PulseBar
powershell -ExecutionPolicy Bypass -File packaging/portable/build-portable.ps1
# → artifacts/PulseBar-portable-win-x64.zip (+ SHA-256)
```

Unzip anywhere and run `PulseBar.exe`. An Inno Setup per-user installer script is also available under `packaging/installer/`.

### 2. First run
1. System metrics appear on the taskbar immediately.
2. Claude/Codex CLIs are auto-detected (Windows + WSL) and profiles are saved to `%LOCALAPPDATA%\PulseBar\config.json`.
3. Tray right-click → **Codex login** — finish in the browser, numbers appear within seconds.
4. Tray right-click → **Connect Claude usage** — auto-installs the statusline bridge, or asks consent to wrap your existing HUD. Restart Claude Code once.
5. Optional: **Enable Fable token telemetry (OTel)** for local per-model token counts. Restart Claude Code once.

## One number is not the other

```
Claude weekly usage 33%      ← server-side account quota (official statusline value)
Fable last 7 days 12.4M tok  ← this PC's local telemetry (other devices/web not included)
```

PulseBar keeps these visually and semantically separate, always. Tools that blend them into one "percentage" are lying to you; PulseBar won't.

## Security & privacy

- No administrator rights; no Explorer/DLL injection; no undocumented taskbar hacks.
- Credentials are never read: Codex auth is owned by `codex app-server`, Claude auth by Claude Code.
- No browser cookies, no Windows Credential Manager access.
- Network listeners are loopback-only with bearer secrets (DPAPI-protected at rest).
- Prompts, responses, and transcripts are never collected, logged, or stored.
- `settings.json` modifications: timestamped backup first, add-only-when-absent, atomic writes, foreign settings never overwritten without explicit consent.
- Zero outbound telemetry from PulseBar itself.

Details: [docs/security.md](docs/security.md)

## Building from source

```powershell
dotnet restore
dotnet build PulseBar.sln -c Release
dotnet test PulseBar.sln -c Release --no-build   # 180 tests
```

| Project | Role |
|---|---|
| `src/PulseBar.App` | WPF app — DI host, overlay, detail popup, tray |
| `src/PulseBar.Core` | Models, provider contract, config, localization, formatters |
| `src/PulseBar.Windows` | Metrics (PDH/DXGI), taskbar interop, CLI detection, DPAPI |
| `src/PulseBar.Providers.Codex` | codex app-server JSON-RPC client + login |
| `src/PulseBar.Providers.Claude` | Statusline parser/installer, OTel parser, provider |
| `src/PulseBar.Bridge` | Statusline bridge + WSL OTel helper executable |
| `src/PulseBar.Storage` | SQLite token-event store (idempotent, 30-day retention) |

Stack: .NET 8 LTS · WPF · SQLite · xUnit. Deliberately **not** used: Electron, web views, background Node/Python daemons, Docker, admin drivers.

## Troubleshooting

See [docs/troubleshooting.md](docs/troubleshooting.md) — covers the statusline merge command for existing HUDs, stale-data meanings, and counter availability. Logs live in `%LOCALAPPDATA%\PulseBar\logs\`.

## Known limitations

- Docks on horizontal taskbars; vertical/auto-hide taskbars use the floating mode.
- Primary-monitor taskbar only (secondary-monitor selection is planned).
- Fable telemetry counts only this machine's Claude Code traffic — by design.
- GPU temperature is intentionally out of scope (no admin-driver hacks).

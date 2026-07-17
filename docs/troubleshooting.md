# Troubleshooting

## Logs

`%LOCALAPPDATA%\PulseBar\logs\` — rolling logs, 7 days / 50 MB max.

## Common issues

| Symptom | Check |
|---|---|
| Overlay not visible | Explorer restarted? PulseBar repositions on `TaskbarCreated`; try right-click → Refresh. |
| Codex shows "executable missing" | `where.exe codex` (Windows) or `wsl.exe -d <distro> -- sh -lc "command -v codex"`. |
| Claude usage stuck at "stale" | Claude Code must be running with the PulseBar statusline bridge installed. |
| Values look wrong | Compare with Task Manager; file an issue with logs. |

## Claude usage shows "연동 필요 / Setup required"

PulseBar reads Claude quota from the official statusline JSON via `PulseBar.Bridge`.

- **No statusLine configured**: PulseBar registers the bridge automatically on first run
  (your previous `settings.json` is backed up to `%LOCALAPPDATA%\PulseBar\backups`).
- **You already have a statusLine** (e.g. claude-hud): PulseBar never overwrites it.
  Wrap your existing command manually in `~/.claude/settings.json`:

```json
{
  "statusLine": {
    "type": "command",
    "command": "\"/mnt/c/.../PulseBar.Bridge.exe\" claude-statusline --output 'C:\\Users\\<you>\\AppData\\Local\\PulseBar\\bridge\\claude-status.json' --passthrough \"<your original statusline command>\""
  }
}
```

The bridge forwards the same statusline JSON to your original command and echoes its
output back to Claude Code, so your existing HUD keeps working. The exact command for
your machine is printed in the PulseBar log when an existing statusLine is detected.

More entries are added as phases land.

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

More entries are added as phases land.

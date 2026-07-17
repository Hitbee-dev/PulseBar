# Security

- No admin rights required, ever.
- Codex OAuth is owned by `codex app-server`; PulseBar never parses `~/.codex` credentials.
- Claude OAuth tokens are never read; only official statusline JSON and OTel events are consumed.
- No browser cookie access, no Windows Credential Manager scraping.
- Local bridge secrets are protected with DPAPI (CurrentUser).
- OTLP receiver binds to 127.0.0.1 only; bearer secret required.
- No prompts/responses/transcripts are ever logged or stored — token counts and metadata only.
- Claude settings.json is backed up (timestamped) before any modification; an existing `statusLine` is never overwritten.
- No outbound telemetry from PulseBar itself.

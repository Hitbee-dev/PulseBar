# Provider Contract

All AI usage providers implement `IUsageProvider` (see `PulseBar.Core/Interfaces`).

- `ProbeAsync` — detect executable/environment availability.
- `StartAsync` — start long-lived connections (e.g. codex app-server process).
- `GetSnapshotAsync` / `RefreshAsync` — pull the latest `UsageSnapshot`.
- `SnapshotUpdated` / `StateChanged` — push updates to the app.

Every value in a snapshot carries:

- `DataFreshness`: Live / Fresh / Stale / Unavailable / AuthenticationRequired / Error
- `DataScope`: ServerAccount / LocalMachine / LocalSession / Estimated

Rules:

- Do not hardcode rate-limit bucket names; classify by `windowDurationMins`.
- Null-safe parsing everywhere; a missing bucket is not an error.
- Account switch ⇒ immediately mark previous snapshot stale.

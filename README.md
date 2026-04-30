# NivoTask

Self-hosted Kanban board with hierarchical time tracking. Built for solo developers who bill by the hour and want one tool that does both — boards and timers — without a SaaS.

Time logged on a sub-task instantly rolls up to the parent head task. Open the board, see exactly how many hours each feature has consumed.

## Features

- **Kanban boards** — drag-and-drop cards across columns, multiple boards, archive when done.
- **Hierarchical time tracking** — head task → sub-tasks → time entries. Sub-task time aggregates to the parent, board total aggregates across tasks.
- **One active timer at a time** — prevents accidental double-billing.
- **Quick-start timer** on the dashboard — pick board, optional task, type notes, hit play.
- **CSV export** — one click for invoicing.
- **Daily / weekly time goals** with progress bars and a 12-week activity heatmap.
- **Cmd/Ctrl+K palette** — jump to any task across boards.
- **Idle detection** — prompts to keep / trim / discard if you walk away with a timer running.
- **Inline edit** of time entries (duration + notes).
- **Filter chips** — priority, due date, label, hide-done.
- **Bulk move** — Ctrl+click cards, move them all to a column at once.
- **WIP limits** per column with a red warning when exceeded.
- **Templates** — duplicate a task or whole board (columns, sub-tasks, labels included).
- **Per-task time chart** + week-over-week delta on the dashboard.
- **In-app updater** — Check for updates → Update now from the About dialog. App downloads the right binary for your OS, swaps files, restarts itself.
- **PWA** — installable as a desktop app on Windows / macOS.

## Tech stack

- **.NET 10** — Web API + Blazor WebAssembly (standalone PWA)
- **MudBlazor 9** — Material UI components, drag-and-drop included
- **MySQL 8.x or 9.x** — via Pomelo EF Core 9 provider (EF Core 10 + Pomelo not yet released)
- **ASP.NET Identity** — cookie auth, single-user app
- **MySqlConnector** (transitive)

The API hosts the WASM static files in production, so you ship one process.

## Install (recommended: pre-built binary)

1. Grab the release for your platform from [Releases](https://github.com/nivobi/NivoTask/releases/latest):

   | Platform | Asset |
   |----------|-------|
   | Windows 64-bit | `nivotask-vX.Y.Z-win-x64.zip` |
   | Windows 32-bit | `nivotask-vX.Y.Z-win-x86.zip` |
   | Linux x64 | `nivotask-vX.Y.Z-linux-x64.tar.gz` |
   | macOS Intel | `nivotask-vX.Y.Z-osx-x64.tar.gz` |
   | macOS Apple Silicon | `nivotask-vX.Y.Z-osx-arm64.tar.gz` |

2. Unzip / untar to a writable directory (not Program Files — the in-app updater overwrites files in place).

3. Make sure MySQL is running and you have a database + user. Quick setup:
   ```sql
   CREATE DATABASE nivotask CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
   CREATE USER 'nivotask'@'localhost' IDENTIFIED BY 'change-me';
   GRANT ALL ON nivotask.* TO 'nivotask'@'localhost';
   ```

4. Run the binary:
   - Windows: `NivoTask.Api.exe`
   - Linux/macOS: `./NivoTask.Api`

5. Open `http://localhost:5243` (default port). The setup wizard runs on first launch — point it at MySQL, set an admin email + password. EF migrations apply automatically.

6. Done. Boards, time tracker, dashboard.

## Updating

- App periodically checks GitHub Releases.
- Top-right Info icon shows a dot when an update is available.
- Click → About dialog → release notes → **Update now**. App downloads the matching binary, restarts, and you reload the page.

Manual updates: stop the process, replace the install dir contents with the new release, restart.

## Build from source

Requires .NET 10 SDK + EF Core CLI tool.

```bash
git clone https://github.com/nivobi/NivoTask.git
cd NivoTask
dotnet restore
dotnet build
dotnet test src/NivoTask.Api.Tests
```

Run locally:
```bash
dotnet run --project src/NivoTask.Api
```

Apply migrations to a configured DB:
```bash
dotnet ef database update --project src/NivoTask.Api/NivoTask.Api.csproj
```

Self-contained publish for your platform:
```bash
dotnet publish src/NivoTask.Api -c Release -r win-x64 --self-contained true
```

## Configuration

Two layered files:
- `appsettings.json` — defaults shipped with the binary (logging, etc.).
- `setup.json` — written by the setup wizard, contains the MySQL connection string and `SetupComplete: true`. Treat it as a secret; gitignored by default.

Environment variables override either file (standard ASP.NET Core configuration).

## Project layout

```
src/
  NivoTask.Api/         ASP.NET Core Web API, Identity, EF migrations, hosts WASM
  NivoTask.Client/      Blazor WebAssembly PWA frontend
  NivoTask.Shared/      DTOs shared by both
  NivoTask.Api.Tests/   xUnit + WebApplicationFactory + SQLite in-memory
.github/workflows/
  ci.yml                build + test on every push/PR
  release.yml           publish self-contained binaries on tag push
```

## Status

Single-user, self-hosted, billable-hours focused. Not built for teams, not multi-tenant, no SSO, no email notifications. That is by design.

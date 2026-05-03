# Monitor Command Reference

All commands assume PowerShell is open at the copied and renamed workspace folder:

```text
C:\VSCodeProjects\MonitorProject
```

The monitor is a solo local proposal gate. It is designed for one human reviewing Codex-generated changes on one machine before those changes become normal source history in Git.

Build:

```powershell
dotnet build ".\Monitor\AIWorkflowMonitor.csproj"
```

Run help:

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- --help
```

Run self-check without creating Working state:

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- --self-check
```

Refresh and compare:

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- "C:\VSCodeProjects\MonitorProject\MyWatchedProject\Path\To\File.cs"
```

Refresh only:

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- "C:\VSCodeProjects\MonitorProject\MyWatchedProject\Path\To\File.cs" --refresh-only
```

Compare only:

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- "C:\VSCodeProjects\MonitorProject\MyWatchedProject\Path\To\File.cs" --compare-only
```

Compare with a ledger summary:

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- "C:\VSCodeProjects\MonitorProject\MyWatchedProject\Path\To\File.cs" --compare-only --ledger-summary "Changed save validation routing and kept Razor comments minimal."
```

Use a one-time watched root override:

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- "C:\OtherProject\File.cs" --observed-root "C:\OtherProject"
```

Open telemetry for this run:

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- "C:\VSCodeProjects\MonitorProject\MyWatchedProject\Path\To\File.cs" --telemetry-window
```

Telemetry is independent of the compare tool. A telemetry window can open even when no WinMerge/Beyond Compare/VS Code window opens, especially when there is no diff, when a run only refreshes or validates, or when telemetry auto-open is enabled in `appsettings.json`.

The AI-readable JSON logs are intentionally capped. `_runs.json` keeps the most recent 500 run-detail entries. `_telemetry.json` keeps the most recent 50 telemetry runs with up to 300 retained lines per run. The complete long-term record is Git plus the monitor's per-file ledgers/snapshots/archives, not unbounded JSON growth.

Each run logs the effective `app_root`, `observed_root`, `working_dir`, and `history_dir` near startup so humans and AI sessions can quickly confirm that generated state is going to the intended monitor folder.

## Exit Codes

Exit codes are for scripts and other AI/tool sessions that need a compact machine-readable result. They do not replace the console output, telemetry window, or JSON run details.

```text
0   Success.
1   Unexpected monitor failure.
10  Usage or configuration error.
20  Source path or observed-root path error.
30  Working refresh state is stale.
40  Roslyn validation blocked compare.
50  Diff tool launch failed.
60  No differences found; compare window skipped.
70  Another monitor run is already active.
```

Code `60` is a clean stop, not a broken run. It means the monitor had nothing to hand to the diff tool.

## Roslyn Working Overlay

Before compare, Roslyn validates the selected Working `.cs` file with a sparse overlay:

- the selected file uses its current `Monitor\Working` content
- other current sibling Working `.cs` files override their matching observed source files
- new Working-only `.cs` files are included so new helper types can be validated before merge
- compare windows are still launched one at a time

This supports the normal multi-file Codex cycle: edit every needed Working file first, then review the generated diffs serially.

Roslyn validation is a fast preflight, not a full MSBuild/project-graph build. It can catch obvious proposed-code issues before compare, but the watched project's real build/debug/QA cycle remains the final truth after merge.

The monitor also warns, without blocking, when the selected Working file appears to add many comment lines. Routine edit history should go to `--ledger-summary` and component maps, not source comments.

## New File Creation

The monitor is file-copy based, so it is strongest when the watched source file already exists. For a brand-new feature that creates new files, the initial creation pass may be done directly in the watched project when the user asks for "no monitor" or an equivalent fast implementation pass.

For new C# files, add `AIFileContext` and `FileVersion("1.0")`. After the files exist, use the normal monitor flow for follow-up edits, review diffs, ledger summaries, and documentation cleanup.

## Tiny Sample Watched Project

A disposable console project is included at:

```text
Monitor\Docs\Samples\TinyConsoleWatchedProject
```

Refresh it without changing `appsettings.json`:

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- ".\Monitor\Docs\Samples\TinyConsoleWatchedProject\Program.cs" --observed-root ".\Monitor\Docs\Samples\TinyConsoleWatchedProject" --refresh-only --no-prune
```

To smoke-test overlay validation, refresh both sample `.cs` files, edit both matching files under `Monitor\Working\<observed-root-key>\TinyConsoleWatchedProject`, then compare one file. The observed-root key includes the watched folder name plus a short path hash so same-named watched roots do not share state. Roslyn should validate using the sibling Working file as an overlay while the diff review stays serial.

## Diff Inputs

The monitor creates a timestamped proposed snapshot before launching the diff tool. Diff review compares that immutable proposed snapshot against the existing source file. This keeps the visible diff aligned with the per-file ledger and history snapshot even if `Monitor\Working` changes later.

WinMerge orientation remains:

```text
left  = new/proposed snapshot
right = existing source
```

The watched root should be a sibling project beside `Monitor`. The monitor rejects watched roots that point at the Monitor folder or generated monitor state. The bundled tiny sample under `Monitor\Docs\Samples` is the only intentional in-Monitor watched root exception.


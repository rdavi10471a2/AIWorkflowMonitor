# MonitorProject

This folder is a reusable AI workflow monitor starter. Copy this whole folder, rename the copy to your project workspace name, then initialize it against one watched project.

This is not a passive monitor. It is an active local proposal gate that controls how AI-generated file changes are staged, preflighted, reviewed, and accepted.

This is intentionally a solo local tool. It is a personal proposal gate for Codex-generated work on one machine: refresh source into `Working`, let Codex propose edits, preflight the proposal, review one diff at a time, then build and QA the real watched project. It is not meant to replace Git, CI, pull requests, or a team review process. Those belong downstream after the local proposal cycle produces tested source changes.

The standard workflow is:

1. Copy this folder.
2. Rename the copied outer folder manually.
3. Create or clone the watched project as a sibling of `Monitor`.
4. Open the renamed outer folder in VS Code.
5. Ask Codex to read the markdown files and initialize the monitor for the watched project.

Recommended layout:

```text
C:\VSCodeProjects\MonitorProject\
  Monitor\
    AIWorkflowMonitor.csproj
    appsettings.template.json
    AGENTS.md
  MyWatchedProject\
    YourProject.sln
    ...
```

`Monitor` is the monitoring tool. The watched project should be a sibling folder. Generated monitor state such as `Working`, `History`, archives, and telemetry belongs under `Monitor`, never inside the watched project.

Before copying this base folder, `Monitor\bin`, `Monitor\obj`, `Monitor\Working`, `Monitor\History`, `Monitor\Archive`, and telemetry output should be absent or ignored. They are generated state, not part of the reusable starter.

For the monitor's own implementation layout, see `Monitor\Docs\MonitorImplementationMap.md`. The `AIWorkflowRunner.*.cs` partial-file naming is intentional and keeps concerns findable without adding DI/container ceremony.

Required watched-project convention:

- [QUICK_START.md](QUICK_START.md) is the shortest first-run path for a copied monitor base.
- [START_HERE_WORKFLOW_SUMMARY.md](START_HERE_WORKFLOW_SUMMARY.md) is the one-page operating model for this workspace.
- [AIWorkflowRunner.ExecutionFlow.md](AIWorkflowRunner.ExecutionFlow.md) shows the runtime flow through the partial `AIWorkflowRunner.*.cs` implementation files.
- [SELF_CHECK.md](SELF_CHECK.md) has quick commands for verifying root guardrails, snapshot diff semantics, and retention constants.
- [AI_REVIEW_BRIEF_SOLO_MONITOR.md](AI_REVIEW_BRIEF_SOLO_MONITOR.md) is the compact brief to feed another AI session when asking it to review the monitor concept or current branch.
- [AI_ATTRIBUTES_EXAMPLE.md](AI_ATTRIBUTES_EXAMPLE.md) explains the lightweight AI attributes that initialization installs into the watched project. They are for durable file context, not routine edit logs.
- [DOCUMENTATION_WORKFLOW.md](DOCUMENTATION_WORKFLOW.md) explains how to document large AI-generated code without flooding source files with process comments.
- `Monitor\Docs\Samples\FeatureMapTemplate.md` is the starter shape for nearby feature/data-flow maps with human-owned and AI-maintained sections.

Recommended after a feature is implemented and tested: ask Codex for a documentation-only pass. It should add `AIFileContext` to new C# files, update a nearby feature/component map when the feature spans multiple files or a namespace/folder area, include basic feature/data flow notes, and avoid inline comments unless they document a real trap or invariant.

Documentation is generated after the feature stabilizes, not during active churn.

## Quick Start

1. Copy this folder.
2. Rename the copied folder, for example from `Monitor Base` to `MonitorProject`.
3. Create, put, or clone the project to watch as a sibling of `Monitor`.
4. Open the renamed outer folder in VS Code.
5. Ask Codex to initialize, or open the renamed folder in PowerShell and run initialization yourself:

```powershell
.\Initialize-MonitorProject.ps1 -WatchedRoot "C:\VSCodeProjects\MonitorProject\MyWatchedProject" -SourceFile "C:\VSCodeProjects\MonitorProject\MyWatchedProject\Path\To\File.cs"
```

The script creates or updates local `Monitor\appsettings.json` from tracked `Monitor\appsettings.template.json`, builds `Monitor\AIWorkflowMonitor.csproj`, and runs the first `--refresh-only` pass. The local `appsettings.json` is intentionally ignored by Git so each copied monitor can keep its own watched root without fighting future pulls.

If you want the script to pick the first `.cs` file it can find:

```powershell
.\Initialize-MonitorProject.ps1 -WatchedRoot "C:\VSCodeProjects\MonitorProject\MyWatchedProject"
```

For an existing watched project that already has older AI helper files or old AI comments from a prior monitor workflow, initialization preserves them. If the current workflow attributes are missing, it adds a supplemental `AI\AIWorkflowCurrentAttributes.cs` file without replacing the old helper or cleaning old comments.

To avoid any AI attribute file change during initialization:

```powershell
.\Initialize-MonitorProject.ps1 -WatchedRoot "C:\VSCodeProjects\MonitorProject\ExistingWatchedProject" -SkipAIAttributes
```

If PowerShell blocks the script, run this from the renamed outer folder:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\Initialize-MonitorProject.ps1 -WatchedRoot "C:\VSCodeProjects\MonitorProject\MyWatchedProject"
```

## Current Workflow Summary

1. Refresh the source file into `Monitor\Working`.
2. Codex edits only the working copy.
3. For C# files, preserve `AIFileContext`; add it to new C# files. Each physical C# file should have its own `FileVersion`, including partial-class files, and meaningful edits should bump that file's version.
4. Codex runs compare with `--ledger-summary` so the running comment log goes to `Monitor\Working\History\Ledgers`, not into source comments.
5. Before compare, Roslyn validates the selected Working file against the observed source plus any current sibling Working `.cs` overrides. This lets a multi-file proposed edit validate as a set while the human still reviews one diff at a time.
6. The monitor creates a timestamped snapshot, appends the per-file ledger, and launches the configured diff tool.
7. The human submits the proposal by merging the diff into source, then immediately builds/runs/QA tests the real app.
8. Old loose snapshots are zipped after 7 days; per-file ledger entries are trimmed after about 2 days.

For multi-file changes, Codex may edit all required Working files first, then treat compares as a queue. It should open one GUI diff window, report the file and paths, then wait for you to say to continue before opening the next one. WinMerge orientation is new/proposed snapshot on the left and existing source file on the right.

Before the diff tool opens, the monitor creates a timestamped proposed snapshot. The diff tool reviews that snapshot against the existing source file, so the visible diff matches the ledger/history record even if `Monitor\Working` changes later.

For brand-new features that create new files, it is acceptable to tell Codex to implement directly in the watched project without the monitor for the initial creation pass. The monitor is strongest when a source file already exists and can be refreshed into `Working`. After the new files exist and the feature starts to stabilize, bring the monitor back for refinement, review, ledger summaries, and documentation cleanup.

The old source-map implementation has been removed from the active monitor and archived under `Monitor\Docs\ArchivedSourceMap`. Roslyn overlay validation is the active compare-gate safety mechanism.

Roslyn overlay validation is an early warning system, not a complete replacement for the watched project's real build. After merging a proposal, build/run/QA the actual project.

Telemetry is separate from the diff tool. If telemetry auto-open is enabled, the monitor may open a live log window even when WinMerge or Beyond Compare does not open because there was no file diff to review. Treat that window as the run transcript, not as a compare window.

The JSON run and telemetry files are capped so they stay useful for AI inspection. `_runs.json` keeps the most recent 500 entries. `_telemetry.json` keeps the most recent 50 runs with up to 300 retained lines per run. They are short-term context, not durable project history.

## Recovery Model

The monitor keeps enough proposal history to recover or reconstruct recent AI work before it becomes meaningful Git history.

- `Monitor\Working\...`: current proposed working copy.
- `Monitor\Working\History\...`: timestamped compare snapshots.
- `Monitor\Working\History\Ledgers\...`: short running log that points to the exact snapshot for each compare.
- `Monitor\Working\Archive\history_yyyyMMdd.zip`: zipped older snapshots.

To roll back a recent proposed change, ask Codex to inspect the source file, the working copy, and the matching ledger/snapshot. If the loose snapshot has been zipped, the ledger entry contains `ArchiveZip` and `ArchiveEntry` so the exact proposal can still be found.

This is proposal recovery, not a replacement for Git. Git should hold meaningful source checkpoints; the monitor holds short-term AI proposal history.

## Git Layer

Put Git on the watched project, not on `Monitor\Working`. The monitor is for AI proposal safety before source history; Git is for meaningful source history after work has stabilized.

Recommended feature workflow:

1. Create a working feature branch in the watched project.
2. Use monitor cycles for small proposal, review, merge, and QA loops.
3. Commit stable chunks when they are meaningful, not every generated proposal.
4. When the feature is complete and tested, run a documentation-only pass.
5. Commit the completed feature with updated `AIFileContext`, `FileVersion`, component maps, and any high-value comments.
6. Merge the feature branch when the bulk feature is ready.

The optional post-build Git checkpoint can still be used, but it is not the main safety mechanism. Prefer the monitor for proposal safety and Git for durable project history.

## Prompt For Codex

After creating the sibling watched project, use a prompt like this in VS Code:

```text
Read the markdown files in this workspace and initialize the monitor for this watched project:

C:\VSCodeProjects\MonitorProject\MyWatchedProject

Use a representative C# source file from that project for the first refresh-only run. Ensure the standard AI attributes are installed in the watched project. Keep generated monitor state under Monitor, not inside the watched project.

When editing watched source files later, preserve existing `AIFileContext`, `FileVersion`, and meaningful `AIChange` attributes. Put routine change summaries in monitor-owned history/ledger notes or nearby component maps instead of stacking comments in source files.
```

If you already know the first source file, include it:

```text
Watched root:
C:\VSCodeProjects\MonitorProject\MyWatchedProject

First source file:
C:\VSCodeProjects\MonitorProject\MyWatchedProject\Form1.cs
```

## Visual Studio 2022 Project Creation

When creating the watched project in Visual Studio 2022, set `Location` to the renamed outer workspace folder and set `Project name` to the watched app name.

For a simple one-project app, prefer `Place solution and project in the same directory` so the result is:

```text
C:\VSCodeProjects\MonitorProject\
  Monitor\
  MyWatchedProject\
    MyWatchedProject.sln
    MyWatchedProject.csproj
```

Do not create the watched project inside `Monitor`.

The monitor rejects watched roots that point at `Monitor` or generated monitor state. The only intended in-Monitor watched root is the bundled tiny sample under `Monitor\Docs\Samples`.

## Useful Commands

Show help:

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- --help
```

Run self-check:

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- --self-check
```

Refresh only:

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- "C:\VSCodeProjects\MonitorProject\MyWatchedProject\Path\To\File.cs" --refresh-only
```

Compare only:

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- "C:\VSCodeProjects\MonitorProject\MyWatchedProject\Path\To\File.cs" --compare-only
```

Compare only with a compact monitor-owned ledger summary:

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- "C:\VSCodeProjects\MonitorProject\MyWatchedProject\Path\To\File.cs" --compare-only --ledger-summary "Changed save validation routing and kept source comments minimal."
```

Override the watched root without editing local `appsettings.json`:

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- "C:\Path\To\Project\File.cs" --observed-root "C:\Path\To\Project"
```

Smoke-test the monitor with the tiny sample watched project:

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- ".\Monitor\Docs\Samples\TinyConsoleWatchedProject\Program.cs" --observed-root ".\Monitor\Docs\Samples\TinyConsoleWatchedProject" --refresh-only --no-prune
```

## Diff Tool

Local `Monitor\appsettings.json` supports these `DiffTool` values:

- `WinMerge`
- `BeyondCompare`

WinMerge is the default and recommended diff tool. If your tool is not installed in a default location, pass its executable path as the second positional argument.


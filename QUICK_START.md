# Quick Start

Use this when you want the monitor running before reading the full operating model.

## 1. Copy The Base

Copy the whole repository folder to a new workspace folder and rename the outer folder.

Example:

```text
C:\VSCodeProjects\MonitorProject
```

Expected shape:

```text
C:\VSCodeProjects\MonitorProject\
  Monitor\
  MyWatchedProject\
```

`Monitor` is the tool. `MyWatchedProject` is the real project Codex will help edit.

## 2. Create Or Clone The Watched Project

Put the watched project next to `Monitor`, not inside it.

```text
C:\VSCodeProjects\MonitorProject\
  Monitor\
  MyWatchedProject\
    MyWatchedProject.sln
```

Generated monitor state must stay under `Monitor\Working`.

## 3. Open The Workspace

Open the renamed outer folder in VS Code:

```text
C:\VSCodeProjects\MonitorProject
```

Tell Codex to read the markdown files before editing.

## 4. Initialize

From the outer workspace folder:

```powershell
.\Initialize-MonitorProject.ps1 -WatchedRoot "C:\VSCodeProjects\MonitorProject\MyWatchedProject"
```

The initializer:

- creates local `Monitor\appsettings.json` from `Monitor\appsettings.template.json`
- sets the watched root
- adds current AI workflow attributes when missing
- builds the monitor
- runs the first refresh pass

Local `Monitor\appsettings.json` is ignored by Git so every copied monitor can point at its own watched project.

## 5. Run The Self Check

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- --self-check
```

Use this to confirm root guardrails, diff tool resolution, exit codes, and retention constants.

## 6. Refresh A File

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- "C:\VSCodeProjects\MonitorProject\MyWatchedProject\Path\To\File.cs" --refresh-only
```

This copies the watched source file into `Monitor\Working`.

## 7. Let Codex Edit Working

Codex should edit the `Monitor\Working` copy, not the real source file, unless you intentionally ask for a fast new-file creation pass.

After Codex edits the Working file, compare it:

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- "C:\VSCodeProjects\MonitorProject\MyWatchedProject\Path\To\File.cs" --compare-only --ledger-summary "Describe the accepted proposal here."
```

WinMerge shows the proposed snapshot on the left and the current source file on the right.

## 8. Accept, Build, Iterate

If the proposal is good:

1. Merge the diff into the real source file.
2. Build and run the watched project.
3. QA/debug the real behavior.
4. Ask Codex for the next small proposal.

Git belongs on the watched project. Commit stable, tested source there after the local proposal cycle.

## More Context

- Read [START_HERE_WORKFLOW_SUMMARY.md](START_HERE_WORKFLOW_SUMMARY.md) for the operating model.
- Read [MONITOR_COMMAND_REFERENCE.md](MONITOR_COMMAND_REFERENCE.md) for command details.
- Read [AIWorkflowRunner.ExecutionFlow.md](AIWorkflowRunner.ExecutionFlow.md) to understand the runtime path through the monitor code.

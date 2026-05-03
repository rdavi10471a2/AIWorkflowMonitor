# Initialize With A New Watched Project

Use this guide after copying the base folder and manually renaming it, for example to `MonitorProject`.

The initialized layout should be:

```text
C:\VSCodeProjects\MonitorProject\
  Monitor\
  MyWatchedProject\
```

## What Codex Needs

Tell Codex:

- the absolute path to the renamed workspace folder
- the absolute path to the watched project root
- one source file to use for the first refresh, or permission to auto-pick one
- which diff tool you want, if not `WinMerge`
- confirmation that the lightweight standard AI attributes should be installed into the watched project
- whether this is a legacy watched project that already has older AI helper files/comments and should preserve them without installing the standard sample

If the watched project was just created in Visual Studio 2022, make sure it is a sibling of `Monitor` before initializing.

Example values:

```powershell
$WorkspaceRoot = "C:\VSCodeProjects\MonitorProject"
$WatchedRoot = "C:\VSCodeProjects\MonitorProject\MyWinFormsApp"
$SourceFile = "C:\VSCodeProjects\MonitorProject\MyWinFormsApp\Form1.cs"
```

## Preferred Setup Command

Open the renamed workspace folder in PowerShell and run:

```powershell
.\Initialize-MonitorProject.ps1 -WatchedRoot "C:\VSCodeProjects\MonitorProject\MyWinFormsApp" -SourceFile "C:\VSCodeProjects\MonitorProject\MyWinFormsApp\Form1.cs" -DiffTool VSCode
```

For an existing project that already has older AI helpers or a lot of legacy AI comments, do not burn tokens cleaning that up during initialization. By default, initialization preserves the existing helper file and adds only a small supplemental `AI\AIWorkflowCurrentAttributes.cs` file if the current workflow attributes are missing.

If you truly want to avoid any AI attribute file changes during initialization, use:

```powershell
.\Initialize-MonitorProject.ps1 -WatchedRoot "C:\VSCodeProjects\MonitorProject\ExistingApp" -SourceFile "C:\VSCodeProjects\MonitorProject\ExistingApp\Path\To\File.cs" -SkipAIAttributes
```

If PowerShell blocks local scripts, run this first in the same PowerShell window:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```

## Prompt To Give Codex

```text
Read the markdown files in this workspace and initialize the monitor workspace at C:\VSCodeProjects\MonitorProject for this watched project:
C:\VSCodeProjects\MonitorProject\MyWatchedProject

Create or update local Monitor\appsettings.json from Monitor\appsettings.template.json, then set ObservedRoot to that watched project root. Keep the monitor and watched project as sibling folders. Build the monitor. Then run a refresh-only monitor pass for this source file:
C:\VSCodeProjects\MonitorProject\MyWatchedProject\Path\To\File.cs

Do not move generated monitor state into the watched project.
```

Always include this AI attribute instruction:

```text
Install the standard AI attributes into the watched project if they are missing. For every source file Codex edits later, preserve existing AIFileContext, FileVersion, and meaningful AIChange attributes. For meaningful C# edits, bump the edited file's FileVersion and include that version in --ledger-summary. Do not add routine AI/process comments or stacked source attributes. Put routine change summaries in monitor-owned history/ledger notes or nearby component maps.
```

For a legacy project that already has an older AI helper file or old AI comments, replace that instruction with:

```text
This watched project already has legacy AI helper files/comments from a prior monitor workflow. Do not replace, rename, clean up, or mass-edit them during initialization. Preserve legacy comments. If the current AI workflow attributes are missing, add only the supplemental current attribute definitions needed for future FileVersion/AIFileContext use. Initialize local Monitor\appsettings.json from the tracked template, build the monitor, and run the first refresh-only pass.
```

## Codex Checklist

Codex should do this:

1. Confirm the monitor folder exists.
2. Confirm the watched project folder exists.
3. Find a suitable `.cs` file if you did not provide one.
4. Install `AI\AIAttributes.cs` into the watched project if it is missing, using `Monitor\Docs\Samples\AIAttributes.cs` as the template. If a legacy `AI\AIAttributes.cs` already exists, preserve it and add only missing current attributes in `AI\AIWorkflowCurrentAttributes.cs`.
5. Create or patch local `Monitor\appsettings.json`.
6. Run `dotnet build`.
7. Run `dotnet run --project ... -- <source-file> --refresh-only`.
8. Report the exact command used and whether the refresh succeeded.

Codex should not initialize if the watched project is nested under `Monitor`. Move or recreate the watched project as a sibling first.

## Manual Equivalent

Edit local `appsettings.json`:

```json
{
  "WorkflowSettings": {
    "ObservedRoot": "C:\\VSCodeProjects\\MonitorProject\\MyWatchedProject",
    "DiffTool": "WinMerge",
    "ContractEnforcementMode": "StrictExternal",
    "AllowLocalTypeEvolution": true,
    "TelemetryEnabled": true,
    "TelemetryAutoOpenWindow": true
  }
}
```

`Monitor\appsettings.template.json` is tracked by Git. `Monitor\appsettings.json` is local generated configuration and is ignored so pulls from the base repo do not overwrite each monitor copy's watched root.

Then build:

```powershell
dotnet build ".\Monitor\AIWorkflowMonitor.csproj"
```

Then refresh:

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- "C:\VSCodeProjects\MonitorProject\MyWatchedProject\Path\To\File.cs" --refresh-only
```


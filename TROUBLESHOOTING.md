# Troubleshooting

## ObservedRoot Not Found

If the monitor says `ObservedRoot` does not exist, edit `appsettings.json` and use an absolute path to the watched project root.

Good:

```json
"ObservedRoot": "C:\\VSCodeProjects\\MonitorProject\\MyWatchedProject"
```

Avoid relative paths unless you know exactly where the monitor process starts.

## Source File Is Outside ObservedRoot

The source file passed to the monitor must be inside the configured watched project root.

Example:

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- "C:\VSCodeProjects\MonitorProject\MyWatchedProject\Form1.cs"
```

## Diff Tool Not Found

Set `WorkflowSettings:DiffTool` to one of:

- `WinMerge`
- `BeyondCompare`
- `VSCode`

Or pass the diff executable path:

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- "C:\VSCodeProjects\MonitorProject\MyWatchedProject\Form1.cs" "C:\Program Files\WinMerge\WinMergeU.exe"
```

## Working Copy Looks Stale

Run a refresh:

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- "C:\VSCodeProjects\MonitorProject\MyWatchedProject\Form1.cs" --refresh-only
```

## PowerShell Blocks The Init Script

If PowerShell refuses to run `Initialize-MonitorProject.ps1`, run this in the same PowerShell window from the renamed outer workspace folder:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```

Then rerun the initialization command.

## Copied Folder Contains Build Output

If the copied base includes generated monitor output, remove it before treating the folder as a clean starter:

```powershell
Remove-Item .\Monitor\bin, .\Monitor\obj -Recurse -Force
```

Also remove `Monitor\Working`, `Monitor\History`, `Monitor\Archive`, or monitor telemetry folders if they exist and came from a previous watched project.

## Source Files Are Getting Too Noisy

Do not use routine AI/process comments in source files as a change log. Keep source comments for durable code facts and put routine change summaries in monitor-owned history/ledger notes or nearby component maps.

See `DOCUMENTATION_WORKFLOW.md`.

## History Retention

The monitor currently zips matching source-file snapshots older than 7 days into `Monitor\Working\Archive\history_yyyyMMdd.zip`, updates matching ledger entries with `ArchiveZip` and `ArchiveEntry`, then removes the loose snapshot file from `Monitor\Working\History`.

Per-file ledgers under `Monitor\Working\History\Ledgers` are short-term working memory and are trimmed to roughly 2 days during prune. The monitor does not prune `_runs.json`, telemetry, errors, or zip archives.

## Recover Or Reconstruct A Proposal

Use the ledger as the index:

```text
Monitor\Working\History\Ledgers\
```

Find the entry for the file and time you care about. If `ArchiveZip` is `not archived`, use the `Snapshot` path. If `ArchiveZip` and `ArchiveEntry` are populated, open that zip and extract the entry.

Then ask Codex to compare the source file with that proposal snapshot or reconstruct the intended change.

## Build Fails On Target Framework

The monitor project currently targets `net10.0`. Install the matching .NET SDK or change `TargetFramework` in `AIWorkflowMonitor.csproj` to a locally installed SDK target if you intentionally need that.


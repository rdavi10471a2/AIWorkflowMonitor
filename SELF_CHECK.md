# Monitor Self Check

Use this when you copy the base, rename the outer folder, or want quick proof that the monitor is still pointed at the right places.

## Command

From the outer workspace folder:

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- --self-check
```

Expected result:

```text
EXIT=0
```

The self-check is read-only. It does not create `Monitor\Working`, launch telemetry, or open WinMerge.

## What To Look For

The output should show:

- `app_root` points to the copied workspace's `Monitor` folder.
- `working_dir` points to `Monitor\Working`.
- `configured_observed_root` is either blank in a fresh template or points to the sibling watched project.
- `monitor-root` is rejected.
- `working-root` is rejected.
- `bundled-sample` is allowed.
- `configured-observed-root` is allowed after initialization.
- the configured diff tool is resolvable, or you know you will pass an explicit diff path.

## Quick Guardrail Checks

These should fail with exit code `20`:

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- ".\Monitor\Docs\Samples\TinyConsoleWatchedProject\Program.cs" --observed-root ".\Monitor" --refresh-only --no-prune
```

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- ".\Monitor\Docs\Samples\TinyConsoleWatchedProject\Program.cs" --observed-root ".\Monitor\Working" --refresh-only --no-prune
```

This should pass with exit code `0` because the bundled sample is the intentional in-Monitor exception:

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- ".\Monitor\Docs\Samples\TinyConsoleWatchedProject\Program.cs" --observed-root ".\Monitor\Docs\Samples\TinyConsoleWatchedProject" --refresh-only --no-prune
```

## Snapshot Diff Check

The monitor creates a timestamped proposed snapshot before launching the diff tool. The diff tool reviews:

```text
left  = proposed snapshot
right = existing source
```

That keeps the visible diff aligned with the ledger and history file.

## Retention Constants

The self-check prints the current retention constants:

```text
run_log_entry_retention_limit = 500
telemetry_run_retention_limit = 50
telemetry_line_retention_limit = 300
```

These keep AI-readable JSON logs useful without letting them grow forever.

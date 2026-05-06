# AIWorkflowRunner Execution Flow

This file explains the runtime path through the `Monitor\AIWorkflowRunner.*.cs` partial files. The partial layout is intentional: it keeps related code findable without adding DI/container ceremony.

## Main Entry

```text
Program.Main()
  -> AIWorkflowRunner.Run(args)
```

`Run` is the orchestration path. It parses the command, validates the local monitor setup, records telemetry, and decides whether the current run is self-check, refresh, compare, or prune work.

## Startup Flow

```text
Run(args)
  -> acquire single-run lock
  -> parse options
  -> resolve app root
  -> load appsettings.template.json + local appsettings.json
  -> validate runtime configuration
  -> resolve observed root
  -> derive observed-root key
  -> derive Working/History/Archive paths
  -> start run telemetry
```

Key files:

- `AIWorkflowRunner.cs`
- `AIWorkflowRunner.Options.cs`
- `AIWorkflowRunner.Paths.cs`
- `AIWorkflowRunner.Telemetry.cs`

The observed-root key keeps generated state for different watched projects separated under `Monitor\Working`.

## Self Check Flow

```text
--self-check
  -> resolve effective paths
  -> verify observed-root guardrails
  -> verify diff tool resolution
  -> print exit-code and retention constants
  -> exit without creating normal proposal state
```

Key file:

- `AIWorkflowRunner.SelfCheck.cs`

Self-check is a manual confidence check, not a mandatory tollbooth on every run.

## Refresh Flow

```text
source file path
  -> validate source path is under observed root
  -> map source path to keyed Working path
  -> copy source content into Working
  -> write refresh state
  -> optionally stop for --refresh-only
```

Key files:

- `AIWorkflowRunner.cs`
- `AIWorkflowRunner.Paths.cs`
- `AIWorkflowRunner.StateAndLog.cs`

Refresh creates the editable proposal copy. Codex normally edits the Working file, not the watched source file.

## Compare Flow

```text
--compare-only or refresh-and-compare
  -> verify Working refresh state is current
  -> run Roslyn preflight with sparse Working overlay for .cs files
  -> create immutable proposed snapshot
  -> write run details and telemetry
  -> append optional per-file ledger entry
  -> launch diff tool against proposed snapshot and source
```

Key files:

- `AIWorkflowRunner.cs`
- `AIWorkflowRunner.ContractGuard.cs`
- `AIWorkflowRunner.FileLedger.cs`
- `AIWorkflowRunner.StateAndLog.cs`
- `AIWorkflowRunner.Telemetry.cs`

WinMerge orientation is:

```text
left  = proposed snapshot from Monitor\Working history
right = current watched source file
```

The snapshot is created before diff launch so the visible review, ledger entry, and history artifact refer to the same proposed content.

## Roslyn Preflight Flow

```text
selected Working .cs file
  -> collect watched source .cs files
  -> overlay current Working .cs proposals
  -> include new Working-only .cs files
  -> compile syntax trees with trusted platform references
  -> block or warn according to validation policy
```

Key file:

- `AIWorkflowRunner.ContractGuard.cs`

This is a fast preflight, not a full MSBuild project build. The watched project's real build and QA run remain the final truth after merge.

Razor component files such as `.razor` are not sent through this compilation gate. The monitor still snapshots, ledgers, and launches the diff for Razor reviews, but the real watched-project build is the authority for markup/component compilation.

## Ledger And History Flow

```text
compare run
  -> snapshot proposed file
  -> write per-run JSON detail
  -> append per-file ledger when summary is supplied
  -> prune old loose history
  -> zip archived history when retention rules apply
```

Key files:

- `AIWorkflowRunner.FileLedger.cs`
- `AIWorkflowRunner.StateAndLog.cs`
- `AIWorkflowRunner.Prune.cs`

Routine edit history belongs in the ledger. Durable source understanding belongs in `AIFileContext`, `FileVersion`, and nearby feature/component maps after behavior stabilizes.

## Telemetry Flow

```text
run starts
  -> append live telemetry lines
  -> optionally open telemetry window
  -> cap retained telemetry runs and lines
```

Key file:

- `AIWorkflowRunner.Telemetry.cs`

Telemetry is the live monitor log, not the diff tool. It may open even when no compare window opens.

## Exit Flow

```text
success or known stop
  -> set explicit exit code
  -> write final telemetry
  -> release run lock
```

Common exit codes:

```text
0   Success.
10  Usage or configuration error.
20  Source path or observed-root path error.
30  Working refresh state is stale.
40  Roslyn validation blocked compare.
50  Diff tool launch failed.
60  No differences found; compare window skipped.
70  Another monitor run is already active.
```

See [MONITOR_COMMAND_REFERENCE.md](MONITOR_COMMAND_REFERENCE.md) for the full command reference.

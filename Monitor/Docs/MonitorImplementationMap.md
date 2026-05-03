# Monitor Implementation Map

<!-- HUMAN-OWNED: preserve unless explicitly asked to edit. -->
## Human Notes

- The monitor is a solo local workflow tool, not a multi-user platform.
- Reliability matters, but avoid judging the implementation as if it were a hosted service, CI system, or team PR product.
- Git, CI, and team review are downstream of this local proposal gate.
- The monitor uses `AIWorkflowRunner.*.cs` partial files intentionally.
- The naming is meant to make each concern easy for Codex and the human to find without adding DI/container ceremony.
- Do not collapse these files back into one large runner file.
- Do not introduce abstractions just to make the code look more traditionally architectural.

<!-- AI-MAINTAINED: update during documentation-only passes or direct map-update requests. -->
## File Responsibilities

- `Program.cs`: loads configuration and invokes `AIWorkflowRunner`.
- `AIWorkflowRunner.cs`: main workflow orchestration, run lock, diff tool launch, compare lifecycle, human override UI.
- `AIWorkflowRunner.Options.cs`: CLI option parsing, usage text, observed-root resolution.
- `AIWorkflowRunner.Paths.cs`: app root and external tool path resolution.
- `AIWorkflowRunner.ContractGuard.cs`: Roslyn validation, Working overlay validation, comment churn warning.
- `AIWorkflowRunner.FileLedger.cs`: per-file ledger entries, archive references, ledger retention.
- `AIWorkflowRunner.Prune.cs`: history snapshot archival into zip files.
- `AIWorkflowRunner.StateAndLog.cs`: refresh state, run logs, hashes.
- `AIWorkflowRunner.Telemetry.cs`: telemetry capture, screen log formatting, optional telemetry window.
- `AIWorkflowRunner.Models.cs`: small shared model types used by active workflow components.

## Control Flow

```text
[Program.Main]
     |
     v
[AIWorkflowRunner.Run]
     |
     +--> [Options / Observed Root]
     |
     +--> [Refresh Working Copy]
     |
     +--> [Roslyn Contract Guard + Working Overlay]
     |
     +--> [Snapshot + Ledger]
     |
     +--> [Diff Tool Launch]
     |
     +--> [Prune / Archive]
```

## Maintenance Notes

- Keep generated monitor state out of source: `Working`, `History`, `Archive`, telemetry, `bin`, and `obj`.
- Prefer narrow edits inside the matching partial file.
- If a concern grows large enough to become hard to navigate, add another clearly named partial file before introducing broad architecture.
- The archived source-map work lives under `Monitor\Docs\ArchivedSourceMap` and is not part of the active monitor build.

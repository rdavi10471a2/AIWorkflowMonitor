# Start Here Workflow Summary

This workspace is a reusable AI proposal review gate. Copy it, rename the outer folder, add a sibling watched project, initialize the monitor, and then use the monitor to keep AI-generated changes reviewable.

## Scope

This is a solo local workflow tool. It is meant to sit between Codex-generated proposals and the real watched project on one developer machine.

It is not a team source-control platform, CI system, hosted review service, or replacement for Git. The monitor gives one human a local proposal gate, Roslyn early warning, short-term proposal history, and a serial diff review loop. Git remains the durable project history after source changes are accepted and tested.

Review this project as a practical local safety tool, not as a multi-user production service.

## Folder Model

```text
C:\VSCodeProjects\MonitorProject\
  Monitor\
  MyWatchedProject\
```

`Monitor` owns tooling and generated review state. `MyWatchedProject` owns the real source code and Git repository.

Generated monitor state belongs under `Monitor`:

```text
Monitor\Working
Monitor\Working\History
Monitor\Working\History\Ledgers
Monitor\Working\Archive
Monitor\Working\History\Telemetry
```

## Setup Loop

1. Copy the clean base folder.
2. Rename the copied outer folder manually.
3. Open the renamed outer folder in VS Code.
4. Tell Codex to read the markdown files in the whole workspace.
5. Create or clone the real watched project as a sibling of `Monitor`.
6. Tell Codex to initialize the monitor for that watched root.
7. Put Git on the watched project.

Initialization should update `Monitor\appsettings.json`, install `AI\AIAttributes.cs` into the watched project, build `Monitor\AIWorkflowMonitor.csproj`, and run one `--refresh-only` pass.

The watched root should be a sibling project, not the `Monitor` folder and not `Monitor\Working`. The bundled tiny sample under `Monitor\Docs\Samples` is the intentional exception for smoke tests.

Run `dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- --self-check` when you want to verify effective paths, root guardrails, diff tool resolution, exit codes, and retention constants.

For an existing project that already has legacy AI helper files/comments from an older monitor workflow, do not spend tokens cleaning old comments unless that cleanup becomes a separate explicit task. Initialization preserves old helper files and adds only missing current workflow attributes in `AI\AIWorkflowCurrentAttributes.cs`. Use `-SkipAIAttributes` only when you want zero attribute file changes.

## Daily Work Loop

For existing source files:

```text
refresh source into Working
Codex edits Working
Roslyn preflights the proposed Working set
WinMerge shows one serial review diff
human merges proposal into source
human immediately builds/runs/QA tests
Codex iterates on the next proposal
```

WinMerge orientation is new/proposed snapshot on the left and existing source on the right. The snapshot is created before diff launch so the ledger, history file, and visible review all refer to the same proposed content.

For multi-file edits, Codex may edit all needed Working files first. The monitor validates with a sparse Working overlay so related proposed files can compile together, while the human still reviews one diff at a time.

Roslyn validation is a fast preflight, not a replacement for the watched project's real build. It uses the proposed Working set plus source context to catch obvious compile/semantic breaks before compare. The real source build and QA pass remain the source of truth after merge.

Telemetry is the live monitor log, not the diff tool. A run may open telemetry even when WinMerge does not open, for example when there is no diff to review, when running a validation-focused compare, or when telemetry auto-open is enabled. The telemetry window exists so the human can see what the monitor did during the local proposal cycle.

The AI-readable JSON logs are capped: `_runs.json` keeps 500 recent entries, and `_telemetry.json` keeps 50 recent runs with up to 300 retained lines per run.

## New File Exception

For brand-new features that create new files, tell Codex to implement directly in the watched project when needed. The monitor is strongest after the file exists and can be refreshed into `Working`.

Use this shape:

```text
create new files directly in watched project
build/run/QA
bring existing files under monitor for refinement
review serial diffs
```

New C# files should include `AIFileContext` and `FileVersion("1.0")`.

## Attribute Model

Watched projects should contain:

```text
MyWatchedProject\AI\AIAttributes.cs
```

Use attributes for durable source context:

- `AIFileContext`: what future Codex/human readers need to know about the file
- `FileVersion`: file-level version marker for meaningful edits
- `AIChange`: rare source-visible marker for unusual edits

Do not use source attributes as routine logging. Routine change summaries belong in the monitor ledger.

## Ledger Model

When comparing a meaningful edit, Codex should pass:

```powershell
--ledger-summary "v1.2 changed validation routing and kept source comments minimal."
```

The monitor writes per-file ledger entries under:

```text
Monitor\Working\History\Ledgers
```

The ledger points to the exact compare snapshot. Old loose snapshots are zipped into `Monitor\Working\Archive`; ledger entries are short-term working memory.

## Git Model

Git belongs on the watched project, not on `Monitor\Working`.

Use Git for meaningful source history:

1. Create a feature branch for real work.
2. Use monitor cycles for proposal/review/QA safety.
3. Commit stable chunks when they are meaningful.
4. Avoid committing every AI proposal.
5. When the feature is complete and tested, run a documentation-only pass.
6. Commit or merge the completed feature branch in bulk.

The monitor is source control before source control. Git is the durable project history.

## Documentation Timing

Do not try to document every churn step. After a feature stabilizes, ask Codex for a documentation-only pass:

- add/update `AIFileContext`
- bump `FileVersion` for meaningful C# edits
- create or update component/feature maps for multi-file areas
- add feature/data flow notes
- avoid routine inline comments
- do not change behavior

Inline comments are only for real traps, invariants, lifecycle rules, or non-obvious behavior.

Use `Monitor\Docs\Samples\FeatureMapTemplate.md` for maps that need a human-owned notes section and an AI-maintained generated flow section.

## Tiny Smoke Test

A disposable watched project lives at:

```text
Monitor\Docs\Samples\TinyConsoleWatchedProject
```

Use it to verify monitor refresh, Roslyn validation, Working overlay behavior, and AI attribute compilation without touching a real project.

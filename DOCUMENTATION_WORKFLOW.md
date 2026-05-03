# Documentation Workflow

The monitor workflow should not create thousands of lines of undocumented code and then try to recover by stuffing process comments into source files. Source should carry durable explanations; monitor-owned history should carry routine edit records.

## Philosophy

The human directs intent, tests behavior, and decides whether a merge is accepted.

Codex can maintain documentation, but should do it after the implementation stabilizes. Documentation should summarize settled behavior and useful navigation, not narrate every intermediate attempt.

Generate documentation after stability, not during churn. Comments and maps should describe stabilized understanding, not every intermediate attempt.

## Source Documentation

Inline source comments are the exception. Use them only for code facts that future maintainers need while reading the file:

- non-obvious business rules
- fragile UI or CSS behavior
- public API assumptions
- threading, async, disposal, or lifetime constraints
- parsing, serialization, database, or file-format assumptions

Do not add routine AI/process comments to source files just to prove that an edit happened.

During normal implementation, avoid adding or rewriting comments unless the edit invalidates an existing comment or the code has a real trap/invariant. After implementation and testing, use a documentation-only pass to add curated file context and feature/data-flow maps.

The monitor warns, without blocking compare, when the selected Working file appears to add many comment lines. Treat that as a review nudge: routine edit history belongs in `--ledger-summary` and component maps, not source comments.

## File Context

Add `AIFileContext` to every new C# source file Codex creates. Treat it as "what you need to know about this file," not as an edit log.

For existing C# files, add or update `AIFileContext` when the file is important, frequently edited, split across partials, Razor-adjacent, or hard to understand locally.

For meaningful C# edits, bump the edited file's `FileVersion`. For new C# files, add `FileVersion("1.0")`. For partial classes, each physical partial file may have its own `AIFileContext` and its own `FileVersion`; treat the version as file-level, not whole-type.

For Razor, prefer code-behind attributes or nearby markdown maps. Avoid stacked Razor comments unless they explain actual rendering behavior.

## Feature/Component Maps

For large generated surfaces or multi-file areas, add or maintain a nearby markdown map in the watched project. "Component" is broad here: it may be a UI component, feature folder, namespace folder, service area, subsystem, Razor surface, or any cluster of files that are usually changed together.

Examples:

```text
Components\Pages\ManageViews\README.md
UI\MergedEditorSurface\IntegrationsViewImportControl.md
Services\ImportPipeline\README.md
Domain\ViewModels\README.md
```

Use `Monitor\Docs\Samples\FeatureMapTemplate.md` when creating a new map. It separates human-owned notes from AI-maintained generated flow sections.

A feature/component map should include:

- what the subsystem/page/control does
- which files own which responsibilities
- where important methods/events live
- partial-class ownership notes
- Razor/code-behind relationships
- maintenance warnings that would prevent blind edits

Keep maps practical. The point is to route future Codex work without rereading an entire thousand-line surface or namespace folder.

Human-owned sections should hold durable intent, dated QA discoveries, mandated behavior, and constraints that should not be rewritten unless explicitly requested. AI-maintained sections should hold generated feature summaries, source anchors, control flow, data flow, file responsibilities, and maintenance notes.

## Post-Feature Documentation Pass

After a feature is implemented and tested, run a documentation-only pass. AI is often better at summarizing after the code exists than during the first implementation.

Use this prompt:

```text
The feature is implemented and tested. Do a documentation pass only:
- Add AIFileContext to each new C# source file.
- Add or update a nearby feature/component README map if this feature spans multiple files, namespace folders, partial classes, Razor/code-behind, service areas, or generated-style surfaces.
- In that map, include a basic feature flow and data flow summary.
- Avoid inline comments unless they document a trap, invariant, framework lifecycle rule, or non-obvious behavior.
- Do not add routine AI/process comments.
- Do not change behavior.
```

The expected output is better navigation for future work, not a rewritten codebase.

This pass is where Codex should manage comments and durable documentation. It may add or update `AIFileContext`, feature/component maps, and a small number of high-value comments for traps or invariants. It should not create a running log in source files.

## Feature And Data Flow Notes

For non-trivial features, the feature/component map should include a short flow summary:

- entry points: UI events, commands, routes, public methods, or background triggers
- data inputs: user inputs, services, files, database tables, API calls, or parameters
- state changes: fields, view models, caches, persisted records, or UI state
- outputs: rendered UI, saved data, emitted events, files, logs, or service calls
- validation/error path: where failures are detected and surfaced

Keep this high level. The goal is to help Codex and the human find the right file quickly.

## Monitor-Owned File Ledger

Routine edit history belongs under monitor state, not in source. The monitor writes a compact per-file ledger entry when it creates a compare snapshot. Codex should pass `--ledger-summary` when it has useful context for the change:

```text
Monitor\Working\History\Ledgers\
  <safe-relative-source-path>.md
```

Example compare command:

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- "C:\Path\To\Project\File.cs" --compare-only --ledger-summary "v1.2 changed duplicate-name validation routing and kept Razor comments minimal."
```

When a C# source file's `FileVersion` was bumped, include that version in the ledger summary.

Suggested entry format:

```markdown
## 2026-05-02 21:10

File: `Components/Pages/ManageViews/EditView.razor`
Snapshot: `Working/History/.../EditView_20260502_211000.razor`
ArchiveZip: `not archived`
ArchiveEntry: `not archived`

Changed duplicate-name validation routing and removed noisy Razor process comments.
```

Every ledger entry directly correlates to one history snapshot. While the snapshot is still loose, use `Snapshot`. After prune zips it, the monitor updates the entry with `ArchiveZip` and `ArchiveEntry`; the original `Snapshot` path remains as the source trace.

The ledger is not a gate or status tracker. If the human merged the diff, the source is accepted. If not, the snapshot remains a recovery/reference artifact until retention moves it.

## Retention

The monitor currently zips matching source-file history snapshots older than 7 days into `Working\Archive\history_yyyyMMdd.zip`, updates matching ledger entries with the zip path and entry name, then removes the loose snapshot file from `Working\History`.

Per-file ledgers are short-term working memory. Entries older than 2 days are trimmed during prune. Logs, telemetry, errors, and zip archives are not pruned by the current code.

## Proposal Recovery

The monitor keeps enough proposal history to reconstruct recent AI work:

- current proposed file: `Working\...`
- loose compare snapshot: `Working\History\...`
- running log entry: `Working\History\Ledgers\...`
- archived snapshot: `Working\Archive\history_yyyyMMdd.zip`

The ledger is the index. Each entry points to the loose `Snapshot`; after zipping, it also points to `ArchiveZip` and `ArchiveEntry`. Use those paths when asking Codex to explain, reconstruct, or undo a recent AI proposal.

This is intentionally lighter than Git checkpoints. It protects the AI proposal/review layer; Git remains the meaningful source-history layer.

## Git And Documentation Timing

Git belongs on the watched project. The monitor's Working, History, Ledger, and Archive folders are proposal/review artifacts and should not be treated as source history.

Use Git branches for real feature work:

- create or switch to a feature branch before substantial work
- use the monitor for small proposal/review/QA loops
- commit stable chunks when they are meaningful
- avoid committing every AI proposal as a Git checkpoint
- after the feature is implemented and tested, run the documentation-only pass
- commit the finished feature and documentation together, or as a small final docs commit

This keeps documentation aligned with settled behavior instead of churn, and keeps Git useful as a human-readable project history.

## Multi-File Compare Queue

When a change touches multiple files, Codex should not open every GUI diff at once. It should:

1. List the files that need review.
2. Launch the first compare only.
3. Report the exact file, Original path, Working path, and Proposed snapshot path.
4. Wait for the human to say to continue.
5. Launch the next compare only after that instruction.

This keeps WinMerge review oriented and prevents several windows from appearing faster than the human can track.

WinMerge orientation: new/proposed working file is on the left; existing source file is on the right.

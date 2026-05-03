# MonitorProject Workspace Instructions

This workspace is meant to contain the monitor and one or more watched projects as sibling folders.

Generate documentation after stability, not during active churn.

Expected layout:

```text
C:\VSCodeProjects\MonitorProject\
  Monitor\
  MyWatchedProject\
```

Use `Monitor` for the monitoring tool. Use a sibling folder for the project being watched.

When initializing the monitor for a watched project:

1. Create local `Monitor\appsettings.json` from tracked `Monitor\appsettings.template.json` if needed.
2. Set local `WorkflowSettings:ObservedRoot` to the absolute watched project path.
3. Ensure the watched project contains `AI\AIAttributes.cs` with the watched project namespace.
4. Build `Monitor\AIWorkflowMonitor.csproj`.
5. Run a `--refresh-only` pass for a source file inside the watched project.

Generated monitor state belongs under `Monitor`, not inside the watched project.

When Codex edits watched source files, preserve existing `AIFileContext`, `FileVersion`, and meaningful `AIChange` attributes. Do not add routine AI/process comments to source files. Put routine change summaries in monitor-owned history/ledger notes or nearby component maps.

For meaningful C# edits, bump the edited file's `FileVersion`. For new C# files, add `AIFileContext` and `FileVersion("1.0")`. For partial classes, every physical partial file may have its own `FileVersion`; treat it as file-level, not whole-type.

Do not use C# top-level statements in generated source or samples. Use an explicit `Program` class and `Main` method for console entry points.

When running compare after a meaningful edit, pass `--ledger-summary "<short summary>"` so the monitor writes the running comment log under `Monitor\Working\History\Ledgers` instead of putting routine process comments in source. Include the edited file's `FileVersion` value in the summary.

For large generated or frequently edited surfaces, create or maintain a nearby markdown map in the watched project. A map may cover a UI component, feature folder, namespace folder, service area, subsystem, Razor surface, or partial-class family. See `DOCUMENTATION_WORKFLOW.md` and `Monitor\Docs\Samples\ComponentMap.md`.

When creating a new feature map, prefer `Monitor\Docs\Samples\FeatureMapTemplate.md`. Preserve human-owned sections unless explicitly asked to edit them; update AI-maintained generated flow sections only during documentation passes or direct map-update requests.

After a feature is implemented and tested, Codex may be asked for a documentation-only pass. In that pass, add `AIFileContext` to new C# files, add or update nearby component maps with feature/data flow notes, avoid routine inline comments, and do not change behavior.

Multi-file compare rule: never launch multiple GUI diff windows back-to-back. If a change requires more than one file, build an ordered compare queue, launch the first diff only, report the exact file plus Original/Proposed paths, and wait for the user to say to continue before launching the next diff. If the user says a file is accepted/merged, move to the next queued file only after that explicit instruction.

For multi-file C# edits, it is valid to update all required Working files before launching the first compare. The monitor's Roslyn validation uses current sibling Working `.cs` files as an overlay so proposed multi-file changes can validate together while diffs are still reviewed serially.

New-file exception: when a brand-new feature requires new source files that do not yet exist in the watched project, Codex may implement those files directly in the watched project if the user asks to "run and go no monitor" or otherwise asks for an initial feature creation pass. Add `AIFileContext` and `FileVersion("1.0")` to new C# files. After the files exist, return to the monitor workflow for refinement, serial diffs, ledger summaries, and documentation cleanup.

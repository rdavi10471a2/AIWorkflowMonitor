# Prompt: Review This Solo Local Monitor Workflow

Copy/paste this prompt into another AI session when asking it to review the monitor. Attach or provide the relevant source/docs after the prompt.

```text
I want you to review this project, but please anchor the review to its actual intended use.

This project is a solo local proposal gate for AI-assisted coding on one developer machine.

The intended loop is:

Codex proposes changes in Monitor\Working
Roslyn gives an early warning preflight
WinMerge shows one file diff at a time
human merges or refuses the proposal
human builds/runs/QA tests the real watched project
Git records meaningful stable source history later

This is not intended to be:

- a hosted team review system
- a CI replacement
- a Git replacement
- a pull-request platform
- a multi-user workflow coordinator
- a general IDE automation framework

Please review it as a practical local safety tool, not as a production platform.

Telemetry intent:

Telemetry is intentionally visible. It is a live run log for the human because chat output can move too fast and because a separate window gives the operator a stable place to see what the monitor did.

Telemetry is not the diff tool. A telemetry window may open even when WinMerge does not open, especially when:

- there is no file diff
- the run only refreshes
- validation or logging happened before compare
- telemetry auto-open is enabled

The JSON run/telemetry files are primarily for AI inspection and short-term troubleshooting, not for the human to read manually.

Current design choices:

- `Monitor` owns generated state under `Monitor\Working`.
- The watched project is a sibling folder and owns real source plus Git.
- WinMerge orientation is new/proposed snapshot on the left and existing source on the right.
- The monitor reviews one diff at a time, but Codex may prepare several Working files before serial review.
- Roslyn overlay validation is a fast preflight, not full MSBuild project compilation.
- Durable documentation belongs in `AIFileContext`, `FileVersion`, and nearby feature/component maps after a feature stabilizes.
- Routine edit history belongs in monitor ledgers, not in source comments.
- The partial-file structure of `AIWorkflowRunner.*.cs` is intentional for navigation without DI/container ceremony.

Recent hardening:

- Generalized the base away from the old Schema Studio project identity.
- Added reusable initialization docs and sample watched console project.
- Added sparse Working overlay validation for multi-file proposals.
- Added per-file ledgers and zipped history archive behavior.
- Clarified telemetry-vs-diff behavior in docs.
- Capped `_runs.json` at 500 entries and `_telemetry.json` at 50 runs / 300 retained lines per run so AI-readable logs stay recent and bounded.
- Added startup logging of effective `app_root`, `observed_root`, `working_dir`, and `history_dir`.
- Added `--self-check` plus `SELF_CHECK.md` for root guardrails, diff tool resolution, exit code table, and retention constants.
- Aligned diff input with ledger/snapshot semantics so the diff tool reviews the immutable proposed snapshot.
- Added watched-root guardrails to reject accidental Monitor/Working roots while preserving the bundled sample exception.
- Added explicit process exit codes for script/tool interpretation:

```text
0   Success.
1   Unexpected monitor failure.
10  Usage or configuration error.
20  Source path or observed-root path error.
30  Working refresh state is stale.
40  Roslyn validation blocked compare.
50  Diff tool launch failed.
60  No differences found; compare window skipped.
70  Another monitor run is already active.
```

Helpful review:

- find realistic solo-workflow reliability bugs
- find places where the AI could misunderstand the workflow
- find places where generated state could leak into the watched project
- find places where diff orientation, ledger correlation, or Roslyn validation could mislead the human
- suggest small hardening changes that preserve the local proposal-gate model

Less helpful review:

- turning the monitor into a hosted service
- requiring a full adapter/interface architecture before the tool has that pressure
- treating visible telemetry as accidental UI coupling
- treating Git/CI/team PR features as missing from the monitor instead of downstream from it

Please answer in this structure:

1. Confirm or challenge whether the stated scope is coherent.
2. List the top 5 practical risks for this solo/local workflow.
3. Separate issues into:
   - fix now
   - watch later
   - not worth changing for this tool
4. Identify any docs or prompts that could cause another AI to misunderstand the workflow.
5. Suggest small next changes only if they preserve the local proposal-gate model.

Open questions worth reviewing:

- Should project-aware MSBuild validation be added as an optional deeper mode while keeping the current Roslyn overlay as fast preflight?
- Are the docs clear enough that a copied base folder can be renamed, opened, initialized, and used without remembering this conversation?
```

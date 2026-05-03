# Feature Map Template

Use this as a nearby markdown map for a feature, component, namespace folder, Razor surface, service area, or partial-class family.

<!-- HUMAN-OWNED: preserve this section unless explicitly asked to edit it. -->
## Human Notes

- `YYYY-MM-DD - Feature/Bug/Mandate:` write durable human intent, constraints, QA discoveries, or business rules here.
- Keep notes short and concrete.
- Prefer source anchors when a note depends on specific implementation.

<!-- AI-MAINTAINED: update during documentation-only passes after implementation stabilizes. -->
## Generated Feature Summary

Describe what this feature does and what problem it solves.

## Source Anchors

- `Path/To/File.cs:42` - important entry point or behavior.
- `Path/To/OtherFile.cs:88` - important dependency or state transition.

## Control Flow

```text
[Entry Point]
     |
     v
[Coordinator/Handler]
     |
     +--> [Validation/Error Path] -> [User/Caller Feedback]
     |
     v
[Persistence/External Call]
     |
     v
[UI/State/Result Refresh]
```

## Data Flow

- Inputs:
- State touched:
- Outputs:
- Validation/error behavior:

## File Responsibilities

- `Path/To/File.cs`:
- `Path/To/OtherFile.cs`:

## Maintenance Notes

- Traps, invariants, lifecycle rules, or framework quirks that should guide future edits.
- Avoid routine edit history here; use the monitor ledger for proposal traces.

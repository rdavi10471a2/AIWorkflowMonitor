# Feature/Component Map

Use this file as a nearby map for a large page, control, feature folder, namespace folder, service area, subsystem, Razor surface, or partial-class family.

## Purpose

Briefly describe what this feature/component/subsystem does.

## File Responsibilities

- `MainFile.cs`: owns the public entry points and orchestration.
- `MainFile.Rendering.razor`: owns markup/rendering details.
- `MainFile.Events.cs`: owns UI event handlers.

## Namespace Or Folder Scope

Describe the namespace/folder this map covers and what files are intentionally out of scope.

## Important Methods

- `MethodName`: why it matters and which file owns it.

## Feature Flow

1. Entry point: route, UI event, command, public method, or background trigger.
2. Coordination: file/type that orchestrates the workflow.
3. Result: rendered UI, saved data, service call, event, or file output.

## Data Flow

- Inputs: user input, parameters, services, database rows, files, or API responses.
- State changes: view model fields, component state, caches, persisted records, or selected items.
- Outputs: UI updates, saved records, emitted events, logs, files, or service calls.
- Validation/error path: where failures are detected and how they surface.

## Partial And Razor Notes

Describe each partial file's responsibility, where `AIFileContext` should live, and whether Razor markup should defer durable context to code-behind. `FileVersion` is file-level, so each physical partial file may have its own version.

## Maintenance Notes

List gotchas that should prevent broad or blind edits.

# AI Attributes

Every watched C# project should include the standard AI attribute file. During initialization, `Initialize-MonitorProject.ps1` installs it at:

```text
MyWatchedProject\
  AI\
    AIAttributes.cs
```

The namespace is generated from the watched project, usually:

```csharp
namespace MyWatchedProject.AI;
```

A copyable sample lives at:

```text
Monitor\Docs\Samples\AIAttributes.cs
```

## Current Purpose

AI attributes are now for durable source context, not routine process logging.

Use them sparingly:

- `AIFileContext`: explains what a file or partial file is responsible for.
- `FileVersion`: gives a simple visible version marker for C# files/classes touched by the monitor workflow.
- `AIChange`: optional compact source marker for unusual edits that really need to remain visible in code.

Routine "what changed this time" notes should go in monitor-owned history/ledger notes, not in source files.

The sample also keeps `AICommandStatus` and a status-aware `AIChange` constructor for legacy compatibility. New workflow edits should not use status-bearing source attributes as the normal approval process; the monitor ledger is the routine workflow record.

For full compatibility with projects created under older monitor workflows, the sample also includes legacy `DoNotRefactor`, `AIInstructions`, `AIHistory`, and `UserHistory` attributes. They are retained so old source annotations compile. Do not treat them as the recommended path for new workflow records.

## FileVersion Rule

For meaningful C# edits, increment the edited file's `FileVersion`. Use the same version in the `--ledger-summary` text so the source marker and monitor ledger can be correlated.

For new C# files, add both `AIFileContext` and `FileVersion("1.0")`.

For partial classes, every physical partial file may have its own `FileVersion`. Treat the version as a file version, not a whole-type version.

## Example

```csharp
using MyWatchedProject.AI;

namespace MyWatchedProject.Services;

[FileVersion("1.1")]
[AIFileContext(
    "Services/OrderWorkflowService.cs",
    "Coordinates order validation, pricing, persistence, and notification handoff.",
    Responsibilities = "Keep public workflow entry points stable for UI callers.",
    Nuances = "Cancellation is part of the service contract.")]
public sealed class OrderWorkflowService
{
    public async Task<OrderResult> SubmitOrderAsync(OrderDraft draft, CancellationToken cancellationToken)
    {
        // Implementation omitted.
    }
}
```

## Partial Classes

For partial classes:

- Keep `FileVersion` per physical file.
- Put `AIFileContext` on each partial file when the partials have different responsibilities.
- Use the physical file path in the first `AIFileContext` argument so Codex can route future edits without opening every partial file.

## Razor Files

Avoid routine AI/process comments in `.razor` files. They make human diff review noisy.

Prefer:

- `AIFileContext` on `.razor.cs` code-behind when available.
- A nearby component README/map for complex Razor surfaces.
- Plain Razor comments only for real rendering or UI behavior explanations.

## Codex Instruction

Use this instruction when asking Codex to work in a watched project:

```text
This project uses AI workflow attributes for durable source context only. Preserve existing AIFileContext, FileVersion, and meaningful AIChange attributes. Do not add routine per-edit process comments or stacked source attributes. Record routine change summaries in the monitor-owned file ledger or component markdown map instead.
```

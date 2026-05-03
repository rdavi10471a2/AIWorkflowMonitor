# Tiny Console Watched Project

This is a tiny disposable C# console project for monitor smoke tests. It is not a template for real watched projects and should not be copied into production source.

Use it when you want to verify that the monitor can refresh, validate, and compare a harmless watched project.

It includes `AI\AIAttributes.cs` and sample `AIFileContext` / `FileVersion("1.0")` usage so the monitor base tests the watched-project attribute convention too.

Example refresh-only command from the workspace root:

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- ".\Monitor\Docs\Samples\TinyConsoleWatchedProject\Program.cs" --observed-root ".\Monitor\Docs\Samples\TinyConsoleWatchedProject" --refresh-only --no-prune
```

Example compare-only validation command after editing the Working copy:

```powershell
dotnet run --project ".\Monitor\AIWorkflowMonitor.csproj" -- ".\Monitor\Docs\Samples\TinyConsoleWatchedProject\Program.cs" --observed-root ".\Monitor\Docs\Samples\TinyConsoleWatchedProject" --compare-only --no-prune --ledger-summary "Smoke-tested monitor validation against the tiny console sample."
```

For overlay testing, refresh both `Program.cs` and `Services\GreetingCalculator.cs`, edit both matching files under `Monitor\Working\<observed-root-key>\TinyConsoleWatchedProject`, then compare one file. The observed-root key includes the watched folder name plus a short path hash so same-named watched roots do not share state. Roslyn should validate with the sibling Working file as an overlay while still launching only one diff.

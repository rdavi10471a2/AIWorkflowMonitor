namespace AIWorkflowMonitor;

internal static partial class AIWorkflowRunner
{
    private static int RunSelfCheck()
    {
        var appRoot = ResolveAppRoot();
        var workingDir = Path.Combine(appRoot, "Working");
        var historyDir = Path.Combine(workingDir, "History");
        var configuredObservedRoot = _configuration?.GetSection("WorkflowSettings")?["ObservedRoot"] ?? string.Empty;
        var sampleRoot = Path.Combine(appRoot, "Docs", "Samples", "TinyConsoleWatchedProject");

        Console.WriteLine("AIWorkflowMonitor self-check");
        Console.WriteLine();
        Console.WriteLine("Effective paths:");
        Console.WriteLine($"  app_root: {appRoot}");
        Console.WriteLine($"  working_dir: {workingDir}");
        Console.WriteLine($"  history_dir: {historyDir}");
        Console.WriteLine($"  configured_observed_root: {FormatOptionalPath(configuredObservedRoot)}");
        if (!string.IsNullOrWhiteSpace(configuredObservedRoot))
        {
            Console.WriteLine($"  configured_observed_root_key: {BuildObservedRootKey(configuredObservedRoot)}");
        }
        Console.WriteLine();

        Console.WriteLine("Observed-root guardrails:");
        PrintGuardrailDecision("monitor-root", appRoot, appRoot);
        PrintGuardrailDecision("working-root", workingDir, appRoot);
        PrintGuardrailDecision("bundled-sample", sampleRoot, appRoot);
        if (!string.IsNullOrWhiteSpace(configuredObservedRoot))
        {
            PrintGuardrailDecision("configured-observed-root", Path.GetFullPath(configuredObservedRoot), appRoot);
        }
        else
        {
            Console.WriteLine("  configured-observed-root: not configured in template appsettings.json");
        }
        Console.WriteLine();

        Console.WriteLine("Diff tool resolution:");
        Console.WriteLine($"  configured_diff_tool: {_configuration?.GetSection("WorkflowSettings:DiffTool").Value ?? "WinMerge"}");
        Console.WriteLine($"  winmerge: {FormatOptionalPath(ResolveWinMergePath())}");
        Console.WriteLine($"  vscode: {FormatOptionalPath(ResolveVSCodePath())}");
        Console.WriteLine($"  beyond_compare: {FormatOptionalPath(ResolveBeyondComparePath())}");
        Console.WriteLine();

        Console.WriteLine("Validation and telemetry:");
        Console.WriteLine($"  contract_enforcement_mode: {ResolveContractEnforcementMode()}");
        Console.WriteLine($"  allow_local_type_evolution: {ResolveAllowLocalTypeEvolution()}");
        Console.WriteLine($"  telemetry_enabled: {ResolveTelemetryEnabled()}");
        Console.WriteLine($"  telemetry_auto_open_window: {ResolveTelemetryAutoOpenWindow()}");
        Console.WriteLine($"  run_log_entry_retention_limit: {RunLogEntryRetentionLimit}");
        Console.WriteLine($"  telemetry_run_retention_limit: {TelemetryRunRetentionLimit}");
        Console.WriteLine($"  telemetry_line_retention_limit: {TelemetryLineRetentionLimit}");
        Console.WriteLine();

        Console.WriteLine("Exit codes:");
        foreach (var value in Enum.GetValues<AIWorkflowRunnerExitCode>())
        {
            Console.WriteLine($"  {(int)value} = {value}");
        }
        Console.WriteLine();

        Console.WriteLine("Self-check complete. No Working state was created.");
        return (int)AIWorkflowRunnerExitCode.Success;
    }

    private static void PrintGuardrailDecision(string label, string observedRoot, string appRoot)
    {
        var observedFullPath = Path.GetFullPath(observedRoot);
        var allowed = ValidateObservedRootSafetyQuiet(observedFullPath, appRoot, out var reason);
        Console.WriteLine($"  {label}: {(allowed ? "allowed" : "rejected")} - {observedFullPath}");
        if (!string.IsNullOrWhiteSpace(reason))
        {
            Console.WriteLine($"    reason: {reason}");
        }
    }

    private static bool ValidateObservedRootSafetyQuiet(string observedRoot, string appRoot, out string reason)
    {
        var observedFullPath = TrimDirectorySeparator(Path.GetFullPath(observedRoot));
        var appRootFullPath = TrimDirectorySeparator(Path.GetFullPath(appRoot));
        var workingRoot = TrimDirectorySeparator(Path.Combine(appRootFullPath, "Working"));
        var sampleRoot = TrimDirectorySeparator(Path.Combine(appRootFullPath, "Docs", "Samples"));

        if (string.Equals(observedFullPath, appRootFullPath, StringComparison.OrdinalIgnoreCase))
        {
            reason = "observed root cannot be the monitor folder";
            return false;
        }

        if (IsSameOrChildPath(observedFullPath, workingRoot))
        {
            reason = "observed root cannot be inside monitor generated state";
            return false;
        }

        if (IsSameOrChildPath(observedFullPath, appRootFullPath)
            && !IsSameOrChildPath(observedFullPath, sampleRoot))
        {
            reason = "observed root cannot be inside Monitor except for bundled docs samples";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static string FormatOptionalPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? "(not found)" : path;
    }
}

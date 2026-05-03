namespace AIWorkflowMonitor;

internal static partial class AIWorkflowRunner
{
    private static bool IsHelpOption(string arg)
    {
        return string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "/?", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSelfCheckOption(string arg)
    {
        return string.Equals(arg, "--self-check", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseOptions(
        string[] args,
        out string? beyondComparePath,
        out string? observedRoot,
        out bool refreshOnly,
        out bool refresh,
        out bool prune,
        out bool openTelemetryWindow,
        out string? ledgerSummary)
    {
        beyondComparePath = null;
        observedRoot = null;
        refreshOnly = false;
        refresh = true;
        prune = true;
        openTelemetryWindow = false;
        ledgerSummary = null;

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--refresh-only", StringComparison.OrdinalIgnoreCase))
            {
                refreshOnly = true;
                refresh = true;
                continue;
            }

            if (string.Equals(arg, "--refresh", StringComparison.OrdinalIgnoreCase))
            {
                refresh = true;
                continue;
            }

            if (string.Equals(arg, "--no-refresh", StringComparison.OrdinalIgnoreCase))
            {
                refresh = false;
                continue;
            }

            if (string.Equals(arg, "--compare-only", StringComparison.OrdinalIgnoreCase))
            {
                refresh = false;
                continue;
            }

            if (arg.StartsWith("--observed-root=", StringComparison.OrdinalIgnoreCase))
            {
                observedRoot = arg["--observed-root=".Length..];
                continue;
            }

            if (string.Equals(arg, "--observed-root", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("Missing value for --observed-root.");
                    return false;
                }

                i++;
                observedRoot = args[i];
                continue;
            }

            if (string.Equals(arg, "--prune", StringComparison.OrdinalIgnoreCase))
            {
                prune = true;
                continue;
            }

            if (string.Equals(arg, "--no-prune", StringComparison.OrdinalIgnoreCase))
            {
                prune = false;
                continue;
            }

            if (string.Equals(arg, "--telemetry-window", StringComparison.OrdinalIgnoreCase))
            {
                openTelemetryWindow = true;
                continue;
            }

            if (arg.StartsWith("--ledger-summary=", StringComparison.OrdinalIgnoreCase))
            {
                ledgerSummary = arg["--ledger-summary=".Length..];
                continue;
            }

            if (string.Equals(arg, "--ledger-summary", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("Missing value for --ledger-summary.");
                    return false;
                }

                i++;
                ledgerSummary = args[i];
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"Unknown option: {arg}");
                return false;
            }

            if (beyondComparePath is null)
            {
                beyondComparePath = arg;
                continue;
            }

            Console.Error.WriteLine($"Unexpected argument: {arg}");
            return false;
        }

        return true;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  AIWorkflowMonitor.exe <sourceFilePath> [diffToolExePath] [options]");
        Console.WriteLine();
        Console.WriteLine("Modes:");
        Console.WriteLine("  (default)       Refresh from source, then compare.");
        Console.WriteLine("  --refresh-only  Refresh Working copy from source and exit.");
        Console.WriteLine("  --compare-only  Compare current Working copy only (no refresh).");
        Console.WriteLine("                  Alias: --no-refresh");
        Console.WriteLine();
        Console.WriteLine("Path Options:");
        Console.WriteLine("  --observed-root <path>   Explicit source root mirrored into Working.");
        Console.WriteLine("  [diffToolExePath]        Optional explicit diff executable path (positional).");
        Console.WriteLine();
        Console.WriteLine("History:");
        Console.WriteLine("  --prune                    Zip old snapshots into Working\\Archive and trim ledgers (default: on).");
        Console.WriteLine("  --no-prune                 Skip archival prune.");
        Console.WriteLine("  --ledger-summary <text>    Add a compact summary to the per-file ledger for this compare.");
        Console.WriteLine();
        Console.WriteLine("Validation:");
        Console.WriteLine("  Roslyn semantic/compile validation runs automatically before compare.");
        Console.WriteLine();
        Console.WriteLine("Telemetry:");
        Console.WriteLine("  --telemetry-window      Force open telemetry window for this run.");
        Console.WriteLine();
        Console.WriteLine("Self Check:");
        Console.WriteLine("  --self-check            Print effective monitor settings and guardrail checks.");
        Console.WriteLine();
        Console.WriteLine("Exit Codes:");
        Console.WriteLine("  0   Success.");
        Console.WriteLine("  1   Unexpected monitor failure.");
        Console.WriteLine("  10  Usage or configuration error.");
        Console.WriteLine("  20  Source path or observed-root path error.");
        Console.WriteLine("  30  Working refresh state is stale.");
        Console.WriteLine("  40  Roslyn validation blocked compare.");
        Console.WriteLine("  50  Diff tool launch failed.");
        Console.WriteLine("  60  No differences found; compare window skipped.");
        Console.WriteLine("  70  Another monitor run is already active.");
        Console.WriteLine();
        Console.WriteLine("Help:");
        Console.WriteLine("  --help | -h | /?        Show this help.");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  - sourceFilePath is required unless requesting help.");
        Console.WriteLine("  - Compare requires a current refresh state for the same source file.");
        Console.WriteLine("  - --compare-only may still refresh if Working copy does not yet exist.");
    }

    private static string ResolveObservedRoot(string? explicitObservedRoot)
    {
        if (!string.IsNullOrWhiteSpace(explicitObservedRoot))
        {
            var explicitPath = Path.GetFullPath(explicitObservedRoot);
            if (!Directory.Exists(explicitPath))
            {
                Console.Error.WriteLine($"ERROR: Explicit observed root directory does not exist: {explicitPath}");
                throw new DirectoryNotFoundException($"Observed root directory not found: {explicitPath}");
            }
            return explicitPath;
        }

        var configuredRoot = _configuration?.GetSection("WorkflowSettings")?["ObservedRoot"];
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            Console.Error.WriteLine("ERROR: ObservedRoot not found in local appsettings.json (WorkflowSettings:ObservedRoot). Run initialization or pass --observed-root <path>.");
            throw new InvalidOperationException("ObservedRoot configuration is required.");
        }

        var fullPath = Path.GetFullPath(configuredRoot);
        if (!Directory.Exists(fullPath))
        {
            Console.Error.WriteLine($"ERROR: Configured observed root directory does not exist: {fullPath}");
            throw new DirectoryNotFoundException($"Observed root directory not found: {fullPath}");
        }

        return fullPath;
    }
}



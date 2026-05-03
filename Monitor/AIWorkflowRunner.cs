using System.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace AIWorkflowMonitor;

internal static partial class AIWorkflowRunner
{
    private const string ArchiveFolderName = "Archive";
    private const string TelemetryFolderName = "Telemetry";
    private const string ErrorFolderName = "Errors";
    private const string LedgersFolderName = "Ledgers";
    private static readonly TimeSpan PruneAge = TimeSpan.FromDays(7);
    private static readonly TimeSpan LedgerRetentionAge = TimeSpan.FromDays(2);
    private static IConfiguration? _configuration;

    private static readonly string[] BeyondCompareCandidates =
    [
        @"C:\Program Files\Beyond Compare 5\BCompare.exe",
        @"C:\Program Files\Beyond Compare 5\BComp.exe"
    ];

    private static readonly string[] WinMergeCandidates =
    [
        @"C:\Program Files\WinMerge\WinMergeU.exe",
        @"C:\Program Files (x86)\WinMerge\WinMergeU.exe"
    ];

    internal static void SetConfiguration(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    internal static int Run(string[] args)
    {
        using var runLock = TryAcquireRunLock();
        if (runLock is null)
        {
            Console.Error.WriteLine("Another AIMonitor workflow run is already active. Wait for it to finish, then run again.");
            return (int)AIWorkflowRunnerExitCode.RunAlreadyActive;
        }

        if (args.Any(IsHelpOption))
        {
            PrintUsage();
            return (int)AIWorkflowRunnerExitCode.Success;
        }

        if (args.Any(IsSelfCheckOption))
        {
            return RunSelfCheck();
        }

        if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            Console.Error.WriteLine("Missing required argument: sourceFilePath");
            PrintUsage();
            return (int)AIWorkflowRunnerExitCode.UsageOrConfigError;
        }

        var originalFilePath = Path.GetFullPath(args[0]);
        var contractEnforcementMode = ResolveContractEnforcementMode();
        var allowLocalTypeEvolution = ResolveAllowLocalTypeEvolution();
        if (!TryParseOptions(
                args,
                out var explicitBeyondComparePath,
                out var explicitObservedRoot,
                out var refreshOnly,
                out var refresh,
                out var prune,
                out var openTelemetryWindow,
                out var ledgerSummary))
        {
            PrintUsage();
            return (int)AIWorkflowRunnerExitCode.UsageOrConfigError;
        }

        if (!ValidateRuntimeConfiguration(explicitObservedRoot))
        {
            return (int)AIWorkflowRunnerExitCode.UsageOrConfigError;
        }

        if (!File.Exists(originalFilePath))
        {
            Console.Error.WriteLine($"Original file not found: {originalFilePath}");
            PrintUsage();
            return (int)AIWorkflowRunnerExitCode.SourcePathError;
        }

        var appRoot = ResolveAppRoot();
        string observedRoot;
        try
        {
            observedRoot = ResolveObservedRoot(explicitObservedRoot);
        }
        catch
        {
            return (int)AIWorkflowRunnerExitCode.SourcePathError;
        }
        if (!ValidateObservedRootSafety(observedRoot, appRoot))
        {
            return (int)AIWorkflowRunnerExitCode.SourcePathError;
        }
        var observedRootName = new DirectoryInfo(observedRoot).Name;
        var relativeSourcePath = Path.GetRelativePath(observedRoot, originalFilePath);
        if (IsPathOutside(relativeSourcePath))
        {
            Console.Error.WriteLine($"Source file path is not under observed root. Source: {originalFilePath} | ObservedRoot: {observedRoot}");
            return (int)AIWorkflowRunnerExitCode.SourcePathError;
        }

        var workingDir = Path.Combine(appRoot, "Working");
        var historyDir = Path.Combine(workingDir, "History");
        var stateDir = Path.Combine(workingDir, ".state");
        var archiveDir = Path.Combine(workingDir, ArchiveFolderName);
        var runId = $"{DateTime.Now:yyyyMMddTHHmmssfff}-{Environment.ProcessId}-{Guid.NewGuid():N}"[..44];
        // Avoid duplicate telemetry windows in two-step workflows by auto-opening
        // only for compare runs; refresh-only can still force open via --telemetry-window.
        using var telemetrySession = BeginTelemetrySession(historyDir, runId, openTelemetryWindow, allowAutoOpenWindow: !refreshOnly);
        AppendRunDetailLog(historyDir, runId, "run-start", new Dictionary<string, string>
        {
            ["args"] = string.Join(" ", args),
            ["source_file"] = originalFilePath,
            ["app_root"] = appRoot,
            ["observed_root"] = observedRoot,
            ["working_dir"] = workingDir,
            ["history_dir"] = historyDir,
            ["refresh_only"] = refreshOnly.ToString(),
            ["refresh"] = refresh.ToString(),
            ["prune"] = prune.ToString(),
            ["roslyn_validation"] = "true",
            ["telemetry_window"] = openTelemetryWindow.ToString(),
            ["contract_enforcement_mode"] = contractEnforcementMode.ToString(),
            ["allow_local_type_evolution"] = allowLocalTypeEvolution.ToString(),
            ["diff_override"] = explicitBeyondComparePath ?? string.Empty,
            ["ledger_summary"] = ledgerSummary ?? string.Empty
        });
        Console.WriteLine($"Monitor root: {appRoot}");
        Console.WriteLine($"Observed root: {observedRoot}");
        Console.WriteLine($"Working dir: {workingDir}");

        var workingRelativePath = Path.Combine(observedRootName, relativeSourcePath);
        var workingFilePath = Path.Combine(workingDir, workingRelativePath);
        var refreshStatePath = Path.Combine(stateDir, workingRelativePath + ".refresh.state");

        if (refresh || !File.Exists(workingFilePath))
        {
            if (!refresh && !File.Exists(workingFilePath))
            {
                Console.WriteLine("Working copy is missing, so source refresh is required (even with --no-refresh/--compare-only).");
            }

            Directory.CreateDirectory(workingDir);
            Directory.CreateDirectory(stateDir);
            Directory.CreateDirectory(Path.GetDirectoryName(workingFilePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(refreshStatePath)!);
            File.Copy(originalFilePath, workingFilePath, overwrite: true);
            SaveRefreshState(refreshStatePath, originalFilePath);
            Console.WriteLine($"Refreshed working copy from source: {workingFilePath}");
            AppendRunDetailLog(historyDir, runId, "refresh-complete", new Dictionary<string, string>
            {
                ["working_file"] = workingFilePath,
                ["refresh_state"] = refreshStatePath
            });
        }
        else if (!IsRefreshStateCurrent(refreshStatePath, originalFilePath))
        {
            Console.Error.WriteLine("Working copy refresh state is stale or missing. Run with --refresh-only (or --refresh) before compare.");
            AppendRunDetailLog(historyDir, runId, "refresh-state-stale", new Dictionary<string, string>
            {
                ["refresh_state"] = refreshStatePath
            });
            return (int)AIWorkflowRunnerExitCode.RefreshStateStale;
        }
        else if (!refresh)
        {
            Console.WriteLine("Compare-only mode: using existing Working copy (no source refresh).");
        }

        if (refreshOnly)
        {
            Directory.CreateDirectory(historyDir);
            AppendRunLog(historyDir, "refresh-only", originalFilePath, workingFilePath, proposedFilePath: string.Empty, runId);
            AppendRunDetailLog(historyDir, runId, "run-refresh-only", new Dictionary<string, string>
            {
                ["source_file"] = originalFilePath,
                ["working_file"] = workingFilePath
            });
            if (prune)
            {
                var prunedCount = PruneHistoryForSource(historyDir, archiveDir, workingRelativePath, originalFilePath);
                var ledgerPrunedCount = PruneFileLedger(historyDir, workingRelativePath);
                AppendRunLog(historyDir, "prune", originalFilePath, workingFilePath, proposedFilePath: string.Empty, runId);
                Console.WriteLine($"Archived old snapshots: {prunedCount}");
                Console.WriteLine($"Pruned ledger entries: {ledgerPrunedCount}");
                AppendRunDetailLog(historyDir, runId, "prune-complete", new Dictionary<string, string>
                {
                    ["archived_snapshot_count"] = prunedCount.ToString(),
                    ["pruned_ledger_count"] = ledgerPrunedCount.ToString()
                });
            }
            Console.WriteLine("Refresh complete. Skipping compare launch due to --refresh-only.");
            AppendRunDetailLog(historyDir, runId, "run-end", new Dictionary<string, string>
            {
                ["status"] = "refresh-only"
            });
            return (int)AIWorkflowRunnerExitCode.Success;
        }

        // Enforce Roslyn semantic/compile validation before snapshot/diff.
        var validation = ValidateWorkingFileContracts(
            workingFilePath,
            relativeSourcePath,
            observedRoot,
            stateDir,
            contractEnforcementMode,
            allowLocalTypeEvolution);

        AppendRunDetailLog(historyDir, runId, "contract-check", new Dictionary<string, string>
        {
            ["roslyn_available"] = validation.RoslynAvailable.ToString(),
            ["candidate_count"] = validation.CandidateCount.ToString(),
            ["checked_count"] = validation.CheckedCount.ToString(),
            ["ambiguous_count"] = validation.AmbiguousCount.ToString(),
            ["overlay_file_count"] = validation.OverlayFileCount.ToString(),
            ["comment_churn_count"] = validation.CommentChurnCount.ToString(),
            ["violation_count"] = validation.Violations.Count.ToString()
        });

        if (validation.OverlayFileCount > 0)
        {
            Console.WriteLine($"Roslyn overlay: validating with {validation.OverlayFileCount} additional Working .cs file(s).");
        }

        if (validation.CommentChurnCount >= 12)
        {
            Console.WriteLine($"Comment churn warning: {validation.CommentChurnCount} new comment line(s) detected in the current Working file. Keep routine edit history in the ledger/map, not source comments.");
        }

        if (validation.Violations.Count > 0)
        {
            var violationSummary = string.Join(" | ", validation.Violations
                .Take(20)
                .Select(v => $"{v.TypeName}.{v.MemberName}@L{v.Line}: {v.Reason}"));
            AppendRunDetailLog(historyDir, runId, "contract-violations", new Dictionary<string, string>
            {
                ["violations"] = violationSummary
            });
        }

        if (validation.ShouldBlockRun)
        {
            const string contractError = "Roslyn validation failed. The edited working file has unresolved or compile-time errors.";
            Console.Error.WriteLine(contractError);
            var detailLines = BuildViolationDetailLines(validation.Violations, workingFilePath, maxItems: 40);
            if (validation.Violations.Count > 40)
            {
                detailLines.Add($"... plus {validation.Violations.Count - 40} more.");
            }

            var errorFilePath = SurfaceErrorForHuman(historyDir, runId, contractError, detailLines, showDialog: false);
            var continueWithCompare = PromptUserToContinueAfterContractFailure(
                historyDir,
                runId,
                "AIMonitor Contract Warning",
                "Contract validation failed. Continue to compare anyway?",
                errorFilePath);
            AppendRunDetailLog(historyDir, runId, "contract-override-decision", new Dictionary<string, string>
            {
                ["reason"] = "external-contract-validation-failed",
                ["continue_compare"] = continueWithCompare.ToString()
            });
            if (!continueWithCompare)
            {
                AppendRunDetailLog(historyDir, runId, "run-end", new Dictionary<string, string>
                {
                    ["status"] = "contract-check-failed",
                    ["reason"] = "external-contract-validation-failed"
                });
                return (int)AIWorkflowRunnerExitCode.ValidationBlocked;
            }

            Console.WriteLine("User chose to continue compare despite contract validation failures.");
            Console.WriteLine("WARNING: Contract enforcement was overridden by user decision.");
        }

        if (FilesAreIdentical(originalFilePath, workingFilePath))
        {
            Console.WriteLine("No differences found between source and working copy. Skipping compare launch.");
            AppendRunLog(historyDir, "compare-skipped-identical", originalFilePath, workingFilePath, proposedFilePath: string.Empty, runId);
            AppendRunDetailLog(historyDir, runId, "compare-skipped-identical", new Dictionary<string, string>
            {
                ["source_file"] = originalFilePath,
                ["working_file"] = workingFilePath
            });

            if (prune)
            {
                var prunedCount = PruneHistoryForSource(historyDir, archiveDir, workingRelativePath, originalFilePath);
                var ledgerPrunedCount = PruneFileLedger(historyDir, workingRelativePath);
                AppendRunLog(historyDir, "prune", originalFilePath, workingFilePath, proposedFilePath: string.Empty, runId);
                Console.WriteLine($"Archived old snapshots: {prunedCount}");
                Console.WriteLine($"Pruned ledger entries: {ledgerPrunedCount}");
                AppendRunDetailLog(historyDir, runId, "prune-complete", new Dictionary<string, string>
                {
                    ["archived_snapshot_count"] = prunedCount.ToString(),
                    ["pruned_ledger_count"] = ledgerPrunedCount.ToString()
                });
            }

            AppendRunDetailLog(historyDir, runId, "run-end", new Dictionary<string, string>
            {
                ["status"] = "compare-skipped-identical"
            });
            return (int)AIWorkflowRunnerExitCode.NoDifferences;
        }

        var baseName = Path.GetFileNameWithoutExtension(workingFilePath);
        var extension = Path.GetExtension(workingFilePath);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var historyRelativeDir = Path.GetDirectoryName(workingRelativePath) ?? string.Empty;
        var historyTargetDir = Path.Combine(historyDir, historyRelativeDir);
        var proposedFilePath = Path.Combine(historyTargetDir, $"{baseName}_{stamp}{extension}");

        Directory.CreateDirectory(historyDir);
        Directory.CreateDirectory(historyTargetDir);
        File.Copy(workingFilePath, proposedFilePath, overwrite: false);
        AppendFileLedgerEntry(
            historyDir,
            workingRelativePath,
            relativeSourcePath,
            originalFilePath,
            workingFilePath,
            proposedFilePath,
            ledgerSummary);
        AppendRunDetailLog(historyDir, runId, "compare-snapshot-created", new Dictionary<string, string>
        {
            ["proposed_file"] = proposedFilePath,
            ["working_file"] = workingFilePath,
            ["ledger_file"] = GetFileLedgerPath(historyDir, workingRelativePath)
        });

        // Launch the configured diff tool
        bool diffLaunched;
        try
        {
            diffLaunched = LaunchDiffTool(explicitBeyondComparePath, originalFilePath, workingFilePath, proposedFilePath, historyDir, runId);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Diff launch failed: {ex.Message}");
            AppendRunDetailLog(historyDir, runId, "diff-launch-failed", new Dictionary<string, string>
            {
                ["reason"] = "exception",
                ["error"] = ex.Message
            });
            diffLaunched = false;
        }

        if (!diffLaunched)
        {
            AppendRunDetailLog(historyDir, runId, "run-end", new Dictionary<string, string>
            {
                ["status"] = "diff-launch-failed"
            });
            return (int)AIWorkflowRunnerExitCode.DiffLaunchFailed;
        }

        AppendRunLog(historyDir, refresh ? "refresh+compare" : "compare", originalFilePath, workingFilePath, proposedFilePath, runId);
        AppendRunDetailLog(historyDir, runId, refresh ? "run-refresh+compare" : "run-compare", new Dictionary<string, string>
        {
            ["source_file"] = originalFilePath,
            ["working_file"] = workingFilePath,
            ["proposed_file"] = proposedFilePath
        });
        if (prune)
        {
            var prunedCount = PruneHistoryForSource(
                historyDir,
                archiveDir,
                workingRelativePath,
                originalFilePath,
                keepHistoryFilePath: proposedFilePath);
            var ledgerPrunedCount = PruneFileLedger(historyDir, workingRelativePath);
            AppendRunLog(historyDir, "prune", originalFilePath, workingFilePath, proposedFilePath: string.Empty, runId);
            Console.WriteLine($"Archived old snapshots: {prunedCount}");
            Console.WriteLine($"Pruned ledger entries: {ledgerPrunedCount}");
            AppendRunDetailLog(historyDir, runId, "prune-complete", new Dictionary<string, string>
            {
                ["archived_snapshot_count"] = prunedCount.ToString(),
                ["pruned_ledger_count"] = ledgerPrunedCount.ToString()
            });
        }

        Console.WriteLine($"Original: {originalFilePath}");
        Console.WriteLine($"Proposed: {proposedFilePath}");
        AppendRunDetailLog(historyDir, runId, "run-end", new Dictionary<string, string>
        {
            ["status"] = "compare-launched",
            ["original_file"] = originalFilePath,
            ["proposed_file"] = proposedFilePath
        });
        return (int)AIWorkflowRunnerExitCode.Success;
    }

    private static IDisposable? TryAcquireRunLock()
    {
        try
        {
            // Keep runs sequential across processes to avoid telemetry/log file contention
            // and interleaved workflow state changes.
            var lockName = @"Global\AIWorkflowMonitor_WorkflowRunLock";
            var mutex = new Mutex(initiallyOwned: false, name: lockName);
            var acquired = false;
            try
            {
                acquired = mutex.WaitOne(0, exitContext: false);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                mutex.Dispose();
                return null;
            }

            return new RunLockHandle(mutex);
        }
        catch
        {
            return null;
        }
    }

    private sealed class RunLockHandle : IDisposable
    {
        private readonly Mutex _mutex;
        private bool _disposed;

        public RunLockHandle(Mutex mutex)
        {
            _mutex = mutex;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
                // ignore
            }
            _mutex.Dispose();
        }
    }

    private static bool ValidateRuntimeConfiguration(string? explicitObservedRoot)
    {
        var settings = _configuration?.GetSection("WorkflowSettings");
        if (settings is null)
        {
            Console.Error.WriteLine("ERROR: Missing WorkflowSettings section in appsettings.json.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(explicitObservedRoot)
            && string.IsNullOrWhiteSpace(settings["ObservedRoot"]))
        {
            Console.Error.WriteLine("ERROR: ObservedRoot is not configured. Set WorkflowSettings:ObservedRoot in appsettings.json or pass --observed-root <path>.");
            return false;
        }

        var diffTool = settings["DiffTool"];
        if (!string.IsNullOrWhiteSpace(diffTool)
            && !string.Equals(diffTool, "WinMerge", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(diffTool, "BeyondCompare", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(diffTool, "VSCode", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"ERROR: Unknown DiffTool '{diffTool}'. Supported values: WinMerge, BeyondCompare, VSCode.");
            return false;
        }

        var contractMode = settings["ContractEnforcementMode"];
        if (!string.IsNullOrWhiteSpace(contractMode)
            && !string.Equals(contractMode, "Off", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(contractMode, "Warn", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(contractMode, "StrictExternal", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"ERROR: Unknown ContractEnforcementMode '{contractMode}'. Supported values: Off, Warn, StrictExternal.");
            return false;
        }

        return true;
    }

    private static bool LaunchDiffTool(
        string? explicitBeyondComparePath,
        string originalFilePath,
        string workingFilePath,
        string proposedFilePath,
        string historyDir,
        string runId)
    {
        var diffToolConfig = _configuration?.GetSection("WorkflowSettings:DiffTool").Value ?? "WinMerge";
        AppendRunDetailLog(historyDir, runId, "diff-resolve-start", new Dictionary<string, string>
        {
            ["configured_diff_tool"] = diffToolConfig,
            ["explicit_diff_tool"] = explicitBeyondComparePath ?? string.Empty
        });

        // If explicit diff executable path was provided via CLI, use that
        if (!string.IsNullOrWhiteSpace(explicitBeyondComparePath))
        {
            if (!File.Exists(explicitBeyondComparePath))
            {
                Console.Error.WriteLine($"Diff tool not found at: {explicitBeyondComparePath}");
                AppendRunDetailLog(historyDir, runId, "diff-launch-failed", new Dictionary<string, string>
                {
                    ["reason"] = "explicit-tool-not-found",
                    ["tool_path"] = explicitBeyondComparePath
                });
                return false;
            }

            if (IsWinMergeExecutable(explicitBeyondComparePath))
            {
                return LaunchWinMerge(explicitBeyondComparePath, originalFilePath, proposedFilePath, historyDir, runId);
            }
            else
            {
                return LaunchDiffExecutable(explicitBeyondComparePath, originalFilePath, proposedFilePath, "Explicit diff tool", historyDir, runId);
            }
        }

        // Route based on configured tool
        switch (diffToolConfig.ToLowerInvariant())
        {
            case "winmerge":
                var winMergePath = ResolveWinMergePath();
                if (winMergePath == null)
                {
                    Console.Error.WriteLine("WinMerge not found. Install WinMerge or change DiffTool in appsettings.json to 'BeyondCompare' or 'VSCode'");
                    AppendRunDetailLog(historyDir, runId, "diff-launch-failed", new Dictionary<string, string>
                    {
                        ["reason"] = "winmerge-not-found"
                    });
                    return false;
                }
                return LaunchWinMerge(winMergePath, originalFilePath, proposedFilePath, historyDir, runId);
            case "vscode":
                return LaunchVSCode(originalFilePath, proposedFilePath, historyDir, runId);
            case "beyondcompare":
                var bcPath = ResolveBeyondComparePath();
                if (bcPath == null)
                {
                    Console.Error.WriteLine("Beyond Compare not found. Install BC5 or change DiffTool in appsettings.json to 'WinMerge' or 'VSCode'");
                    AppendRunDetailLog(historyDir, runId, "diff-launch-failed", new Dictionary<string, string>
                    {
                        ["reason"] = "beyondcompare-not-found"
                    });
                    return false;
                }
                return LaunchBeyondCompare(bcPath, originalFilePath, proposedFilePath, historyDir, runId);
            default:
                Console.Error.WriteLine($"Unknown DiffTool: {diffToolConfig}. Supported: WinMerge, VSCode, BeyondCompare");
                AppendRunDetailLog(historyDir, runId, "diff-launch-failed", new Dictionary<string, string>
                {
                    ["reason"] = "unknown-diff-tool",
                    ["configured_diff_tool"] = diffToolConfig
                });
                return false;
        }
    }

    private static bool LaunchVSCode(string originalFilePath, string proposedFilePath, string historyDir, string runId)
    {
        var vscodePath = ResolveVSCodePath();
        if (string.IsNullOrWhiteSpace(vscodePath))
        {
            Console.Error.WriteLine("VS Code not found. Install VS Code or change DiffTool in appsettings.json to 'BeyondCompare'");
            AppendRunDetailLog(historyDir, runId, "diff-launch-failed", new Dictionary<string, string>
            {
                ["reason"] = "vscode-not-found"
            });
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = vscodePath,
            UseShellExecute = true
        };
        AddArguments(startInfo, "--diff", originalFilePath, proposedFilePath);

        var process = Process.Start(startInfo);
        Console.WriteLine("VS Code diff viewer launched.");
        AppendRunDetailLog(historyDir, runId, "diff-launched", new Dictionary<string, string>
        {
            ["tool"] = "VSCode",
            ["tool_path"] = vscodePath,
            ["arguments"] = FormatArgumentList(startInfo),
            ["pid"] = process?.Id.ToString() ?? string.Empty
        });
        return process is not null;
    }

    private static bool LaunchBeyondCompare(string beyondComparePath, string originalFilePath, string proposedFilePath, string historyDir, string runId)
    {
        return LaunchDiffExecutable(beyondComparePath, originalFilePath, proposedFilePath, "Beyond Compare", historyDir, runId);
    }

    private static bool LaunchWinMerge(string winMergePath, string originalFilePath, string proposedFilePath, string historyDir, string runId)
    {
        // Force a clean WinMerge session (ignoring persisted prefs) and open
        // new/proposed snapshot on left, existing source file on right.
        var fileName = Path.GetFileName(originalFilePath);
        var relativeHint = Path.GetFileName(Path.GetDirectoryName(originalFilePath) ?? string.Empty);
        var displayName = string.IsNullOrWhiteSpace(relativeHint) ? fileName : $"{relativeHint}\\{fileName}";
        var startInfo = new ProcessStartInfo
        {
            FileName = winMergePath,
            UseShellExecute = true
        };
        AddArguments(
            startInfo,
            "/u",
            "/ignoreeol:1",
            "/dl",
            $"New/Proposed (Working) - {displayName}",
            "/dr",
            $"Existing Source (Right) - {displayName}",
            proposedFilePath,
            originalFilePath);

        var process = Process.Start(startInfo);
        Console.WriteLine("WinMerge launched.");
        AppendRunDetailLog(historyDir, runId, "diff-launched", new Dictionary<string, string>
        {
            ["tool"] = "WinMerge",
            ["tool_path"] = winMergePath,
            ["arguments"] = FormatArgumentList(startInfo),
            ["pid"] = process?.Id.ToString() ?? string.Empty
        });
        return process is not null;
    }

    private static bool IsWinMergeExecutable(string executablePath)
    {
        var fileName = Path.GetFileName(executablePath);
        return fileName.Equals("WinMergeU.exe", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("WinMerge.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ValidateObservedRootSafety(string observedRoot, string appRoot)
    {
        var observedFullPath = TrimDirectorySeparator(Path.GetFullPath(observedRoot));
        var appRootFullPath = TrimDirectorySeparator(Path.GetFullPath(appRoot));
        var workingRoot = TrimDirectorySeparator(Path.Combine(appRootFullPath, "Working"));
        var sampleRoot = TrimDirectorySeparator(Path.Combine(appRootFullPath, "Docs", "Samples"));

        if (string.Equals(observedFullPath, appRootFullPath, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"ERROR: ObservedRoot cannot be the monitor folder: {observedFullPath}");
            return false;
        }

        if (IsSameOrChildPath(observedFullPath, workingRoot))
        {
            Console.Error.WriteLine($"ERROR: ObservedRoot cannot be inside monitor generated state: {observedFullPath}");
            return false;
        }

        if (IsSameOrChildPath(observedFullPath, appRootFullPath)
            && !IsSameOrChildPath(observedFullPath, sampleRoot))
        {
            Console.Error.WriteLine($"ERROR: ObservedRoot cannot be inside the Monitor folder. Use a sibling watched project instead. ObservedRoot: {observedFullPath}");
            return false;
        }

        return true;
    }

    private static bool IsSameOrChildPath(string candidatePath, string rootPath)
    {
        return string.Equals(candidatePath, rootPath, StringComparison.OrdinalIgnoreCase)
            || candidatePath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || candidatePath.StartsWith(rootPath + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimDirectorySeparator(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string? SurfaceErrorForHuman(
        string historyDir,
        string runId,
        string message,
        IEnumerable<string>? detailLines = null,
        bool showDialog = true)
    {
        try
        {
            var errorDir = GetErrorDirectory(historyDir);
            Directory.CreateDirectory(errorDir);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var safeRunId = SanitizeForFileName(runId);
            var errorFilePath = Path.Combine(errorDir, $"_error_{stamp}_{safeRunId}.txt");
            var details = new List<string>
            {
                $"Timestamp: {DateTime.Now:O}",
                $"RunId: {runId}",
                $"Message: {message}"
            };
            if (detailLines is not null)
            {
                var materialized = detailLines.Where(static line => !string.IsNullOrWhiteSpace(line)).ToList();
                if (materialized.Count > 0)
                {
                    details.Add(string.Empty);
                    details.Add("Details:");
                    details.AddRange(materialized);
                }
            }

            File.WriteAllText(errorFilePath, string.Join(Environment.NewLine, details));
            Console.Error.WriteLine($"Error details saved: {errorFilePath}");

            // Show a focused error dialog with details for immediate human visibility.
            if (showDialog)
            {
                try
                {
                    var scriptPath = Path.Combine(errorDir, "_show_error_dialog.ps1");
                    var scriptContent = string.Join(Environment.NewLine, new[]
                    {
                        "param([string]$Title, [string]$Message, [string]$DetailPath)",
                        "Add-Type -AssemblyName System.Windows.Forms",
                        "Add-Type -AssemblyName System.Drawing",
                        "$form = New-Object System.Windows.Forms.Form",
                        "$form.Text = $Title",
                        "$form.StartPosition = 'CenterScreen'",
                        "$form.Width = 900",
                        "$form.Height = 600",
                        "$form.TopMost = $true",
                        "$label = New-Object System.Windows.Forms.Label",
                        "$label.Text = $Message",
                        "$label.ForeColor = [System.Drawing.Color]::DarkRed",
                        "$label.Font = New-Object System.Drawing.Font('Segoe UI', 10, [System.Drawing.FontStyle]::Bold)",
                        "$label.AutoSize = $false",
                        "$label.Left = 12",
                        "$label.Top = 12",
                        "$label.Width = 860",
                        "$label.Height = 44",
                        "$text = New-Object System.Windows.Forms.TextBox",
                        "$text.Multiline = $true",
                        "$text.ScrollBars = 'Vertical'",
                        "$text.ReadOnly = $true",
                        "$text.Font = New-Object System.Drawing.Font('Segoe UI', 11)",
                        "$text.HideSelection = $true",
                        "$text.Left = 12",
                        "$text.Top = 64",
                        "$text.Width = 860",
                        "$text.Height = 450",
                        "if (Test-Path $DetailPath) {",
                        "  $text.Text = Get-Content -Path $DetailPath -Raw",
                        "} else {",
                        "  $text.Text = $Message",
                        "}",
                        "$text.SelectionStart = 0",
                        "$text.SelectionLength = 0",
                        "$btn = New-Object System.Windows.Forms.Button",
                        "$btn.Text = 'OK'",
                        "$btn.Width = 120",
                        "$btn.Height = 32",
                        "$btn.Left = 752",
                        "$btn.Top = 525",
                        "$btn.Add_Click({ $form.Close() })",
                        "$form.Controls.Add($label)",
                        "$form.Controls.Add($text)",
                        "$form.Controls.Add($btn)",
                        "$form.AcceptButton = $btn",
                        "$form.Add_Shown({ $form.Activate() })",
                        "[void]$form.ShowDialog()"
                    });
                    File.WriteAllText(scriptPath, scriptContent, new System.Text.UTF8Encoding(false));

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoLogo -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{scriptPath}\" -Title \"AIMonitor Error\" -Message \"{message.Replace("\"", "'")}\" -DetailPath \"{errorFilePath}\"",
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                }
                catch
                {
                    // Non-fatal: keep pipeline moving even if dialog launch fails.
                }
            }

            return errorFilePath;
        }
        catch
        {
            // Never let error-surfacing failures interfere with core workflow.
            return null;
        }
    }

    private static bool PromptUserToContinueAfterContractFailure(
        string historyDir,
        string runId,
        string title,
        string promptMessage,
        string? detailPath)
    {
        try
        {
            var errorDir = GetErrorDirectory(historyDir);
            Directory.CreateDirectory(errorDir);
            var scriptPath = Path.Combine(errorDir, "_prompt_contract_override.ps1");
            var scriptContent = string.Join(Environment.NewLine, new[]
            {
                "param([string]$Title, [string]$Prompt, [string]$DetailPath)",
                "Add-Type -AssemblyName System.Windows.Forms",
                "Add-Type -AssemblyName System.Drawing",
                "$form = New-Object System.Windows.Forms.Form",
                "$form.Text = $Title",
                "$form.StartPosition = 'CenterScreen'",
                "$form.Width = 980",
                "$form.Height = 680",
                "$form.TopMost = $true",
                "$label = New-Object System.Windows.Forms.Label",
                "$label.Text = $Prompt",
                "$label.ForeColor = [System.Drawing.Color]::DarkRed",
                "$label.Font = New-Object System.Drawing.Font('Segoe UI', 10, [System.Drawing.FontStyle]::Bold)",
                "$label.AutoSize = $false",
                "$label.Left = 12",
                "$label.Top = 12",
                "$label.Width = 940",
                "$label.Height = 44",
                "$text = New-Object System.Windows.Forms.TextBox",
                "$text.Multiline = $true",
                "$text.ScrollBars = 'Vertical'",
                "$text.ReadOnly = $true",
                "$text.Font = New-Object System.Drawing.Font('Segoe UI', 11)",
                "$text.HideSelection = $true",
                "$text.Left = 12",
                "$text.Top = 64",
                "$text.Width = 940",
                "$text.Height = 520",
                "if (-not [string]::IsNullOrWhiteSpace($DetailPath) -and (Test-Path $DetailPath)) {",
                "  $text.Text = Get-Content -Path $DetailPath -Raw",
                "} else {",
                "  $text.Text = $Prompt",
                "}",
                "$text.SelectionStart = 0",
                "$text.SelectionLength = 0",
                "$continue = New-Object System.Windows.Forms.Button",
                "$continue.Text = 'Continue Compare (Yes)'",
                "$continue.Width = 190",
                "$continue.Height = 34",
                "$continue.Left = 562",
                "$continue.Top = 598",
                "$stop = New-Object System.Windows.Forms.Button",
                "$stop.Text = 'Stop Compare (No)'",
                "$stop.Width = 190",
                "$stop.Height = 34",
                "$stop.Left = 762",
                "$stop.Top = 598",
                "$decision = 'No'",
                "$continue.Add_Click({ $script:decision = 'Yes'; $form.Close() })",
                "$stop.Add_Click({ $script:decision = 'No'; $form.Close() })",
                "$form.Add_FormClosing({ if ([string]::IsNullOrWhiteSpace($script:decision)) { $script:decision = 'No' } })",
                "$form.Controls.Add($label)",
                "$form.Controls.Add($text)",
                "$form.Controls.Add($continue)",
                "$form.Controls.Add($stop)",
                "$form.AcceptButton = $continue",
                "$form.CancelButton = $stop",
                "$form.Add_Shown({ $form.Activate() })",
                "[void]$form.ShowDialog()",
                "if ($decision -eq 'Yes') { exit 0 }",
                "exit 1"
            });
            File.WriteAllText(scriptPath, scriptContent, new System.Text.UTF8Encoding(false));

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoLogo -STA -ExecutionPolicy Bypass -File \"{scriptPath}\" -Title \"{title.Replace("\"", "'")}\" -Prompt \"{promptMessage.Replace("\"", "'")}\" -DetailPath \"{(detailPath ?? string.Empty).Replace("\"", "'")}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                AppendRunDetailLog(historyDir, runId, "contract-override-prompt", new Dictionary<string, string>
                {
                    ["status"] = "failed-to-start",
                    ["default_decision"] = "stop-compare"
                });
                return false;
            }

            process.WaitForExit();
            var continueCompare = process.ExitCode == 0;
            AppendRunDetailLog(historyDir, runId, "contract-override-prompt", new Dictionary<string, string>
            {
                ["status"] = "completed",
                ["exit_code"] = process.ExitCode.ToString(),
                ["continue_compare"] = continueCompare.ToString()
            });
            return continueCompare;
        }
        catch (Exception ex)
        {
            AppendRunDetailLog(historyDir, runId, "contract-override-prompt", new Dictionary<string, string>
            {
                ["status"] = "exception",
                ["error"] = ex.Message,
                ["default_decision"] = "stop-compare"
            });
            return false;
        }
    }

    private static string SanitizeForFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "run";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return cleaned.Length > 48 ? cleaned[..48] : cleaned;
    }

    private static List<string> BuildViolationDetailLines(
        List<ContractViolation> violations,
        string workingFilePath,
        int maxItems)
    {
        var details = new List<string>();
        string[]? sourceLines = null;

        try
        {
            sourceLines = File.ReadAllLines(workingFilePath);
        }
        catch
        {
            // Keep going even if source file cannot be read.
        }

        foreach (var violation in violations.Take(maxItems))
        {
            details.Add($"{violation.TypeName}.{violation.MemberName} (Line {violation.Line}) - {violation.Reason}");
            if (!string.IsNullOrWhiteSpace(violation.SourcePath)
                && !string.Equals(Path.GetFullPath(violation.SourcePath), Path.GetFullPath(workingFilePath), StringComparison.OrdinalIgnoreCase))
            {
                details.Add($"File: {violation.SourcePath}");
                continue;
            }

            if (sourceLines is not null
                && violation.Line > 0
                && violation.Line <= sourceLines.Length)
            {
                var codeLine = sourceLines[violation.Line - 1].Trim();
                if (!string.IsNullOrWhiteSpace(codeLine))
                {
                    details.Add($"Code: {codeLine}");
                }
            }
        }

        return details;
    }

    private static bool FilesAreIdentical(string leftPath, string rightPath)
    {
        var leftInfo = new FileInfo(leftPath);
        var rightInfo = new FileInfo(rightPath);
        if (!leftInfo.Exists || !rightInfo.Exists)
        {
            return false;
        }

        if (leftInfo.Length != rightInfo.Length)
        {
            return false;
        }

        const int bufferSize = 1024 * 16;
        var leftBuffer = new byte[bufferSize];
        var rightBuffer = new byte[bufferSize];

        using var left = File.OpenRead(leftPath);
        using var right = File.OpenRead(rightPath);
        while (true)
        {
            var leftRead = left.Read(leftBuffer, 0, leftBuffer.Length);
            var rightRead = right.Read(rightBuffer, 0, rightBuffer.Length);

            if (leftRead != rightRead)
            {
                return false;
            }

            if (leftRead == 0)
            {
                return true;
            }

            for (var i = 0; i < leftRead; i++)
            {
                if (leftBuffer[i] != rightBuffer[i])
                {
                    return false;
                }
            }
        }
    }

    private static bool LaunchDiffExecutable(string executablePath, string originalFilePath, string proposedFilePath, string toolLabel, string historyDir, string runId)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = true
        };
        AddArguments(startInfo, originalFilePath, proposedFilePath);

        var process = Process.Start(startInfo);
        Console.WriteLine($"{toolLabel} launched.");
        AppendRunDetailLog(historyDir, runId, "diff-launched", new Dictionary<string, string>
        {
            ["tool"] = toolLabel,
            ["tool_path"] = executablePath,
            ["arguments"] = FormatArgumentList(startInfo),
            ["pid"] = process?.Id.ToString() ?? string.Empty
        });
        return process is not null;
    }

    private static string FormatArgumentList(ProcessStartInfo startInfo)
    {
        return string.Join(" ", startInfo.ArgumentList.Select(QuoteArgumentForLog));
    }

    private static void AddArguments(ProcessStartInfo startInfo, params string[] arguments)
    {
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
    }

    private static string QuoteArgumentForLog(string argument)
    {
        if (argument.Length == 0)
        {
            return "\"\"";
        }

        return argument.Any(char.IsWhiteSpace)
            ? "\"" + argument.Replace("\"", "\\\"") + "\""
            : argument;
    }

    private static string GetTelemetryDirectory(string historyDir)
    {
        return Path.Combine(historyDir, TelemetryFolderName);
    }

    private static string GetErrorDirectory(string historyDir)
    {
        return Path.Combine(historyDir, ErrorFolderName);
    }
}



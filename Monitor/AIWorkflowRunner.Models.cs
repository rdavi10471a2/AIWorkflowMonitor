namespace AIWorkflowMonitor;

internal static partial class AIWorkflowRunner
{
    private enum ContractEnforcementMode
    {
        Off,
        Warn,
        StrictExternal
    }

    private sealed class ContractValidationResult
    {
        public bool RoslynAvailable { get; init; }
        public bool ShouldBlockRun { get; init; }
        public int CandidateCount { get; init; }
        public int CheckedCount { get; init; }
        public int AmbiguousCount { get; init; }
        public int OverlayFileCount { get; init; }
        public int CommentChurnCount { get; init; }
        public List<ContractViolation> Violations { get; init; } = [];
    }

    private sealed class ContractViolation
    {
        public string SourcePath { get; init; } = string.Empty;
        public string TypeName { get; init; } = string.Empty;
        public string MemberName { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
        public int Line { get; init; }
        public bool IsExternal { get; init; }
    }
}

internal enum AIWorkflowRunnerExitCode
{
    Success = 0,
    UnexpectedFailure = 1,
    UsageOrConfigError = 10,
    SourcePathError = 20,
    RefreshStateStale = 30,
    ValidationBlocked = 40,
    DiffLaunchFailed = 50,
    NoDifferences = 60,
    RunAlreadyActive = 70
}




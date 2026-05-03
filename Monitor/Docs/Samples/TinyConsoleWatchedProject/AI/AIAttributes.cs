using System;

namespace TinyConsoleWatchedProject.AI;

/// <summary>
/// Describes the durable purpose, responsibilities, and local gotchas for a source file.
/// Use this sparingly for files that are edited often, split across partials, or hard to understand from syntax alone.
/// </summary>
[AttributeUsage(AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
public sealed class AIFileContextAttribute : Attribute
{
    public string FileName { get; }
    public string Purpose { get; }
    public string Responsibilities { get; set; } = string.Empty;
    public string Nuances { get; set; } = string.Empty;
    public string RelatedFiles { get; set; } = string.Empty;
    public string LastReviewed { get; set; } = string.Empty;

    public AIFileContextAttribute(string fileName, string purpose)
    {
        FileName = fileName;
        Purpose = purpose;
    }
}

/// <summary>
/// Tracks the source file's human-visible version for monitor-assisted edits.
/// For partial classes, each physical partial file may have its own file-level version.
/// </summary>
[AttributeUsage(AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public sealed class FileVersionAttribute : Attribute
{
    public string Version { get; }

    public FileVersionAttribute(string version)
    {
        Version = version;
    }
}

/// <summary>
/// Optional compact marker for unusual edits that need to remain visible in source.
/// Routine edit history belongs in the monitor-owned file ledger, not in stacked source attributes.
/// </summary>
[AttributeUsage(AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true)]
public sealed class AIChangeAttribute : Attribute
{
    public string Version { get; }
    public string Summary { get; }
    public string Timestamp { get; set; } = string.Empty;

    public AIChangeAttribute(string version, string summary)
    {
        Version = version;
        Summary = summary;
    }
}

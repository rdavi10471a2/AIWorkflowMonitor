namespace AIWorkflowMonitor;

internal static partial class AIWorkflowRunner
{
    private static string ResolveAppRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && current is not null; i++)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return TrimDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
    }

    private static string BuildObservedRootKey(string observedRoot)
    {
        var fullPath = TrimDirectorySeparator(Path.GetFullPath(observedRoot));
        var leafName = new DirectoryInfo(fullPath).Name;
        if (string.IsNullOrWhiteSpace(leafName))
        {
            leafName = "ObservedRoot";
        }

        var normalized = fullPath.ToUpperInvariant();
        var hashBytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized));
        var hash = Convert.ToHexString(hashBytes)[..12].ToLowerInvariant();
        return $"{SanitizeForFileName(leafName)}_{hash}";
    }

    private static bool IsPathOutside(string relativePath)
    {
        return relativePath.StartsWith("..", StringComparison.Ordinal)
            || Path.IsPathRooted(relativePath);
    }

    private static string? ResolveBeyondComparePath()
    {
        foreach (var candidate in BeyondCompareCandidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? ResolveWinMergePath()
    {
        foreach (var candidate in WinMergeCandidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

}



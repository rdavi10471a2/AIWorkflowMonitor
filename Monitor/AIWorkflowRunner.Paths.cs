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

    private static string? ResolveVSCodePath()
    {
        // Try to find VS Code in common locations
        var candidates = new[]
        {
            @"C:\Program Files\Microsoft VS Code\Code.exe",
            @"C:\Program Files (x86)\Microsoft VS Code\Code.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Microsoft VS Code\Code.exe"),
            // Also check if VS Code is in PATH via "code" command
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        foreach (var result in ExecuteCommandLines("where", "code"))
        {
            if (File.Exists(result))
            {
                return result;
            }
        }

        return null;
    }

    private static List<string> ExecuteCommandLines(string command, string args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            process?.WaitForExit(5000);
            if (process?.ExitCode == 0)
            {
                return process.StandardOutput.ReadToEnd()
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
            }
        }
        catch
        {
            // Command not found or failed
        }

        return [];
    }
}



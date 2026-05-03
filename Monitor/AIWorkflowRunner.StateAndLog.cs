namespace AIWorkflowMonitor;

internal static partial class AIWorkflowRunner
{
    private const int RunLogEntryRetentionLimit = 500;

    private static readonly System.Text.Json.JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true
    };

    private sealed class RunLogSession
    {
        public string LogPath { get; init; } = string.Empty;
        public int Sequence;
    }

    private static readonly Dictionary<string, RunLogSession> RunLogSessions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object RunLogSessionsSync = new();

    private static RunLogSession GetRunLogSession(string historyDir, string runId)
    {
        lock (RunLogSessionsSync)
        {
            if (RunLogSessions.TryGetValue(runId, out var existing))
            {
                return existing;
            }

            Directory.CreateDirectory(historyDir);
            var created = new RunLogSession
            {
                LogPath = Path.Combine(historyDir, "_runs.json"),
                Sequence = 0
            };
            RunLogSessions[runId] = created;
            return created;
        }
    }

    private static void CompleteRunLogSession(string runId)
    {
        lock (RunLogSessionsSync)
        {
            RunLogSessions.Remove(runId);
        }
    }

    private static void AppendRunEntry(string historyDir, string runId, string entryType, Dictionary<string, string> fields)
    {
        var session = GetRunLogSession(historyDir, runId);
        var seq = Interlocked.Increment(ref session.Sequence);
        var payload = new Dictionary<string, object?>
        {
            ["timestamp_local"] = DateTime.Now.ToString("O"),
            ["seq"] = seq,
            ["entry_type"] = entryType
        };

        foreach (var pair in fields)
        {
            payload[pair.Key] = pair.Value;
        }

        payload["run_id"] = runId;

        var entries = LoadRunEntries(session.LogPath);
        entries.Add(payload);
        TrimListToLast(entries, RunLogEntryRetentionLimit);
        var json = System.Text.Json.JsonSerializer.Serialize(entries, PrettyJsonOptions);
        WriteAllTextAtomically(session.LogPath, json);
    }

    private static List<Dictionary<string, object?>> LoadRunEntries(string logPath)
    {
        if (!File.Exists(logPath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(logPath);
            var parsed = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(json);
            return parsed ?? [];
        }
        catch
        {
            var backupPath = logPath + ".corrupt_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            File.Move(logPath, backupPath, overwrite: true);
            return [];
        }
    }

    private static void AppendRunDetailLog(string historyDir, string runId, string stage, Dictionary<string, string>? fields = null)
    {
        var payload = new Dictionary<string, string>
        {
            ["stage"] = stage
        };

        if (fields is not null)
        {
            foreach (var pair in fields)
            {
                payload[pair.Key] = pair.Value;
            }
        }

        AppendRunEntry(historyDir, runId, "stage", payload);
        if (string.Equals(stage, "run-end", StringComparison.OrdinalIgnoreCase))
        {
            CompleteRunLogSession(runId);
        }
    }

    private static void SaveRefreshState(string refreshStatePath, string sourceFilePath)
    {
        var sourceInfo = new FileInfo(sourceFilePath);
        var sourceHash = ComputeFileSha256(sourceFilePath);
        var lines = new[]
        {
            $"source={sourceFilePath}",
            $"source_last_write_utc_ticks={sourceInfo.LastWriteTimeUtc.Ticks}",
            $"source_size_bytes={sourceInfo.Length}",
            $"source_sha256={sourceHash}",
            $"refreshed_local={DateTime.Now:O}"
        };
        File.WriteAllLines(refreshStatePath, lines);
    }

    private static bool IsRefreshStateCurrent(string refreshStatePath, string sourceFilePath)
    {
        if (!File.Exists(refreshStatePath) || !File.Exists(sourceFilePath))
        {
            return false;
        }

        try
        {
            var state = File.ReadAllLines(refreshStatePath)
                .Select(static line => line.Split('=', 2))
                .Where(static parts => parts.Length == 2)
                .ToDictionary(static parts => parts[0], static parts => parts[1], StringComparer.OrdinalIgnoreCase);

            if (!state.TryGetValue("source", out var recordedSource)
                || !state.TryGetValue("source_last_write_utc_ticks", out var recordedTicksRaw)
                || !long.TryParse(recordedTicksRaw, out var recordedTicks)
                || !state.TryGetValue("source_size_bytes", out var recordedSizeRaw)
                || !long.TryParse(recordedSizeRaw, out var recordedSize)
                || !state.TryGetValue("source_sha256", out var recordedHash)
                || string.IsNullOrWhiteSpace(recordedHash))
            {
                return false;
            }

            var currentInfo = new FileInfo(sourceFilePath);
            if (currentInfo.LastWriteTimeUtc.Ticks != recordedTicks || currentInfo.Length != recordedSize)
            {
                return false;
            }

            var currentHash = ComputeFileSha256(sourceFilePath);
            return string.Equals(recordedSource, sourceFilePath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(recordedHash, currentHash, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }

    private static void WriteAllTextAtomically(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{path}.tmp_{Environment.ProcessId}_{Guid.NewGuid():N}";
        File.WriteAllText(tempPath, contents, new System.Text.UTF8Encoding(false));

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                File.Move(tempPath, path, overwrite: true);
                return;
            }
            catch when (attempt < 3)
            {
                Thread.Sleep(50 * attempt);
            }
        }

        try
        {
            File.Copy(tempPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private static void AppendRunLog(string historyDir, string operation, string sourceFilePath, string workingFilePath, string proposedFilePath, string runId)
    {
        var payload = new Dictionary<string, string>
        {
            ["operation"] = operation,
            ["source_file"] = sourceFilePath,
            ["working_file"] = workingFilePath,
            ["proposed_file"] = proposedFilePath
        };
        AppendRunEntry(historyDir, runId, "operation", payload);
    }

    private static void TrimListToLast<T>(List<T> items, int keepCount)
    {
        if (keepCount <= 0)
        {
            items.Clear();
            return;
        }

        if (items.Count <= keepCount)
        {
            return;
        }

        items.RemoveRange(0, items.Count - keepCount);
    }
}




namespace AIWorkflowMonitor;

internal static partial class AIWorkflowRunner
{
    private static string GetFileLedgerPath(string historyDir, string workingRelativePath)
    {
        var ledgerDir = Path.Combine(historyDir, LedgersFolderName);
        var safeName = SanitizeForFileName(workingRelativePath.Replace(Path.DirectorySeparatorChar, '_'));
        return Path.Combine(ledgerDir, $"{safeName}.md");
    }

    private static void AppendFileLedgerEntry(
        string historyDir,
        string workingRelativePath,
        string observedRelativePath,
        string originalFilePath,
        string workingFilePath,
        string proposedFilePath,
        string? summary)
    {
        var ledgerPath = GetFileLedgerPath(historyDir, workingRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(ledgerPath)!);

        var entry = new List<string>
        {
            $"## {DateTime.Now:yyyy-MM-dd HH:mm}",
            string.Empty,
            $"File: `{observedRelativePath}`",
            $"Original: `{originalFilePath}`",
            $"Working: `{workingFilePath}`",
            $"Snapshot: `{proposedFilePath}`",
            "ArchiveZip: `not archived`",
            "ArchiveEntry: `not archived`",
            string.Empty,
            string.IsNullOrWhiteSpace(summary)
                ? "Compare snapshot created. Add a concise summary with `--ledger-summary` when Codex has useful context."
                : summary.Trim(),
            string.Empty
        };

        File.AppendAllText(ledgerPath, string.Join(Environment.NewLine, entry));
    }

    private static void UpdateLedgerForArchivedSnapshot(
        string historyDir,
        string workingRelativePath,
        string snapshotPath,
        string archivePath,
        string archiveEntry)
    {
        var ledgerPath = GetFileLedgerPath(historyDir, workingRelativePath);
        if (!File.Exists(ledgerPath))
        {
            return;
        }

        var lines = File.ReadAllLines(ledgerPath).ToList();
        var changed = false;
        for (var i = 0; i < lines.Count; i++)
        {
            if (!string.Equals(lines[i], $"Snapshot: `{snapshotPath}`", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            EnsureArchiveLine(lines, i + 1, "ArchiveZip", archivePath);
            EnsureArchiveLine(lines, i + 2, "ArchiveEntry", archiveEntry);
            changed = true;
        }

        if (changed)
        {
            File.WriteAllLines(ledgerPath, lines);
        }
    }

    private static void EnsureArchiveLine(List<string> lines, int index, string label, string value)
    {
        var line = $"{label}: `{value}`";
        if (index < lines.Count && lines[index].StartsWith($"{label}: ", StringComparison.Ordinal))
        {
            lines[index] = line;
            return;
        }

        lines.Insert(Math.Min(index, lines.Count), line);
    }

    private static int PruneFileLedger(string historyDir, string workingRelativePath)
    {
        var ledgerPath = GetFileLedgerPath(historyDir, workingRelativePath);
        if (!File.Exists(ledgerPath))
        {
            return 0;
        }

        var entries = SplitLedgerEntries(File.ReadAllLines(ledgerPath).ToList());
        var cutoff = DateTime.Now - LedgerRetentionAge;
        var keptEntries = new List<List<string>>();
        var pruned = 0;

        foreach (var entry in entries)
        {
            if (!TryParseLedgerEntryDate(entry, out var entryDate) || entryDate >= cutoff)
            {
                keptEntries.Add(entry);
                continue;
            }

            pruned++;
        }

        if (pruned == 0)
        {
            return 0;
        }

        if (keptEntries.Count == 0)
        {
            File.Delete(ledgerPath);
            return pruned;
        }

        var output = keptEntries.SelectMany(static entry => entry).ToList();
        File.WriteAllLines(ledgerPath, output);
        return pruned;
    }

    private static List<List<string>> SplitLedgerEntries(List<string> lines)
    {
        var entries = new List<List<string>>();
        List<string>? current = null;

        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (current is { Count: > 0 })
                {
                    entries.Add(current);
                }

                current = [line];
                continue;
            }

            current ??= [];
            current.Add(line);
        }

        if (current is { Count: > 0 })
        {
            entries.Add(current);
        }

        return entries;
    }

    private static bool TryParseLedgerEntryDate(List<string> entry, out DateTime date)
    {
        date = default;
        if (entry.Count == 0 || !entry[0].StartsWith("## ", StringComparison.Ordinal))
        {
            return false;
        }

        return DateTime.TryParse(entry[0]["## ".Length..], out date);
    }
}

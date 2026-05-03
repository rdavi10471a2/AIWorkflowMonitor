using System.IO.Compression;

namespace AIWorkflowMonitor;

internal static partial class AIWorkflowRunner
{
    private static int PruneHistoryForSource(
        string historyDir,
        string archiveDir,
        string workingRelativePath,
        string sourceFilePath,
        string? keepHistoryFilePath = null)
    {
        var sourceDirRelative = Path.GetDirectoryName(workingRelativePath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(sourceFilePath);
        var extension = Path.GetExtension(sourceFilePath);
        var historySourceDir = Path.Combine(historyDir, sourceDirRelative);
        if (!Directory.Exists(historySourceDir))
        {
            return 0;
        }

        var cutoffUtc = DateTime.UtcNow - PruneAge;
        var searchPattern = $"{baseName}_*{extension}";
        var files = Directory.EnumerateFiles(historySourceDir, searchPattern, SearchOption.TopDirectoryOnly)
            .Where(path => File.GetLastWriteTimeUtc(path) < cutoffUtc)
            .Where(path => keepHistoryFilePath == null || !string.Equals(path, keepHistoryFilePath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (files.Count == 0)
        {
            return 0;
        }

        Directory.CreateDirectory(archiveDir);
        var archived = 0;
        foreach (var file in files)
        {
            var archivePath = GetSnapshotArchivePath(archiveDir, File.GetLastWriteTime(file));
            var entryRelative = Path.Combine(sourceDirRelative, Path.GetFileName(file))
                .Replace(Path.DirectorySeparatorChar, '/');
            var archiveEntry = AddFileToArchive(archivePath, file, entryRelative);
            UpdateLedgerForArchivedSnapshot(historyDir, workingRelativePath, file, archivePath, archiveEntry);
            File.Delete(file);
            archived++;
        }

        return archived;
    }

    private static string GetSnapshotArchivePath(string archiveDir, DateTime snapshotTime)
    {
        var archiveName = $"history_{snapshotTime:yyyyMMdd}.zip";
        return Path.Combine(archiveDir, archiveName);
    }

    private static string AddFileToArchive(string archivePath, string sourcePath, string entryRelativePath)
    {
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update);
        var entryName = EnsureUniqueArchiveEntryName(archive, entryRelativePath);
        archive.CreateEntryFromFile(sourcePath, entryName, CompressionLevel.Optimal);
        return entryName;
    }

    private static string EnsureUniqueArchiveEntryName(ZipArchive archive, string entryName)
    {
        if (archive.GetEntry(entryName) is null)
        {
            return entryName;
        }

        var dir = Path.GetDirectoryName(entryName)?.Replace(Path.DirectorySeparatorChar, '/') ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(entryName);
        var ext = Path.GetExtension(entryName);
        var suffix = DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");
        var uniqueName = $"{name}_archived_{suffix}{ext}";
        return string.IsNullOrWhiteSpace(dir) ? uniqueName : $"{dir}/{uniqueName}";
    }
}

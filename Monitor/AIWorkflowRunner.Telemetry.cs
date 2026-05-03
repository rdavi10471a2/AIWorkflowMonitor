using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AIWorkflowMonitor;

internal static partial class AIWorkflowRunner
{
    private const int TelemetryRunRetentionLimit = 50;
    private const int TelemetryLineRetentionLimit = 300;

    private static readonly JsonSerializerOptions PrettyTelemetryJsonOptions = new()
    {
        WriteIndented = true
    };

    private static IDisposable BeginTelemetrySession(string historyDir, string runId, bool forceOpenWindow, bool allowAutoOpenWindow)
    {
        var telemetryEnabled = ResolveTelemetryEnabled();
        if (!telemetryEnabled)
        {
            return NoopDisposable.Instance;
        }

        var telemetryDir = GetTelemetryDirectory(historyDir);
        Directory.CreateDirectory(telemetryDir);
        var runLabel = BuildShortRunLabel(runId);
        var telemetryPath = Path.Combine(telemetryDir, "_telemetry.log");
        var telemetryJsonPath = Path.Combine(telemetryDir, "_telemetry.json");
        var telemetryScreenPath = Path.Combine(telemetryDir, $"_telemetry.{SanitizeFileSegment(runLabel)}.screen.log");

        var fileWriter = new StreamWriter(telemetryPath, append: true, encoding: new UTF8Encoding(false))
        {
            AutoFlush = true
        };
        var screenWriter = new StreamWriter(telemetryScreenPath, append: false, encoding: new UTF8Encoding(false))
        {
            AutoFlush = true
        };

        var outPrefix = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [run:{runId}] [OUT] ";
        var errPrefix = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [run:{runId}] [ERR] ";
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        var entries = new List<TelemetryLineEntry>();
        var sync = new object();
        var sequence = 0;
        void Capture(string stream, string message)
        {
            lock (sync)
            {
                sequence++;
                entries.Add(new TelemetryLineEntry
                {
                    Seq = sequence,
                    TimeLocal = DateTime.Now.ToString("HH:mm:ss.fff"),
                    Stream = stream,
                    Message = message
                });
            }
        }

        var startedLocal = DateTime.Now.ToString("O");
        var teeOut = new TeeTextWriter(originalOut, fileWriter, screenWriter, outPrefix, "OUT", Capture);
        var teeErr = new TeeTextWriter(originalErr, fileWriter, screenWriter, errPrefix, "ERR", Capture);
        Console.SetOut(teeOut);
        Console.SetError(teeErr);

        var shouldOpenWindow = forceOpenWindow || (allowAutoOpenWindow && ResolveTelemetryAutoOpenWindow());
        if (shouldOpenWindow)
        {
            TryOpenTelemetryWindow(telemetryScreenPath, runLabel);
        }

        Console.WriteLine(string.Empty);
        Console.WriteLine($"========== RUN {runLabel} START {DateTime.Now:HH:mm:ss} ==========");
        Console.WriteLine($"Telemetry session started. Log: {telemetryPath}");
        Console.WriteLine($"Telemetry JSON: {telemetryJsonPath}");
        Console.WriteLine($"Telemetry Screen: {telemetryScreenPath}");
        return new TelemetrySession(
            originalOut,
            originalErr,
            teeOut,
            teeErr,
            fileWriter,
            screenWriter,
            telemetryJsonPath,
            runId,
            runLabel,
            startedLocal,
            entries,
            sync);
    }

    private static bool ResolveTelemetryEnabled()
    {
        var configured = _configuration?.GetSection("WorkflowSettings")?["TelemetryEnabled"];
        return !bool.TryParse(configured, out var parsed) || parsed;
    }

    private static bool ResolveTelemetryAutoOpenWindow()
    {
        var configured = _configuration?.GetSection("WorkflowSettings")?["TelemetryAutoOpenWindow"];
        return bool.TryParse(configured, out var parsed) && parsed;
    }

    private static void TryOpenTelemetryWindow(string telemetryPath, string runLabel)
    {
        try
        {
            var scriptPath = Path.Combine(Path.GetDirectoryName(telemetryPath) ?? AppContext.BaseDirectory, "_open_telemetry_window.ps1");
            var scriptContent = string.Join(Environment.NewLine, new[]
            {
                "param([string]$TelemetryPath, [string]$RunLabel)",
                "Write-Host \"AIMonitor Telemetry (Run: $RunLabel)\"",
                "Write-Host 'Streaming live telemetry...'",
                "if (-not (Test-Path $TelemetryPath)) {",
                "  Write-Host \"Telemetry file not found: $TelemetryPath\"",
                "  Read-Host 'Press Enter to close'",
                "  exit",
                "}",
                "try {",
                "  $idleSeconds = 3",
                "  $hadError = $false",
                "  function Write-TelemetryLine([string]$line) {",
                "    if ($line -match '\\[ERR\\]' -or $line -match 'ERROR' -or $line -match 'Contract enforcement failed') {",
                "      Write-Host $line -ForegroundColor Red",
                "      $script:hadError = $true",
                "      return",
                "    }",
                "    if ($line -match 'WARNING' -or $line -match 'No differences found') {",
                "      Write-Host $line -ForegroundColor Yellow",
                "      return",
                "    }",
                "    if ($line -match '\\[OUT\\]') {",
                "      Write-Host $line -ForegroundColor Gray",
                "      return",
                "    }",
                "    Write-Host $line",
                "  }",
                "  $job = Start-Job -ScriptBlock { param($p) Get-Content -Path $p -Tail 200 -Wait } -ArgumentList $TelemetryPath",
                "  $lastOutputAt = Get-Date",
                "  $sawOutput = $false",
                "  $promptShown = $false",
                "  while ($true) {",
                "    $chunk = Receive-Job -Job $job -ErrorAction SilentlyContinue",
                "    if ($chunk) {",
                "      $chunk | ForEach-Object { Write-TelemetryLine $_ }",
                "      $lastOutputAt = Get-Date",
                "      $sawOutput = $true",
                "      if ($promptShown) {",
                "        $promptShown = $false",
                "      }",
                "    }",
                "    elseif ($sawOutput -and -not $promptShown) {",
                "      $elapsed = ((Get-Date) - $lastOutputAt).TotalSeconds",
                "      if ($elapsed -ge $idleSeconds) {",
                "        Write-Host ''",
                "        Write-Host 'Run appears complete/idle.'",
                "        Write-Host '>> Press Enter to close window, or wait for next run output.'",
                "        $promptShown = $true",
                "      }",
                "    }",
                "    if ($promptShown -and [Console]::KeyAvailable) {",
                "      $key = [Console]::ReadKey($true)",
                "      if ($key.Key -eq [ConsoleKey]::Enter) {",
                "        Write-Host 'Closing telemetry window...'",
                "        break",
                "      }",
                "    }",
                "    Start-Sleep -Milliseconds 200",
                "  }",
                "}",
                "catch {",
                "  Write-Host \"Telemetry stream ended with error: $($_.Exception.Message)\"",
                "}",
                "finally {",
                "  if ($job) {",
                "    Stop-Job -Job $job -ErrorAction SilentlyContinue | Out-Null",
                "    Remove-Job -Job $job -Force -ErrorAction SilentlyContinue | Out-Null",
                "  }",
                "  Write-Host ''",
                "  if ($hadError) {",
                "    Write-Host 'Errors were detected in this telemetry session.' -ForegroundColor Yellow",
                "  }",
                "  Write-Host 'Telemetry stream ended.'",
                "  Start-Sleep -Milliseconds 300",
                "}"
            });
            File.WriteAllText(scriptPath, scriptContent, new UTF8Encoding(false));

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoLogo -ExecutionPolicy Bypass -File \"{scriptPath}\" -TelemetryPath \"{telemetryPath}\" -RunLabel \"{runLabel}\"",
                UseShellExecute = true
            };

            var process = Process.Start(startInfo);
            if (process is not null && process.HasExited)
            {
                Console.WriteLine("WARNING: Telemetry window process exited immediately.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARNING: Unable to open telemetry window: {ex.Message}");
        }
    }

    private static string BuildShortRunLabel(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return "run";
        }

        var parts = runId.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            var stamp = parts[0];
            var shortStamp = stamp.Length > 6 ? stamp[^6..] : stamp;
            return $"r{shortStamp}-{parts[1]}";
        }

        return runId.Length > 10 ? runId[..10] : runId;
    }

    private static string SanitizeFileSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }

    private static List<TelemetryRunEntry> LoadTelemetryRuns(string telemetryJsonPath)
    {
        if (!File.Exists(telemetryJsonPath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(telemetryJsonPath);
            var parsed = JsonSerializer.Deserialize<List<TelemetryRunEntry>>(json);
            return parsed ?? [];
        }
        catch
        {
            var backupPath = telemetryJsonPath + ".corrupt_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            File.Move(telemetryJsonPath, backupPath, overwrite: true);
            return [];
        }
    }

    private static void PersistTelemetryRun(
        string telemetryJsonPath,
        string runId,
        string runLabel,
        string startedLocal,
        List<TelemetryLineEntry> lines)
    {
        var allRuns = LoadTelemetryRuns(telemetryJsonPath);
        var retainedLines = lines.ToList();
        TrimListToLast(retainedLines, TelemetryLineRetentionLimit);
        var run = new TelemetryRunEntry
        {
            RunLabel = runLabel,
            StartedLocal = startedLocal,
            EndedLocal = DateTime.Now.ToString("O"),
            LineCount = lines.Count,
            RetainedLineCount = retainedLines.Count,
            Lines = retainedLines
        };

        // Keep full id available for traceability, but outside the main display label.
        run.RunId = runId;
        allRuns.Add(run);
        TrimListToLast(allRuns, TelemetryRunRetentionLimit);
        WriteAllTextAtomically(telemetryJsonPath, JsonSerializer.Serialize(allRuns, PrettyTelemetryJsonOptions));
    }

    private sealed class TelemetrySession : IDisposable
    {
        private readonly TextWriter _originalOut;
        private readonly TextWriter _originalErr;
        private readonly TeeTextWriter _teeOut;
        private readonly TeeTextWriter _teeErr;
        private readonly StreamWriter _fileWriter;
        private readonly StreamWriter _screenWriter;
        private readonly string _telemetryJsonPath;
        private readonly string _runId;
        private readonly string _runLabel;
        private readonly string _startedLocal;
        private readonly List<TelemetryLineEntry> _entries;
        private readonly object _sync;
        private bool _disposed;

        public TelemetrySession(
            TextWriter originalOut,
            TextWriter originalErr,
            TeeTextWriter teeOut,
            TeeTextWriter teeErr,
            StreamWriter fileWriter,
            StreamWriter screenWriter,
            string telemetryJsonPath,
            string runId,
            string runLabel,
            string startedLocal,
            List<TelemetryLineEntry> entries,
            object sync)
        {
            _originalOut = originalOut;
            _originalErr = originalErr;
            _teeOut = teeOut;
            _teeErr = teeErr;
            _fileWriter = fileWriter;
            _screenWriter = screenWriter;
            _telemetryJsonPath = telemetryJsonPath;
            _runId = runId;
            _runLabel = runLabel;
            _startedLocal = startedLocal;
            _entries = entries;
            _sync = sync;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Console.WriteLine($"========== RUN {_runLabel} END {DateTime.Now:HH:mm:ss} ==========");
            Console.Out.Flush();
            Console.Error.Flush();
            Console.SetOut(_originalOut);
            Console.SetError(_originalErr);
            _teeOut.Dispose();
            _teeErr.Dispose();
            _fileWriter.Dispose();
            _screenWriter.Dispose();

            lock (_sync)
            {
                PersistTelemetryRun(_telemetryJsonPath, _runId, _runLabel, _startedLocal, _entries.ToList());
            }
        }
    }

    private sealed class TeeTextWriter : TextWriter
    {
        private readonly TextWriter _primary;
        private readonly TextWriter _secondary;
        private readonly TextWriter _screen;
        private readonly string _linePrefix;
        private readonly string _stream;
        private readonly Action<string, string> _onLine;
        private const int MaxScreenWidth = 120;

        public TeeTextWriter(
            TextWriter primary,
            TextWriter secondary,
            TextWriter screen,
            string linePrefix,
            string stream,
            Action<string, string> onLine)
        {
            _primary = primary;
            _secondary = secondary;
            _screen = screen;
            _linePrefix = linePrefix;
            _stream = stream;
            _onLine = onLine;
        }

        public override Encoding Encoding => _primary.Encoding;

        public override void WriteLine(string? value)
        {
            var message = value ?? string.Empty;
            _primary.WriteLine(value);
            _secondary.WriteLine(_linePrefix + message);
            _secondary.Flush();
            foreach (var line in FormatForScreenLines(_stream, message))
            {
                _screen.WriteLine(line);
            }
            _screen.Flush();
            _onLine(_stream, message);
        }

        private static IEnumerable<string> FormatForScreenLines(string stream, string message)
        {
            var compact = message;
            if (compact.StartsWith("Source map refreshed:", StringComparison.OrdinalIgnoreCase))
            {
                compact = $"Source map refreshed ({CountCsvItems(compact)} files).";
            }
            else if (compact.StartsWith("Source map unchanged:", StringComparison.OrdinalIgnoreCase))
            {
                compact = $"Source map unchanged ({CountCsvItems(compact)} files).";
            }
            else if (compact.StartsWith("Refreshed working copy from source:", StringComparison.OrdinalIgnoreCase))
            {
                var path = compact["Refreshed working copy from source:".Length..].Trim();
                compact = $"Refreshed working copy: {Path.GetFileName(path)}";
            }
            else if (compact.StartsWith("Original:", StringComparison.OrdinalIgnoreCase)
                || compact.StartsWith("Proposed:", StringComparison.OrdinalIgnoreCase))
            {
                var sep = compact.IndexOf(':');
                if (sep > 0)
                {
                    var label = compact[..sep];
                    var path = compact[(sep + 1)..].Trim();
                    compact = $"{label}: {Path.GetFileName(path)}";
                }
            }

            var firstPrefix = $"{DateTime.Now:HH:mm:ss} [{stream}] ";
            var continuationPrefix = $"         [{stream}] ";
            var firstWidth = Math.Max(20, MaxScreenWidth - firstPrefix.Length);
            var continuationWidth = Math.Max(20, MaxScreenWidth - continuationPrefix.Length);

            var chunks = WrapText(compact, firstWidth, continuationWidth).ToList();
            if (chunks.Count == 0)
            {
                yield return firstPrefix.TrimEnd();
                yield break;
            }

            yield return firstPrefix + chunks[0];
            for (var i = 1; i < chunks.Count; i++)
            {
                yield return continuationPrefix + chunks[i];
            }
        }

        private static int CountCsvItems(string message)
        {
            var sep = message.IndexOf(':');
            if (sep < 0 || sep == message.Length - 1)
            {
                return 0;
            }

            return message[(sep + 1)..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Length;
        }

        private static IEnumerable<string> WrapText(string text, int firstWidth, int continuationWidth)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                yield break;
            }

            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var current = new StringBuilder();
            var targetWidth = firstWidth;

            foreach (var word in words)
            {
                if (current.Length == 0)
                {
                    if (word.Length <= targetWidth)
                    {
                        current.Append(word);
                        continue;
                    }

                    foreach (var part in BreakLongWord(word, targetWidth))
                    {
                        if (part.Length == targetWidth)
                        {
                            yield return part;
                            targetWidth = continuationWidth;
                        }
                        else
                        {
                            current.Append(part);
                        }
                    }
                    continue;
                }

                if (current.Length + 1 + word.Length <= targetWidth)
                {
                    current.Append(' ').Append(word);
                    continue;
                }

                yield return current.ToString();
                current.Clear();
                targetWidth = continuationWidth;

                if (word.Length <= targetWidth)
                {
                    current.Append(word);
                }
                else
                {
                    foreach (var part in BreakLongWord(word, targetWidth))
                    {
                        if (part.Length == targetWidth)
                        {
                            yield return part;
                        }
                        else
                        {
                            current.Append(part);
                        }
                    }
                }
            }

            if (current.Length > 0)
            {
                yield return current.ToString();
            }
        }

        private static IEnumerable<string> BreakLongWord(string word, int width)
        {
            var start = 0;
            while (start < word.Length)
            {
                var len = Math.Min(width, word.Length - start);
                yield return word.Substring(start, len);
                start += len;
            }
        }

        protected override void Dispose(bool disposing)
        {
            // Do not dispose Console's underlying writer.
            base.Dispose(disposing);
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose()
        {
        }
    }

    private sealed class TelemetryRunEntry
    {
        public string RunLabel { get; init; } = string.Empty;
        public string RunId { get; set; } = string.Empty;
        public string StartedLocal { get; init; } = string.Empty;
        public string EndedLocal { get; init; } = string.Empty;
        public int LineCount { get; init; }
        public int RetainedLineCount { get; init; }
        public List<TelemetryLineEntry> Lines { get; init; } = [];
    }

    private sealed class TelemetryLineEntry
    {
        public int Seq { get; init; }
        public string TimeLocal { get; init; } = string.Empty;
        public string Stream { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
    }
}



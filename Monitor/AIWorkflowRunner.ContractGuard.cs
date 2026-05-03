using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AIWorkflowMonitor;

internal static partial class AIWorkflowRunner
{
    private static readonly HashSet<string> RoslynIgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        "bin",
        "obj",
        "node_modules",
        "packages",
        "Working",
        "archive",
        "Archive"
    };

    private static ContractValidationResult ValidateWorkingFileContracts(
        string workingFilePath,
        string observedRelativeSourcePath,
        string observedRoot,
        string stateDir,
        ContractEnforcementMode mode,
        bool allowLocalTypeEvolution)
    {
        _ = stateDir; // Kept for signature compatibility across partial files.

        if (mode == ContractEnforcementMode.Off)
        {
            return new ContractValidationResult
            {
                RoslynAvailable = true
            };
        }

        if (!TryCreateRoslynValidationContext(
                workingFilePath,
                observedRelativeSourcePath,
                observedRoot,
                stateDir,
                out var compilation,
                out var workingTree,
                out var workingTreePath,
                out var overlayTreePaths,
                out var semanticModel,
                out var setupError))
        {
            return new ContractValidationResult
            {
                RoslynAvailable = false,
                ShouldBlockRun = mode == ContractEnforcementMode.StrictExternal,
                Violations =
                [
                    new ContractViolation
                    {
                        TypeName = "<semantic-analysis>",
                        MemberName = "Compilation",
                        Line = 0,
                        IsExternal = true,
                        Reason = setupError
                    }
                ]
            };
        }

        var violations = new List<ContractViolation>();
        var localTypeNames = GetLocalTypeNames(workingFilePath);
        var diagnosticTreePaths = overlayTreePaths
            .Append(workingTreePath)
            .Append(workingFilePath)
            .Select(path => Path.GetFullPath(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidateCount = 0;
        var checkedCount = 0;
        var ambiguousCount = 0;
        var commentChurnCount = CountAddedCommentLines(workingTreePath, workingFilePath);

        var diagnostics = compilation.GetDiagnostics();
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity != DiagnosticSeverity.Error)
            {
                continue;
            }

            if (!diagnostic.Location.IsInSource)
            {
                continue;
            }

            var diagnosticPath = diagnostic.Location.GetLineSpan().Path;
            if (!diagnosticTreePaths.Contains(Path.GetFullPath(diagnosticPath)))
            {
                continue;
            }

            var line = diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1;
            violations.Add(new ContractViolation
            {
                SourcePath = diagnosticPath,
                TypeName = "<compiler>",
                MemberName = diagnostic.Id,
                Line = line,
                IsExternal = true,
                Reason = diagnostic.GetMessage()
            });
        }

        var root = workingTree.GetCompilationUnitRoot();
        var references = new Dictionary<string, ReferencedMemberCandidate>(StringComparer.Ordinal);

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                continue;
            }

            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            var symbol = symbolInfo.Symbol as IMethodSymbol;
            if (symbol is not null && symbol.ContainingType is not null)
            {
                checkedCount++;
                continue;
            }

            if (!TryGetSemanticTypeCandidate(semanticModel, memberAccess.Expression, out var typeCandidate))
            {
                typeCandidate = "<unknown>";
            }

            AddCandidate(
                references,
                workingTree,
                memberAccess.Name,
                typeCandidate,
                memberAccess.Name.Identifier.ValueText,
                MapResolutionState(symbolInfo));
        }

        foreach (var memberAccess in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (memberAccess.Parent is InvocationExpressionSyntax)
            {
                continue;
            }

            var symbolInfo = semanticModel.GetSymbolInfo(memberAccess);
            if (symbolInfo.Symbol is not null)
            {
                checkedCount++;
                continue;
            }

            if (!TryGetSemanticTypeCandidate(semanticModel, memberAccess.Expression, out var typeCandidate))
            {
                typeCandidate = "<unknown>";
            }

            AddCandidate(
                references,
                workingTree,
                memberAccess.Name,
                typeCandidate,
                memberAccess.Name.Identifier.ValueText,
                MapResolutionState(symbolInfo));
        }

        foreach (var candidate in references.Values)
        {
            candidateCount++;
            var shortTypeName = GetShortTypeName(candidate.TypeCandidate);
            if (allowLocalTypeEvolution && localTypeNames.Contains(shortTypeName))
            {
                continue;
            }

            if (candidate.ResolutionState == MemberResolutionState.Resolved)
            {
                continue;
            }

            var reason = candidate.ResolutionState == MemberResolutionState.Ambiguous
                ? "Reference could not be bound unambiguously to a single symbol."
                : "Reference could not be bound to a known symbol.";
            if (candidate.ResolutionState == MemberResolutionState.Ambiguous)
            {
                ambiguousCount++;
            }

            violations.Add(new ContractViolation
            {
                SourcePath = workingTreePath,
                TypeName = candidate.TypeCandidate,
                MemberName = candidate.MemberName,
                Line = candidate.Line,
                IsExternal = true,
                Reason = reason
            });
        }

        var dedupedViolations = violations
            .GroupBy(static v => $"{v.TypeName}|{v.MemberName}|{v.Line}|{v.Reason}", StringComparer.Ordinal)
            .Select(static g => g.First())
            .ToList();

        var shouldBlock = mode == ContractEnforcementMode.StrictExternal && dedupedViolations.Count > 0;
        return new ContractValidationResult
        {
            RoslynAvailable = true,
            ShouldBlockRun = shouldBlock,
            CandidateCount = candidateCount,
            CheckedCount = checkedCount,
            AmbiguousCount = ambiguousCount,
            OverlayFileCount = overlayTreePaths.Count,
            CommentChurnCount = commentChurnCount,
            Violations = dedupedViolations
        };
    }

    private static int CountAddedCommentLines(string sourceFilePath, string workingFilePath)
    {
        try
        {
            if (!File.Exists(sourceFilePath) || !File.Exists(workingFilePath))
            {
                return 0;
            }

            var originalComments = File.ReadLines(sourceFilePath)
                .Select(NormalizeCommentLine)
                .Where(static line => line.Length > 0)
                .GroupBy(static line => line, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

            var addedCount = 0;
            foreach (var line in File.ReadLines(workingFilePath).Select(NormalizeCommentLine))
            {
                if (line.Length == 0)
                {
                    continue;
                }

                if (originalComments.TryGetValue(line, out var count) && count > 0)
                {
                    originalComments[line] = count - 1;
                    continue;
                }

                addedCount++;
            }

            return addedCount;
        }
        catch
        {
            return 0;
        }
    }

    private static string NormalizeCommentLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("/*", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal)
            || trimmed.StartsWith("*/", StringComparison.Ordinal)
            || trimmed.StartsWith("///", StringComparison.Ordinal))
        {
            return trimmed;
        }

        return string.Empty;
    }

    private static bool TryCreateRoslynValidationContext(
        string workingFilePath,
        string observedRelativeSourcePath,
        string observedRoot,
        string stateDir,
        out CSharpCompilation compilation,
        out SyntaxTree workingTree,
        out string workingTreePath,
        out List<string> overlayTreePaths,
        out SemanticModel semanticModel,
        out string error)
    {
        try
        {
            var trees = new List<SyntaxTree>();
            overlayTreePaths = [];
            var parseOptions = new CSharpParseOptions(
                languageVersion: LanguageVersion.Latest,
                kind: SourceCodeKind.Regular,
                documentationMode: DocumentationMode.Parse);
            var observedRootFullPath = Path.GetFullPath(observedRoot);
            var observedRelativeNormalized = NormalizePath(observedRelativeSourcePath);
            var observedRelativeForPath = observedRelativeSourcePath
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            workingTreePath = Path.GetFullPath(Path.Combine(observedRootFullPath, observedRelativeForPath));
            var workingOverlayRoot = ResolveWorkingOverlayRoot(workingFilePath, observedRelativeForPath);
            var workingOverlayPaths = BuildWorkingOverlayMap(
                workingOverlayRoot,
                observedRootFullPath,
                stateDir,
                observedRelativeNormalized,
                workingFilePath);

            foreach (var sourcePath in EnumerateObservedSourceFiles(observedRootFullPath))
            {
                var relative = NormalizePath(Path.GetRelativePath(observedRootFullPath, sourcePath));
                if (string.Equals(relative, observedRelativeNormalized, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var treePath = Path.GetFullPath(sourcePath);
                var textPath = sourcePath;
                if (workingOverlayPaths.TryGetValue(relative, out var overlayPath))
                {
                    textPath = overlayPath;
                    overlayTreePaths.Add(treePath);
                }

                var text = File.ReadAllText(textPath);
                trees.Add(CSharpSyntaxTree.ParseText(text, parseOptions, treePath));
                workingOverlayPaths.Remove(relative);
            }

            foreach (var overlay in workingOverlayPaths)
            {
                var treePath = Path.GetFullPath(Path.Combine(
                    observedRootFullPath,
                    overlay.Key.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)));
                var text = File.ReadAllText(overlay.Value);
                trees.Add(CSharpSyntaxTree.ParseText(text, parseOptions, treePath));
                overlayTreePaths.Add(treePath);
            }

            var workingText = File.ReadAllText(workingFilePath);
            // Use observed-root path as Roslyn tree path so resolution/diagnostics remain
            // anchored to the real source layout while content still comes from Working copy.
            workingTree = CSharpSyntaxTree.ParseText(workingText, parseOptions, workingTreePath);
            trees.Add(workingTree);
            trees.Add(BuildImplicitGlobalUsingsTree(parseOptions, observedRootFullPath, stateDir));

            var references = GetTrustedPlatformReferences();
            compilation = CSharpCompilation.Create(
                assemblyName: "AIMonitorRoslynContractCheck",
                syntaxTrees: trees,
                references: references,
                options: new CSharpCompilationOptions(OutputKind.ConsoleApplication));

            semanticModel = compilation.GetSemanticModel(workingTree, ignoreAccessibility: true);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            compilation = CSharpCompilation.Create("AIMonitorRoslynContractCheck.Empty");
            workingTreePath = workingFilePath;
            overlayTreePaths = [];
            workingTree = CSharpSyntaxTree.ParseText(string.Empty, path: workingTreePath);
            semanticModel = compilation.GetSemanticModel(workingTree, ignoreAccessibility: true);
            error = $"Semantic analysis could not be completed: {ex.Message}";
            return false;
        }
    }

    private static string ResolveWorkingOverlayRoot(string workingFilePath, string observedRelativeSourcePath)
    {
        var workingFullPath = Path.GetFullPath(workingFilePath);
        var relativeForPath = observedRelativeSourcePath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (workingFullPath.EndsWith(relativeForPath, StringComparison.OrdinalIgnoreCase))
        {
            return workingFullPath[..^relativeForPath.Length].TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }

        return Path.GetDirectoryName(workingFullPath) ?? workingFullPath;
    }

    private static Dictionary<string, string> BuildWorkingOverlayMap(
        string workingOverlayRoot,
        string observedRootFullPath,
        string stateDir,
        string currentObservedRelativePath,
        string currentWorkingFilePath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(workingOverlayRoot))
        {
            return result;
        }

        var observedRootKey = BuildObservedRootKey(observedRootFullPath);
        var currentWorkingFullPath = Path.GetFullPath(currentWorkingFilePath);
        foreach (var overlayPath in EnumerateWorkingOverlaySourceFiles(workingOverlayRoot))
        {
            var overlayFullPath = Path.GetFullPath(overlayPath);
            if (string.Equals(overlayFullPath, currentWorkingFullPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = NormalizePath(Path.GetRelativePath(workingOverlayRoot, overlayFullPath));
            if (string.Equals(relative, currentObservedRelativePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var observedPath = Path.Combine(
                observedRootFullPath,
                relative.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
            if (File.Exists(observedPath))
            {
                var refreshStatePath = Path.Combine(
                    stateDir,
                    observedRootKey,
                    relative.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar) + ".refresh.state");
                if (!IsRefreshStateCurrent(refreshStatePath, observedPath))
                {
                    continue;
                }
            }

            result[relative] = overlayFullPath;
        }

        return result;
    }

    private static IEnumerable<string> EnumerateWorkingOverlaySourceFiles(string workingOverlayRoot)
    {
        var pending = new Stack<string>();
        pending.Push(workingOverlayRoot);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var dir in Directory.EnumerateDirectories(current))
            {
                var name = Path.GetFileName(dir);
                if (RoslynIgnoredDirectories.Contains(name)
                    || string.Equals(name, ".state", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, LedgersFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                pending.Push(dir);
            }

            foreach (var file in Directory.EnumerateFiles(current, "*.cs"))
            {
                yield return file;
            }
        }
    }

    private static IEnumerable<string> EnumerateObservedSourceFiles(string observedRoot)
    {
        var pending = new Stack<string>();
        pending.Push(observedRoot);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var dir in Directory.EnumerateDirectories(current))
            {
                var name = Path.GetFileName(dir);
                if (RoslynIgnoredDirectories.Contains(name))
                {
                    continue;
                }

                pending.Push(dir);
            }

            foreach (var file in Directory.EnumerateFiles(current, "*.cs"))
            {
                yield return file;
            }
        }
    }

    private static SyntaxTree BuildImplicitGlobalUsingsTree(CSharpParseOptions parseOptions, string observedRootFullPath, string stateDir)
    {
        // Roslyn validation runs outside the target project's full MSBuild context.
        // Load VS/MSBuild-generated global usings when available, then merge safe defaults.
        var usings = new HashSet<string>(StringComparer.Ordinal)
        {
            "global using System;",
            "global using System.Collections.Generic;",
            "global using System.ComponentModel;",
            "global using System.IO;",
            "global using System.Linq;",
            "global using System.Net.Http;",
            "global using System.Threading;",
            "global using System.Threading.Tasks;"
        };

        foreach (var generatedGlobalUsing in LoadGeneratedGlobalUsings(observedRootFullPath))
        {
            usings.Add(generatedGlobalUsing);
        }

        var orderedUsings = usings
            .OrderBy(static u => u, StringComparer.Ordinal)
            .ToList();
        var text = string.Join(Environment.NewLine, orderedUsings);
        PersistEffectiveGlobalUsings(stateDir, observedRootFullPath, orderedUsings);
        return CSharpSyntaxTree.ParseText(text, parseOptions, path: "<AIMonitor_ImplicitGlobalUsings.g.cs>");
    }

    private static IEnumerable<string> LoadGeneratedGlobalUsings(string observedRootFullPath)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var path in DiscoverGeneratedGlobalUsingFiles(observedRootFullPath))
            {
                foreach (var line in File.ReadLines(path))
                {
                    var trimmed = line.Trim();
                    if (!trimmed.StartsWith("global using ", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!trimmed.EndsWith(';'))
                    {
                        trimmed += ";";
                    }

                    result.Add(trimmed);
                }
            }
        }
        catch
        {
            // Best-effort only. Default using set above still keeps validation functional.
        }

        return result;
    }

    private static IEnumerable<string> DiscoverGeneratedGlobalUsingFiles(string observedRootFullPath)
    {
        var allCandidates = Directory.EnumerateFiles(observedRootFullPath, "*GlobalUsings.g.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // VS/MSBuild may leave multiple generated files across target frameworks/configurations.
        // Keep the newest file per generated filename so Roslyn validation tracks the most recent build context.
        return allCandidates
            .GroupBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
                group.OrderByDescending(static p => File.GetLastWriteTimeUtc(p))
                    .First())
            .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void PersistEffectiveGlobalUsings(string stateDir, string observedRootFullPath, List<string> orderedUsings)
    {
        try
        {
            var roslynStateDir = Path.Combine(stateDir, "roslyn");
            Directory.CreateDirectory(roslynStateDir);
            var path = Path.Combine(roslynStateDir, "AIMonitor.EffectiveGlobalUsings.g.cs");
            var lines = new List<string>
            {
                "// <auto-generated/>",
                $"// observed_root: {observedRootFullPath}",
                $"// generated_local: {DateTime.Now:O}"
            };
            lines.AddRange(orderedUsings);
            File.WriteAllText(path, string.Join(Environment.NewLine, lines), new System.Text.UTF8Encoding(false));
        }
        catch
        {
            // Non-fatal: Roslyn parsing still has an in-memory fallback.
        }
    }

    private static HashSet<string> GetLocalTypeNames(string workingFilePath)
    {
        var sourceText = File.ReadAllText(workingFilePath);
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, path: workingFilePath);
        var root = syntaxTree.GetCompilationUnitRoot();

        return root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Select(static t => t.Identifier.ValueText)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool TryGetSemanticTypeCandidate(SemanticModel model, ExpressionSyntax expression, out string typeCandidate)
    {
        var type = model.GetTypeInfo(expression).Type;
        if (type is not null)
        {
            typeCandidate = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty);
            return true;
        }

        var symbol = model.GetSymbolInfo(expression).Symbol;
        if (symbol is INamedTypeSymbol namedType)
        {
            typeCandidate = namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty);
            return true;
        }

        typeCandidate = string.Empty;
        return false;
    }

    private static List<MetadataReference> GetTrustedPlatformReferences()
    {
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(tpa))
        {
            return [];
        }

        return tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
    }

    private static void AddCandidate(
        Dictionary<string, ReferencedMemberCandidate> candidates,
        SyntaxTree syntaxTree,
        SyntaxNode node,
        string typeCandidate,
        string memberName,
        MemberResolutionState resolutionState)
    {
        var line = syntaxTree.GetLineSpan(node.Span).StartLinePosition.Line + 1;
        var key = $"{typeCandidate}|{memberName}|{line}|{resolutionState}";
        candidates[key] = new ReferencedMemberCandidate(typeCandidate, memberName, line, resolutionState);
    }

    private static MemberResolutionState MapResolutionState(SymbolInfo symbolInfo)
    {
        if (symbolInfo.Symbol is not null)
        {
            return MemberResolutionState.Resolved;
        }

        if (symbolInfo.CandidateSymbols.Length > 1)
        {
            return MemberResolutionState.Ambiguous;
        }

        return MemberResolutionState.Unresolved;
    }

    private static string GetShortTypeName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return typeName;
        }

        var lastDot = typeName.LastIndexOf('.');
        return lastDot >= 0 ? typeName[(lastDot + 1)..] : typeName;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static ContractEnforcementMode ResolveContractEnforcementMode()
    {
        var configured = _configuration?.GetSection("WorkflowSettings")?["ContractEnforcementMode"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return ContractEnforcementMode.Off;
        }

        return configured.Trim().ToLowerInvariant() switch
        {
            "off" => ContractEnforcementMode.Off,
            "warn" => ContractEnforcementMode.Warn,
            "strictexternal" => ContractEnforcementMode.StrictExternal,
            _ => ContractEnforcementMode.Off
        };
    }

    private static bool ResolveAllowLocalTypeEvolution()
    {
        var configured = _configuration?.GetSection("WorkflowSettings")?["AllowLocalTypeEvolution"];
        return !bool.TryParse(configured, out var parsed) || parsed;
    }

    private readonly record struct ReferencedMemberCandidate(
        string TypeCandidate,
        string MemberName,
        int Line,
        MemberResolutionState ResolutionState);

    private enum MemberResolutionState
    {
        Resolved,
        Ambiguous,
        Unresolved
    }
}



param(
    [Parameter(Mandatory = $true)]
    [string] $WatchedRoot,

    [string] $SourceFile,

    [string] $DiffTool = "WinMerge",

    [switch] $SkipAIAttributes
)

$ErrorActionPreference = "Stop"

$WorkspaceRoot = $PSScriptRoot
$MonitorRoot = Join-Path $WorkspaceRoot "Monitor"
$ProjectPath = Join-Path $MonitorRoot "AIWorkflowMonitor.csproj"
$SettingsPath = Join-Path $MonitorRoot "appsettings.json"
$SettingsTemplatePath = Join-Path $MonitorRoot "appsettings.template.json"
$AIAttributesSamplePath = Join-Path $MonitorRoot "Docs\Samples\AIAttributes.cs"

if (-not (Test-Path -LiteralPath $MonitorRoot -PathType Container)) {
    throw "Monitor folder not found: $MonitorRoot"
}

if (-not (Test-Path -LiteralPath $ProjectPath -PathType Leaf)) {
    throw "Monitor project not found: $ProjectPath"
}

if (-not (Test-Path -LiteralPath $SettingsPath -PathType Leaf)) {
    if (-not (Test-Path -LiteralPath $SettingsTemplatePath -PathType Leaf)) {
        throw "Monitor settings template not found: $SettingsTemplatePath"
    }

    Copy-Item -LiteralPath $SettingsTemplatePath -Destination $SettingsPath
    Write-Host "Created local settings file from template: $SettingsPath"
}

function Get-RelativePathFromRoot {
    param(
        [string] $Root,
        [string] $Path
    )

    $rootFullPath = [System.IO.Path]::GetFullPath($Root).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $pathFullPath = [System.IO.Path]::GetFullPath($Path)
    $rootWithSeparator = $rootFullPath + [System.IO.Path]::DirectorySeparatorChar

    if ($pathFullPath.Equals($rootFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        return "."
    }

    if (-not $pathFullPath.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
        return ".."
    }

    return $pathFullPath.Substring($rootWithSeparator.Length)
}

$WatchedRoot = [System.IO.Path]::GetFullPath($WatchedRoot)
if (-not (Test-Path -LiteralPath $WatchedRoot -PathType Container)) {
    throw "Watched project root not found: $WatchedRoot"
}

if ([string]::IsNullOrWhiteSpace($SourceFile)) {
    $SourceFile = Get-ChildItem -LiteralPath $WatchedRoot -Recurse -File -Filter "*.cs" |
        Where-Object {
            $_.FullName -notmatch "\\bin\\" -and
            $_.FullName -notmatch "\\obj\\" -and
            $_.FullName -notmatch "\\.git\\"
        } |
        Select-Object -First 1 -ExpandProperty FullName

    if ([string]::IsNullOrWhiteSpace($SourceFile)) {
        throw "No .cs source file found under watched root: $WatchedRoot"
    }
}

$SourceFile = [System.IO.Path]::GetFullPath($SourceFile)
if (-not (Test-Path -LiteralPath $SourceFile -PathType Leaf)) {
    throw "Source file not found: $SourceFile"
}

$relativeToWatched = Get-RelativePathFromRoot -Root $WatchedRoot -Path $SourceFile
if ($relativeToWatched.StartsWith("..") -or [System.IO.Path]::IsPathRooted($relativeToWatched)) {
    throw "Source file must be inside watched root. SourceFile: $SourceFile WatchedRoot: $WatchedRoot"
}

function Get-WatchedProjectNamespace {
    param([string] $Root)

    $projectFile = Get-ChildItem -LiteralPath $Root -Recurse -File -Filter "*.csproj" |
        Where-Object {
            $_.FullName -notmatch "\\bin\\" -and
            $_.FullName -notmatch "\\obj\\"
        } |
        Select-Object -First 1

    if ($null -eq $projectFile) {
        return "WatchedProject"
    }

    [xml] $projectXml = Get-Content -LiteralPath $projectFile.FullName -Raw
    $propertyGroups = @($projectXml.Project.PropertyGroup)
    foreach ($group in $propertyGroups) {
        if (-not [string]::IsNullOrWhiteSpace($group.RootNamespace)) {
            return [string] $group.RootNamespace
        }
    }

    foreach ($group in $propertyGroups) {
        if (-not [string]::IsNullOrWhiteSpace($group.AssemblyName)) {
            return [string] $group.AssemblyName
        }
    }

    $name = [System.IO.Path]::GetFileNameWithoutExtension($projectFile.Name)
    return ($name -replace '[^A-Za-z0-9_]', '_')
}

function Install-AIAttributes {
    param(
        [string] $Root,
        [string] $ProjectNamespace
    )

    $aiDirectory = Join-Path $Root "AI"
    $attributePath = Join-Path $aiDirectory "AIAttributes.cs"

    if (Test-Path -LiteralPath $attributePath -PathType Leaf) {
        Write-Host "AI attributes already present: $attributePath"
        Ensure-CurrentAIAttributes -Root $Root -ProjectNamespace $ProjectNamespace
        return
    }

    if (-not (Test-Path -LiteralPath $AIAttributesSamplePath -PathType Leaf)) {
        throw "AI attributes sample not found: $AIAttributesSamplePath"
    }

    New-Item -ItemType Directory -Path $aiDirectory -Force | Out-Null
    $attributeNamespace = "$ProjectNamespace.AI"
    $content = Get-Content -LiteralPath $AIAttributesSamplePath -Raw
    $content = $content.Replace("ReplaceWithWatchedProjectNamespace.AI", $attributeNamespace)

    Set-Content -LiteralPath $attributePath -Value $content -Encoding UTF8
    Write-Host "Installed AI attributes: $attributePath"
    Write-Host "AI attribute namespace: $attributeNamespace"
}

function Test-AttributeClassExists {
    param(
        [string] $Root,
        [string] $ClassName
    )

    $pattern = "\b(class|record|enum)\s+$([regex]::Escape($ClassName))\b"
    $match = Get-ChildItem -LiteralPath $Root -Recurse -File -Filter "*.cs" |
        Where-Object {
            $_.FullName -notmatch "\\bin\\" -and
            $_.FullName -notmatch "\\obj\\" -and
            $_.FullName -notmatch "\\.git\\"
        } |
        Select-String -Pattern $pattern |
        Select-Object -First 1

    return $null -ne $match
}

function Ensure-CurrentAIAttributes {
    param(
        [string] $Root,
        [string] $ProjectNamespace
    )

    $missing = @()
    foreach ($className in @(
        "AICommandStatus",
        "DoNotRefactorAttribute",
        "AIInstructionsAttribute",
        "AIFileContextAttribute",
        "FileVersionAttribute",
        "AIChangeAttribute",
        "AIHistoryAttribute",
        "UserHistoryAttribute")) {
        if (-not (Test-AttributeClassExists -Root $Root -ClassName $className)) {
            $missing += $className
        }
    }

    if ($missing.Count -eq 0) {
        Write-Host "Current AI workflow attributes are already available."
        return
    }

    $aiDirectory = Join-Path $Root "AI"
    New-Item -ItemType Directory -Path $aiDirectory -Force | Out-Null
    $attributeNamespace = "$ProjectNamespace.AI"
    $supplementPath = Join-Path $aiDirectory "AIWorkflowCurrentAttributes.cs"

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("using System;")
    $lines.Add("")
    $lines.Add("namespace $attributeNamespace;")
    $lines.Add("")
    $lines.Add("// Supplemental current monitor attributes for legacy watched projects.")
    $lines.Add("// This file is generated only when older AI helper files are present but missing current workflow attributes.")

    if ($missing -contains "AICommandStatus") {
        $lines.Add("")
        $lines.Add("public enum AICommandStatus")
        $lines.Add("{")
        $lines.Add("    Pending,")
        $lines.Add("    Completed,")
        $lines.Add("    Verified,")
        $lines.Add("    Rejected")
        $lines.Add("}")
    }

    if ($missing -contains "DoNotRefactorAttribute") {
        $lines.Add("")
        $lines.Add("[AttributeUsage(AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true)]")
        $lines.Add("public sealed class DoNotRefactorAttribute : Attribute")
        $lines.Add("{")
        $lines.Add("    public string Reason { get; }")
        $lines.Add("    public string Warning => ""CRITICAL: Do not refactor without asking. DO NOT remove or relocate existing comments."";")
        $lines.Add("")
        $lines.Add("    public DoNotRefactorAttribute(string reason)")
        $lines.Add("    {")
        $lines.Add("        Reason = reason;")
        $lines.Add("    }")
        $lines.Add("}")
    }

    if ($missing -contains "AIInstructionsAttribute") {
        $lines.Add("")
        $lines.Add("[AttributeUsage(AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]")
        $lines.Add("public sealed class AIInstructionsAttribute : Attribute")
        $lines.Add("{")
        $lines.Add("    public string Command { get; }")
        $lines.Add("    public AICommandStatus Status { get; set; }")
        $lines.Add("")
        $lines.Add("    public AIInstructionsAttribute(string command, AICommandStatus status = AICommandStatus.Pending)")
        $lines.Add("    {")
        $lines.Add("        Command = command;")
        $lines.Add("        Status = status;")
        $lines.Add("    }")
        $lines.Add("}")
    }

    if ($missing -contains "AIFileContextAttribute") {
        $lines.Add("")
        $lines.Add("[AttributeUsage(AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]")
        $lines.Add("public sealed class AIFileContextAttribute : Attribute")
        $lines.Add("{")
        $lines.Add("    public string FileName { get; }")
        $lines.Add("    public string Purpose { get; }")
        $lines.Add("    public string Responsibilities { get; set; } = string.Empty;")
        $lines.Add("    public string Nuances { get; set; } = string.Empty;")
        $lines.Add("    public string RelatedFiles { get; set; } = string.Empty;")
        $lines.Add("    public string LastReviewed { get; set; } = string.Empty;")
        $lines.Add("")
        $lines.Add("    public AIFileContextAttribute(string fileName, string purpose)")
        $lines.Add("    {")
        $lines.Add("        FileName = fileName;")
        $lines.Add("        Purpose = purpose;")
        $lines.Add("    }")
        $lines.Add("}")
    }

    if ($missing -contains "FileVersionAttribute") {
        $lines.Add("")
        $lines.Add("[AttributeUsage(AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]")
        $lines.Add("public sealed class FileVersionAttribute : Attribute")
        $lines.Add("{")
        $lines.Add("    public string Version { get; }")
        $lines.Add("")
        $lines.Add("    public FileVersionAttribute(string version)")
        $lines.Add("    {")
        $lines.Add("        Version = version;")
        $lines.Add("    }")
        $lines.Add("}")
    }

    if ($missing -contains "AIChangeAttribute") {
        $lines.Add("")
        $lines.Add("[AttributeUsage(AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true)]")
        $lines.Add("public sealed class AIChangeAttribute : Attribute")
        $lines.Add("{")
        $lines.Add("    public string Version { get; }")
        $lines.Add("    public string Summary { get; }")
        $lines.Add("    public string Command => Summary;")
        $lines.Add("    public AICommandStatus Status { get; set; }")
        $lines.Add("    public string Timestamp { get; set; } = string.Empty;")
        $lines.Add("")
        $lines.Add("    public AIChangeAttribute(string version, string summary)")
        $lines.Add("        : this(version, summary, AICommandStatus.Pending)")
        $lines.Add("    {")
        $lines.Add("    }")
        $lines.Add("")
        $lines.Add("    public AIChangeAttribute(string version, string summary, AICommandStatus status)")
        $lines.Add("    {")
        $lines.Add("        Version = version;")
        $lines.Add("        Summary = summary;")
        $lines.Add("        Status = status;")
        $lines.Add("    }")
        $lines.Add("}")
    }

    if ($missing -contains "AIHistoryAttribute") {
        $lines.Add("")
        $lines.Add("[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]")
        $lines.Add("public sealed class AIHistoryAttribute : Attribute")
        $lines.Add("{")
        $lines.Add("    public string Version { get; }")
        $lines.Add("    public string ChangeLog { get; }")
        $lines.Add("    public string Timestamp { get; }")
        $lines.Add("")
        $lines.Add("    public AIHistoryAttribute(string version, string changeLog)")
        $lines.Add("    {")
        $lines.Add("        Version = version;")
        $lines.Add("        ChangeLog = changeLog;")
        $lines.Add("        Timestamp = DateTime.Now.ToString(""yyyy-MM-dd HH:mm"");")
        $lines.Add("    }")
        $lines.Add("}")
    }

    if ($missing -contains "UserHistoryAttribute") {
        $lines.Add("")
        $lines.Add("[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]")
        $lines.Add("public sealed class UserHistoryAttribute : Attribute")
        $lines.Add("{")
        $lines.Add("    public string Version { get; }")
        $lines.Add("    public string ChangeLog { get; }")
        $lines.Add("    public string Timestamp { get; }")
        $lines.Add("")
        $lines.Add("    public UserHistoryAttribute(string version, string changeLog)")
        $lines.Add("    {")
        $lines.Add("        Version = version;")
        $lines.Add("        ChangeLog = changeLog;")
        $lines.Add("        Timestamp = DateTime.Now.ToString(""yyyy-MM-dd HH:mm"");")
        $lines.Add("    }")
        $lines.Add("}")
    }

    Set-Content -LiteralPath $supplementPath -Value ($lines -join [Environment]::NewLine) -Encoding UTF8
    Write-Host "Installed supplemental current AI attributes: $supplementPath"
    Write-Host "Missing attributes supplied: $($missing -join ', ')"
}

$watchedNamespace = Get-WatchedProjectNamespace -Root $WatchedRoot
if ($SkipAIAttributes) {
    Write-Host "Skipping AI attributes install because -SkipAIAttributes was specified."
} else {
    Install-AIAttributes -Root $WatchedRoot -ProjectNamespace $watchedNamespace
}

$settings = Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
if ($null -eq $settings.WorkflowSettings) {
    $settings | Add-Member -MemberType NoteProperty -Name "WorkflowSettings" -Value ([pscustomobject]@{})
}

if ($null -eq $settings.WorkflowSettings.PSObject.Properties["ObservedRoot"]) {
    $settings.WorkflowSettings | Add-Member -MemberType NoteProperty -Name "ObservedRoot" -Value $WatchedRoot
} else {
    $settings.WorkflowSettings.ObservedRoot = $WatchedRoot
}

if ($null -eq $settings.WorkflowSettings.PSObject.Properties["DiffTool"]) {
    $settings.WorkflowSettings | Add-Member -MemberType NoteProperty -Name "DiffTool" -Value $DiffTool
} else {
    $settings.WorkflowSettings.DiffTool = $DiffTool
}
$settings | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $SettingsPath -Encoding UTF8

Write-Host "Updated $SettingsPath"
Write-Host "ObservedRoot: $WatchedRoot"
Write-Host "DiffTool: $DiffTool"
Write-Host "SourceFile: $SourceFile"

dotnet build $ProjectPath
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet run --project $ProjectPath -- $SourceFile --refresh-only
exit $LASTEXITCODE


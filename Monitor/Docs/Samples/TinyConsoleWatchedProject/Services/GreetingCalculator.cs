using TinyConsoleWatchedProject.AI;

namespace TinyConsoleWatchedProject.Services;

[FileVersion("1.0")]
[AIFileContext(
    "Services/GreetingCalculator.cs",
    "Provides a tiny dependency used to verify monitor overlay validation across multiple Working files.",
    Responsibilities = "Normalize the input name and return a deterministic greeting for smoke tests.")]
public sealed class GreetingCalculator
{
    public string BuildGreeting(string name)
    {
        var cleanedName = string.IsNullOrWhiteSpace(name)
            ? "World"
            : name.Trim();

        return $"Hello, {cleanedName}.";
    }
}

using TinyConsoleWatchedProject.AI;
using TinyConsoleWatchedProject.Services;

namespace TinyConsoleWatchedProject;

[FileVersion("1.0")]
[AIFileContext(
    "Program.cs",
    "Provides the explicit console entry point for the tiny monitor smoke-test project.",
    Responsibilities = "Create the sample service, print one deterministic greeting, and avoid top-level statements.")]
internal static class Program
{
    private static void Main()
    {
        var calculator = new GreetingCalculator();
        var message = calculator.BuildGreeting("Monitor");

        Console.WriteLine(message);
    }
}

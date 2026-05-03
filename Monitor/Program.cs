using Microsoft.Extensions.Configuration;

namespace AIWorkflowMonitor;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.template.json", optional: true, reloadOnChange: false)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .Build();

            AIWorkflowRunner.SetConfiguration(configuration);
            return AIWorkflowRunner.Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected monitor failure: {ex.Message}");
            return (int)AIWorkflowRunnerExitCode.UnexpectedFailure;
        }
    }
}



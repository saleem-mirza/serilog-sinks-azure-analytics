using Serilog.Events;
using Serilog.Sinks.AzureAnalytics;

namespace Serilog.TestHarness.Console
{
    class Program
    {
        static void Main(string[] args)
        {
            var primaryAuthKey = "";
            var secondaryAuthKey = "";
            var workspaceId = "";

            Serilog.Debugging.SelfLog.Enable(msg => System.Console.WriteLine(msg));

            var logger = new LoggerConfiguration().WriteTo.AzureAnalytics(workspaceId, primaryAuthKey,
                new ConfigurationSettings
                {
                    SecondaryAuthenticationKey = secondaryAuthKey
                }).CreateLogger();

            var selection = "";

            DisplaySelections();

            while (selection != "q")
            {
                selection = System.Console.ReadLine();

                if (selection == "l")
                {
                    logger.Information("TestHarness Log Entry 1");

                    System.Console.WriteLine("Log entries created.");

                    DisplaySelections();
                }
            }
        }

        private static void DisplaySelections()
        {
            System.Console.WriteLine("q: quit");
            System.Console.WriteLine("l: create log entries");
        }
    }
}

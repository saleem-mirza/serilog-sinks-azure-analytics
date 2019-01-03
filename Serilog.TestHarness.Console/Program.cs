using Serilog.Events;
using Serilog.Sinks.AzureAnalytics;

namespace Serilog.TestHarness.Console
{
    class Program
    {
        static void Main(string[] args)
        {
            var primaryAuthKey = "O86OUwSXFqzhBHQE3nBF+vRTclvLdwRFSW65nOu06N1qAEKA3zI6v3vc+3zcC0G0zRW0dXs9dGMlZa2NoQrGyA==";
            var secondaryAuthKey = "kJcVrEQ+s5swAR35mqoWnTtlTBLXPwAzHinSDWmJ6q9mvDzdQTTGYHa3wpJzLORzt2PqR0jWGR7/dprxt5QK5A==";
            var workspaceId = "aa4e011c-d6c8-43bf-8a23-adcc9938a93b";

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

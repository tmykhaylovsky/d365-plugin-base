using System;

namespace Ops.Plugins.Registration
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                var options = RegistrationOptions.Parse(args ?? Array.Empty<string>());
                if (options.Help)
                {
                    RegistrationConsoleReporter.WriteHelp();
                    return 0;
                }

                options.ValidateForRun();
                var runner = new RegistrationSyncRunner(
                    new PluginAssemblyInspector(),
                    DataverseRegistrationRepository.Create(options),
                    new RegistrationComparer(),
                    new RegistrationConsoleReporter());

                return runner.Run(options);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                Console.Error.WriteLine("Use --help for examples.");
                return 1;
            }
        }
    }
}

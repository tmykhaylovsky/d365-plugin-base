using System;
using System.Collections.Generic;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;

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
                Console.Error.WriteLine("ERROR: " + ex.GetType().Name + ": " + ex.Message);
                WriteInnerExceptions(ex.InnerException);
                Console.Error.WriteLine("Use --help for examples.");
                return 1;
            }
        }

        private static void WriteInnerExceptions(Exception exception)
        {
            var depth = 0;
            while (exception != null && depth < 6)
            {
                if (!string.IsNullOrWhiteSpace(exception.Message))
                    Console.Error.WriteLine("CAUSE: " + exception.GetType().Name + ": " + exception.Message);
                WriteOrganizationServiceFault(exception);

                exception = exception.InnerException;
                depth++;
            }
        }

        private static void WriteOrganizationServiceFault(Exception exception)
        {
            var fault = (exception as FaultException<OrganizationServiceFault>)?.Detail;
            if (fault == null) return;

            Console.Error.WriteLine("DATAVERSE FAULT: " + fault.Message);
            Console.Error.WriteLine("ERROR CODE: " + fault.ErrorCode);
            WriteErrorDetails(fault.ErrorDetails);
            if (!string.IsNullOrWhiteSpace(fault.TraceText))
                Console.Error.WriteLine("TRACE: " + fault.TraceText);
        }

        private static void WriteErrorDetails(ErrorDetailCollection details)
        {
            if (details == null || details.Count == 0) return;

            foreach (KeyValuePair<string, object> detail in details)
                Console.Error.WriteLine("FAULT DETAIL: " + detail.Key + " = " + detail.Value);
        }
    }
}

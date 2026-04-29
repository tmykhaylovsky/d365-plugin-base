using System;
using System.Collections.Generic;
using System.Linq;

namespace Ops.Plugins.Registration
{
    public sealed class RegistrationConsoleReporter
    {
        public void Write(RegistrationOptions options, DesiredRegistration desired, ActualRegistration actual, RegistrationPlan plan)
        {
            Console.WriteLine("Mode: " + (options.Apply ? "APPLY" : "DRY-RUN"));
            if (options.PushAssembly) Console.WriteLine("Assembly push: enabled");
            Console.WriteLine("Assembly: " + actual.Assembly.Name + " (pluginassemblyid: " + actual.Assembly.Id + ")");
            if (!string.IsNullOrWhiteSpace(options.EnvironmentUrl)) Console.WriteLine("Environment: " + options.EnvironmentUrl);
            Console.WriteLine();
            Console.WriteLine("Desired steps: " + desired.PluginTypes.SelectMany(t => t.Steps).Count());
            Console.WriteLine("Matched steps: " + plan.Changes.Count(c => c.Action == RegistrationActionKind.Ok && c.Target == RegistrationTargetKind.Step));
            Console.WriteLine("Creates: " + plan.Creates);
            Console.WriteLine("Updates: " + plan.Updates);
            Console.WriteLine("Extras: " + plan.Extras);
            Console.WriteLine("Warnings: " + plan.Warnings);
            Console.WriteLine("Errors: " + plan.Errors);
            Console.WriteLine();

            WriteTable(plan.Changes.Where(c => options.Verbose || c.Action != RegistrationActionKind.Ok));
        }

        public static void WriteHelp()
        {
            Console.WriteLine("Dataverse plugin registration sync");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  Ops.Plugins.Registration.exe --connectionString DATAVERSE_CONNECTION");
            Console.WriteLine("  Ops.Plugins.Registration.exe --assembly Ops.Plugins/bin/Release/net462/Ops.Plugins.dll --environment https://org.crm.dynamics.com");
            Console.WriteLine("  Ops.Plugins.Registration.exe --connectionString DATAVERSE_CONNECTION --apply");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --assembly <path>              Plugin DLL. Defaults to release, then debug Ops.Plugins.dll.");
            Console.WriteLine("  --pluginAssemblyId <guid>      Preferred exact Dataverse pluginassembly lookup.");
            Console.WriteLine("  --assemblyName <name>          Fallback pluginassembly name lookup.");
            Console.WriteLine("  --environment <url>            Dataverse URL for interactive OAuth connection.");
            Console.WriteLine("  --connectionString <value/env> Explicit connection string or environment variable name.");
            Console.WriteLine("  --userMap <path>               Run-as user JSON. Defaults to " + RegistrationOptions.GetDefaultUserMapPath() + ".");
            Console.WriteLine("  --pushAssembly                 Update matched pluginassembly content from the DLL before comparing steps. --apply does this automatically.");
            Console.WriteLine("  --apply                        Create/update missing or drifted registration rows.");
            Console.WriteLine("  --includeDisabled              Include disabled steps in comparison without enabling them.");
            Console.WriteLine("  --verbose                      Show unchanged rows and IDs.");
        }

        private static void WriteTable(IEnumerable<RegistrationChange> changes)
        {
            var rows = changes.Select(c => new[]
            {
                c.Action.ToString(),
                c.Target.ToString(),
                Shorten(c.PluginTypeName, 36),
                c.MessageName ?? string.Empty,
                c.EntityLogicalName ?? string.Empty,
                c.Detail ?? string.Empty
            }).ToArray();

            Console.WriteLine("Action  Type        Plugin                                Message  Entity        Detail");
            Console.WriteLine("------  ----------  ------------------------------------  -------  ------------  ------------------------------");

            foreach (var row in rows)
                Console.WriteLine($"{row[0],-6}  {row[1],-10}  {row[2],-36}  {row[3],-7}  {row[4],-12}  {row[5]}");
        }

        private static string Shorten(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty.PadRight(max);
            return value.Length <= max ? value : value.Substring(0, max - 3) + "...";
        }
    }
}

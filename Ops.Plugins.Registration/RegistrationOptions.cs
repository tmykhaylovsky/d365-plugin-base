using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Ops.Plugins.Registration
{
    public sealed class RegistrationOptions
    {
        public string AssemblyPath { get; set; }
        public Guid? PluginAssemblyId { get; set; }
        public string AssemblyName { get; set; }
        public string EnvironmentUrl { get; set; }
        public string ConnectionString { get; set; }
        public bool Apply { get; set; }
        public bool PushAssembly { get; set; }
        public bool IncludeDisabled { get; set; }
        public bool Verbose { get; set; }
        public bool Help { get; set; }
        public string UserMapPath { get; set; }
        public IReadOnlyDictionary<string, Guid> UserAliases { get; private set; } = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        public static RegistrationOptions Parse(string[] args)
        {
            var options = new RegistrationOptions();

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                switch (arg)
                {
                    case "--assembly":
                        options.AssemblyPath = RequireValue(args, ref i, arg);
                        break;
                    case "--pluginAssemblyId":
                        Guid id;
                        var rawId = RequireValue(args, ref i, arg);
                        if (!Guid.TryParse(rawId, out id)) throw new ArgumentException("--pluginAssemblyId must be a GUID.");
                        options.PluginAssemblyId = id;
                        break;
                    case "--assemblyName":
                        options.AssemblyName = RequireValue(args, ref i, arg);
                        break;
                    case "--environment":
                        options.EnvironmentUrl = RequireValue(args, ref i, arg);
                        break;
                    case "--connectionString":
                        options.ConnectionString = ResolveConnectionString(RequireValue(args, ref i, arg));
                        break;
                    case "--apply":
                        options.Apply = true;
                        break;
                    case "--pushAssembly":
                        options.PushAssembly = true;
                        break;
                    case "--includeDisabled":
                        options.IncludeDisabled = true;
                        break;
                    case "--verbose":
                        options.Verbose = true;
                        break;
                    case "--userMap":
                        options.UserMapPath = RequireValue(args, ref i, arg);
                        break;
                    case "--help":
                    case "-h":
                    case "/?":
                        options.Help = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument '{arg}'. Use --help for examples.");
                }
            }

            if (string.IsNullOrWhiteSpace(options.AssemblyPath))
                options.AssemblyPath = FindDefaultAssemblyPath();
            options.LoadUserAliases();

            return options;
        }

        public void ValidateForRun()
        {
            if (string.IsNullOrWhiteSpace(AssemblyPath) || !File.Exists(AssemblyPath))
                throw new ArgumentException("Provide --assembly <path>, or build Ops.Plugins first so the default DLL exists.");

            if (string.IsNullOrWhiteSpace(ConnectionString) && string.IsNullOrWhiteSpace(EnvironmentUrl))
                throw new ArgumentException("Provide --connectionString <value-or-env-var> or --environment <url>.");
        }

        private static string RequireValue(string[] args, ref int index, string optionName)
        {
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"{optionName} requires a value.");

            index++;
            return args[index];
        }

        private static string ResolveConnectionString(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            var environmentValue = Environment.GetEnvironmentVariable(value);
            return string.IsNullOrWhiteSpace(environmentValue) ? value : environmentValue;
        }

        private static string FindDefaultAssemblyPath()
        {
            var release = Path.Combine("Ops.Plugins", "bin", "Release", "net462", "Ops.Plugins.dll");
            if (File.Exists(release)) return release;

            var debug = Path.Combine("Ops.Plugins", "bin", "Debug", "net462", "Ops.Plugins.dll");
            return debug;
        }

        private void LoadUserAliases()
        {
            var path = string.IsNullOrWhiteSpace(UserMapPath) ? GetDefaultUserMapPath() : UserMapPath;

            if (!File.Exists(path))
            {
                UserAliases = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            var aliases = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var content = File.ReadAllText(path);
            foreach (Match match in Regex.Matches(content, "\"([^\"]+)\"\\s*:\\s*\"([^\"]+)\""))
            {
                Guid id;
                if (!Guid.TryParse(match.Groups[2].Value, out id))
                    throw new ArgumentException($"User map value for '{match.Groups[1].Value}' must be a systemuserid GUID.");

                aliases[match.Groups[1].Value] = id;
            }

            UserAliases = aliases;
        }

        public static string GetDefaultUserMapPath()
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(root, "Ops.Plugins", "dataverse-registration-users.json");
        }
    }
}

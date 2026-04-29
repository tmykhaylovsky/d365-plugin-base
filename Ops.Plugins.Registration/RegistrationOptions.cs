using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

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
        public IReadOnlyDictionary<string, RunInUserContextReference> UserReferences { get; private set; } = new Dictionary<string, RunInUserContextReference>(StringComparer.OrdinalIgnoreCase);
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

        public void ValidateRunInUserContexts(DesiredRegistration desired)
        {
            var missing = desired.PluginTypes
                .SelectMany(t => t.Steps)
                .Select(s => s.RunInUserContext)
                .Where(IsFixedLabel)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(label =>
                {
                    RunInUserContextReference reference;
                    return UserReferences == null
                        || !UserReferences.TryGetValue(label, out reference)
                        || !reference.SystemUserId.HasValue;
                })
                .ToArray();

            if (missing.Length == 0) return;

            throw new InvalidOperationException(
                "Run in User's Context label(s) missing systemuserid in " + GetResolvedUserMapPath() + ": " +
                string.Join(", ", missing) +
                ". Add them to the repo-local run-as user config or pass --userMap <path>.");
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
            var path = GetResolvedUserMapPath();

            if (!File.Exists(path))
            {
                UserReferences = new Dictionary<string, RunInUserContextReference>(StringComparer.OrdinalIgnoreCase);
                UserAliases = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            var content = File.ReadAllText(path);
            if (LooksLikeArray(content))
                LoadUserReferencesFromArray(content);
            else
                LoadLegacyAliasMap(content);
        }

        private void LoadUserReferencesFromArray(string content)
        {
            var aliases = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var references = new Dictionary<string, RunInUserContextReference>(StringComparer.OrdinalIgnoreCase);
            var serializer = new JavaScriptSerializer();
            var rows = serializer.Deserialize<List<Dictionary<string, object>>>(content);

            foreach (var row in rows ?? new List<Dictionary<string, object>>())
            {
                var label = GetString(row, "label");
                if (string.IsNullOrWhiteSpace(label))
                    throw new ArgumentException("Run-as user config entries must include a non-empty label.");

                var rawId = GetString(row, "systemuserid");
                Guid id;
                Guid? userId = null;
                if (!string.IsNullOrWhiteSpace(rawId))
                {
                    if (!Guid.TryParse(rawId, out id))
                        throw new ArgumentException($"Run-as user config value for '{label}' must be a systemuserid GUID or null.");
                    if (id == Guid.Empty)
                        throw new ArgumentException($"Run-as user config value for '{label}' must be a real systemuserid GUID, not 00000000-0000-0000-0000-000000000000.");
                    userId = id;
                    aliases[label] = id;
                }

                references[label] = new RunInUserContextReference
                {
                    Label = label.Trim(),
                    SystemUserId = userId,
                    FullName = GetString(row, "fullname")
                };
            }

            UserReferences = references;
            UserAliases = aliases;
        }

        private void LoadLegacyAliasMap(string content)
        {
            var aliases = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var references = new Dictionary<string, RunInUserContextReference>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in Regex.Matches(content, "\"([^\"]+)\"\\s*:\\s*\"([^\"]+)\""))
            {
                Guid id;
                if (!Guid.TryParse(match.Groups[2].Value, out id))
                    throw new ArgumentException($"User map value for '{match.Groups[1].Value}' must be a systemuserid GUID.");
                if (id == Guid.Empty)
                    throw new ArgumentException($"User map value for '{match.Groups[1].Value}' must be a real systemuserid GUID, not 00000000-0000-0000-0000-000000000000.");

                var label = match.Groups[1].Value;
                aliases[label] = id;
                references[label] = new RunInUserContextReference { Label = label, SystemUserId = id };
            }

            UserReferences = references;
            UserAliases = aliases;
        }

        public static string GetDefaultUserMapPath()
        {
            return Path.Combine(FindRepoRoot(Environment.CurrentDirectory), ".local", "run-in-user-context.json");
        }

        private string GetResolvedUserMapPath()
        {
            return string.IsNullOrWhiteSpace(UserMapPath) ? GetDefaultUserMapPath() : Path.GetFullPath(UserMapPath);
        }

        private static string FindRepoRoot(string start)
        {
            var directory = new DirectoryInfo(Path.GetFullPath(start));
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Ops.Plugins.slnx")))
                    return directory.FullName;

                directory = directory.Parent;
            }

            return Path.GetFullPath(start);
        }

        private static bool LooksLikeArray(string content)
        {
            return !string.IsNullOrWhiteSpace(content) && content.TrimStart().StartsWith("[", StringComparison.Ordinal);
        }

        private static string GetString(IReadOnlyDictionary<string, object> row, string name)
        {
            object value;
            return row != null && row.TryGetValue(name, out value) ? value?.ToString()?.Trim() : null;
        }

        private static bool IsFixedLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (string.Equals(value, "Calling User", StringComparison.OrdinalIgnoreCase)) return false;

            Guid id;
            return !Guid.TryParse(value, out id);
        }
    }
}

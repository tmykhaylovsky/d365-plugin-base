using System;
using System.Linq;
using System.Text;
using Microsoft.Xrm.Sdk;
// IMPORTANT: use Microsoft.Xrm.Sdk.PluginTelemetry — NOT Microsoft.Extensions.Logging.
// Using the wrong namespace compiles cleanly but ILogger resolves to null at runtime.
using Microsoft.Xrm.Sdk.PluginTelemetry;

namespace Ops.Plugins.Shared
{
    // TraceLevel is preserved for code annotation and future internal gating.
    // Current default: Verbose (all calls pass through). Control via PluginLogger.GlobalLevel
    // or a future unsecure config key if needed.
    public enum TraceLevel
    {
        Off      = 0,
        Critical = 1,
        Warning  = 2,
        Info     = 3,
        Verbose  = 4
    }

    public sealed class PluginLogger
    {
        private readonly ITracingService _tracingService;
        private readonly ILogger _appInsights;   // null when AppInsights not configured — graceful no-op
        private readonly string _prefix;

        // Global gate — set once per assembly load to control all loggers.
        // Defaults to Verbose (log everything). Override at the plugin entry point or via
        // a static initializer in the plugin project when a finer level is needed.
        public static TraceLevel GlobalLevel { get; set; } = TraceLevel.Verbose;

        // Dataverse platform frame prefixes — filtered out of StackTrace extraction
        private static readonly string[] PlatformPrefixes =
        {
            "at Microsoft.Crm.",
            "at Microsoft.Xrm.",
            "at System.Runtime.",
            "at System.Threading.",
            "at System.Reflection.",
            "at System.AppDomain.",
            "at System.Web.",
        };

        private const int MaxStackFrames = 5;

        public PluginLogger(IServiceProvider serviceProvider, string pluginName, Guid correlationId)
        {
            _tracingService = serviceProvider.GetService(typeof(ITracingService)) as ITracingService
                ?? throw new InvalidPluginExecutionException("ITracingService not available.");

            _appInsights = GetOptionalService<ILogger>(serviceProvider);

            _prefix = $"[{pluginName}|{correlationId:N}]";
        }

        // Standard trace — string allocated regardless of level.
        // Use the Func<string> overload inside hot paths.
        public void Trace(TraceLevel level, string message)
        {
            if (level > GlobalLevel) return;
            Write(level, message);
        }

        // Lazy trace — factory only invoked when level passes. Zero allocation on suppressed calls.
        public void Trace(TraceLevel level, Func<string> messageFactory)
        {
            if (level > GlobalLevel) return;
            Write(level, messageFactory());
        }

        // Always writes regardless of GlobalLevel. Extracts the top application frames
        // from the stack trace, filtering Dataverse platform noise.
        public void LogError(Exception ex, string context)
        {
            _tracingService.Trace($"{_prefix}[Critical] {context}");
            _tracingService.Trace(ExtractKeyFrames(ex));
            _appInsights?.LogError(ex, $"{_prefix} {context}");
        }

        private void Write(TraceLevel level, string message)
        {
            var formatted = $"{_prefix}[{level}] {message}";
            _tracingService.Trace(formatted);

            if (_appInsights == null) return;
            switch (level)
            {
                case TraceLevel.Critical: _appInsights.LogCritical(formatted);    break;
                case TraceLevel.Warning:  _appInsights.LogWarning(formatted);     break;
                case TraceLevel.Info:     _appInsights.LogInformation(formatted); break;
                case TraceLevel.Verbose:  _appInsights.LogDebug(formatted);       break;
            }
        }

        private static T GetOptionalService<T>(IServiceProvider serviceProvider) where T : class
        {
            try
            {
                return serviceProvider.GetService(typeof(T)) as T;
            }
            catch
            {
                return null;
            }
        }

        // Extracts the most relevant frames from an exception chain.
        // Strategy: keep non-platform frames up to MaxStackFrames per level;
        // fall back to raw top frames when the exception originates entirely in platform code.
        // Chains through InnerException (max 3 levels) showing message + first frame per level.
        private static string ExtractKeyFrames(Exception ex)
        {
            if (ex == null) return string.Empty;

            var sb     = new StringBuilder();
            var current = ex;
            var depth   = 0;

            while (current != null && depth < 3)
            {
                if (depth > 0) sb.AppendLine().Append("  Caused by: ");
                sb.Append($"{current.GetType().Name}: {current.Message}");

                if (!string.IsNullOrEmpty(current.StackTrace))
                {
                    var allFrames = current.StackTrace
                        .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(f => f.Trim())
                        .Where(f => f.StartsWith("at ", StringComparison.Ordinal))
                        .ToArray();

                    // Prefer application frames; fall back to raw top frames when nothing survives filter
                    var appFrames = allFrames.Where(f => !IsPlatformFrame(f)).Take(MaxStackFrames).ToArray();
                    var frames    = appFrames.Length > 0 ? appFrames : allFrames.Take(MaxStackFrames).ToArray();

                    foreach (var frame in frames)
                        sb.AppendLine().Append("    ").Append(frame);

                    // Signal truncation when platform frames were stripped
                    if (allFrames.Length > frames.Length && appFrames.Length > 0)
                        sb.AppendLine().Append("    [+ platform frames omitted]");
                }

                current = current.InnerException;
                depth++;
            }

            return sb.ToString();
        }

        private static bool IsPlatformFrame(string frame)
        {
            foreach (var prefix in PlatformPrefixes)
                if (frame.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }

    // Parses key=value pairs from a plugin step unsecure configuration string.
    // Format: "Key1=Value1;Key2=Value2"  (semicolon-delimited, case-insensitive keys)
    // TraceLevel is no longer read from config — use PluginLogger.GlobalLevel instead.
    public static class PluginConfig
    {
        public static string Get(string unsecureConfig, string key, string defaultValue = null)
        {
            if (string.IsNullOrWhiteSpace(unsecureConfig)) return defaultValue;
            foreach (var segment in unsecureConfig.Split(';'))
            {
                var idx = segment.IndexOf('=');
                if (idx <= 0) continue;
                if (string.Equals(segment.Substring(0, idx).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    return segment.Substring(idx + 1).Trim();
            }
            return defaultValue;
        }

        public static T GetEnum<T>(string unsecureConfig, string key, T defaultValue) where T : struct
        {
            var raw = Get(unsecureConfig, key);
            return raw != null && Enum.TryParse(raw, ignoreCase: true, out T result) ? result : defaultValue;
        }

        public static int GetInt(string unsecureConfig, string key, int defaultValue)
        {
            var raw = Get(unsecureConfig, key);
            return raw != null && int.TryParse(raw, out var result) ? result : defaultValue;
        }

        public static bool GetBool(string unsecureConfig, string key, bool defaultValue)
        {
            var raw = Get(unsecureConfig, key);
            return raw != null && bool.TryParse(raw, out var result) ? result : defaultValue;
        }
    }
}

using System;

namespace Ops.Plugins.Registration
{
    internal static class RegistrationKeys
    {
        public static string Step(string pluginTypeName, string messageName, string entityLogicalName, int stage, int mode)
        {
            return string.Join("|", new[]
            {
                Normalize(pluginTypeName),
                Normalize(messageName),
                Normalize(entityLogicalName),
                stage.ToString(),
                mode.ToString()
            });
        }

        public static string Image(string stepKey, string alias, int imageType)
        {
            return $"{stepKey}|image|{Normalize(alias)}|{imageType}";
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant();
        }
    }
}

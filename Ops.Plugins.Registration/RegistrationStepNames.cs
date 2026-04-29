namespace Ops.Plugins.Registration
{
    internal static class RegistrationStepNames
    {
        public static string For(DesiredStep desired)
        {
            return $"{desired.PluginTypeName}: {desired.MessageName} of {desired.EntityLogicalName}";
        }
    }
}

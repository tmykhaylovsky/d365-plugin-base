namespace Ops.Plugins.Registration
{
    internal static class RegistrationEntityNames
    {
        public const string PluginAssembly = "pluginassembly";
        public const string PluginType = "plugintype";
        public const string SdkMessage = "sdkmessage";
        public const string SdkMessageFilter = "sdkmessagefilter";
        public const string SdkMessageProcessingStep = "sdkmessageprocessingstep";
        public const string SdkMessageProcessingStepImage = "sdkmessageprocessingstepimage";
        public const string SystemUser = "systemuser";

        public static class PluginAssemblyFields
        {
            public const string Content = "content";
            public const string Culture = "culture";
            public const string Name = "name";
            public const string PluginAssemblyId = "pluginassemblyid";
            public const string PublicKeyToken = "publickeytoken";
            public const string Version = "version";
        }

        public static class PluginTypeFields
        {
            public const string PluginAssemblyId = "pluginassemblyid";
            public const string PluginTypeId = "plugintypeid";
            public const string TypeName = "typename";
        }

        public static class SdkMessageFields
        {
            public const string Name = "name";
            public const string SdkMessageId = "sdkmessageid";
        }

        public static class SdkMessageFilterFields
        {
            public const string PrimaryObjectTypeCode = "primaryobjecttypecode";
            public const string SdkMessageFilterId = "sdkmessagefilterid";
            public const string SdkMessageId = "sdkmessageid";
        }

        public static class StepFields
        {
            public const string Configuration = "configuration";
            public const string Description = "description";
            public const string EventHandler = "eventhandler";
            public const string FilteringAttributes = "filteringattributes";
            public const string ImpersonatingUserId = "impersonatinguserid";
            public const string IsManaged = "ismanaged";
            public const string Mode = "mode";
            public const string Name = "name";
            public const string Rank = "rank";
            public const string SdkMessageFilterId = "sdkmessagefilterid";
            public const string SdkMessageId = "sdkmessageid";
            public const string SdkMessageProcessingStepId = "sdkmessageprocessingstepid";
            public const string Stage = "stage";
            public const string StateCode = "statecode";
            public const string SupportedDeployment = "supporteddeployment";
        }

        public static class ImageFields
        {
            public const string Attributes = "attributes";
            public const string EntityAlias = "entityalias";
            public const string ImageType = "imagetype";
            public const string MessagePropertyName = "messagepropertyname";
            public const string Name = "name";
            public const string SdkMessageProcessingStepId = "sdkmessageprocessingstepid";
            public const string SdkMessageProcessingStepImageId = "sdkmessageprocessingstepimageid";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;

namespace Ops.Plugins.Registration
{
    public enum RegistrationActionKind
    {
        Ok,
        Create,
        Update,
        Extra,
        Warning,
        Error
    }

    public enum RegistrationTargetKind
    {
        Step,
        Image,
        PluginType,
        Assembly
    }

    public sealed class DesiredRegistration
    {
        public DesiredRegistration(string assemblyName, IReadOnlyCollection<DesiredPluginType> pluginTypes)
            : this(assemblyName, pluginTypes, pluginTypes == null ? Array.Empty<string>() : pluginTypes.Select(t => t.TypeName).ToArray())
        {
        }

        public DesiredRegistration(string assemblyName, IReadOnlyCollection<DesiredPluginType> pluginTypes, IReadOnlyCollection<string> pluginTypeNamesInAssembly)
        {
            AssemblyName = assemblyName;
            PluginTypes = pluginTypes;
            PluginTypeNamesInAssembly = pluginTypeNamesInAssembly ?? Array.Empty<string>();
        }

        public string AssemblyName { get; }
        public IReadOnlyCollection<DesiredPluginType> PluginTypes { get; }
        public IReadOnlyCollection<string> PluginTypeNamesInAssembly { get; }
    }

    public sealed class DesiredPluginType
    {
        public DesiredPluginType(string typeName, IReadOnlyCollection<DesiredStep> steps)
        {
            TypeName = typeName;
            Steps = steps;
        }

        public string TypeName { get; }
        public IReadOnlyCollection<DesiredStep> Steps { get; }
    }

    public sealed class DesiredStep
    {
        public string PluginTypeName { get; set; }
        public string MessageName { get; set; }
        public string EntityLogicalName { get; set; }
        public int Stage { get; set; }
        public int Mode { get; set; }
        public int Rank { get; set; }
        public AttributeList FilteringAttributes { get; set; }
        public IReadOnlyCollection<DesiredImage> Images { get; set; }
        public string RunInUserContext { get; set; }
        public string Description { get; set; }

        public string Key => RegistrationKeys.Step(PluginTypeName, MessageName, EntityLogicalName, Stage, Mode);
    }

    public sealed class DesiredImage
    {
        public string PluginTypeName { get; set; }
        public string MessageName { get; set; }
        public string EntityLogicalName { get; set; }
        public int StepStage { get; set; }
        public int StepMode { get; set; }
        public string Alias { get; set; }
        public int ImageType { get; set; }
        public string MessagePropertyName { get; set; }
        public AttributeList Attributes { get; set; }

        public string StepKey => RegistrationKeys.Step(PluginTypeName, MessageName, EntityLogicalName, StepStage, StepMode);
        public string Key => RegistrationKeys.Image(StepKey, Alias, ImageType);
    }

    public sealed class ActualRegistration
    {
        public ActualRegistration(ActualPluginAssembly assembly, IReadOnlyDictionary<string, ActualPluginType> pluginTypes, IReadOnlyCollection<ActualStep> steps, IReadOnlyCollection<ActualImage> images)
        {
            Assembly = assembly;
            PluginTypes = pluginTypes;
            Steps = steps;
            Images = images;
        }

        public ActualPluginAssembly Assembly { get; }
        public IReadOnlyDictionary<string, ActualPluginType> PluginTypes { get; }
        public IReadOnlyCollection<ActualStep> Steps { get; }
        public IReadOnlyCollection<ActualImage> Images { get; }
    }

    public sealed class ActualPluginAssembly
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
    }

    public sealed class ActualPluginType
    {
        public Guid Id { get; set; }
        public string TypeName { get; set; }
    }

    public sealed class ActualStep
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string PluginTypeName { get; set; }
        public Guid PluginTypeId { get; set; }
        public Guid MessageId { get; set; }
        public Guid? MessageFilterId { get; set; }
        public string MessageName { get; set; }
        public string EntityLogicalName { get; set; }
        public int Stage { get; set; }
        public int Mode { get; set; }
        public int Rank { get; set; }
        public AttributeList FilteringAttributes { get; set; }
        public int StateCode { get; set; }
        public bool IsManaged { get; set; }
        public string UnsecureConfiguration { get; set; }
        public EntityReference ImpersonatingUserId { get; set; }
        public string Description { get; set; }

        public string Key => RegistrationKeys.Step(PluginTypeName, MessageName, EntityLogicalName, Stage, Mode);
    }

    public sealed class ActualImage
    {
        public Guid Id { get; set; }
        public Guid StepId { get; set; }
        public string StepKey { get; set; }
        public string Alias { get; set; }
        public int ImageType { get; set; }
        public string MessagePropertyName { get; set; }
        public AttributeList Attributes { get; set; }

        public string Key => RegistrationKeys.Image(StepKey, Alias, ImageType);
    }

    public sealed class RegistrationChange
    {
        public RegistrationActionKind Action { get; set; }
        public RegistrationTargetKind Target { get; set; }
        public string PluginTypeName { get; set; }
        public string MessageName { get; set; }
        public string EntityLogicalName { get; set; }
        public string Detail { get; set; }
        public DesiredStep DesiredStep { get; set; }
        public ActualStep ActualStep { get; set; }
        public DesiredImage DesiredImage { get; set; }
        public ActualImage ActualImage { get; set; }
        public bool Applied { get; set; }
    }

    public sealed class RegistrationPlan
    {
        public RegistrationPlan(IReadOnlyCollection<RegistrationChange> changes)
        {
            Changes = changes;
        }

        public IReadOnlyCollection<RegistrationChange> Changes { get; }
        public int Creates => Count(RegistrationActionKind.Create);
        public int Updates => Count(RegistrationActionKind.Update);
        public int Extras => Count(RegistrationActionKind.Extra);
        public int Warnings => Count(RegistrationActionKind.Warning);
        public int Errors => Count(RegistrationActionKind.Error);

        private int Count(RegistrationActionKind kind)
        {
            var count = 0;
            foreach (var change in Changes)
            {
                if (change.Action == kind) count++;
            }
            return count;
        }
    }
}

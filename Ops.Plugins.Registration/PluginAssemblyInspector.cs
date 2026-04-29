using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xrm.Sdk;

namespace Ops.Plugins.Registration
{
    public sealed class PluginAssemblyInspector
    {
        private const int PreImage = 0;
        private const int PostImage = 1;

        public DesiredRegistration Inspect(string assemblyPath)
        {
            if (string.IsNullOrWhiteSpace(assemblyPath)) throw new ArgumentNullException(nameof(assemblyPath));
            var fullPath = Path.GetFullPath(assemblyPath);
            if (!File.Exists(fullPath)) throw new FileNotFoundException("Plugin assembly was not found.", fullPath);

            var directory = Path.GetDirectoryName(fullPath);
            ResolveEventHandler resolver = (sender, args) => ResolveFromDirectory(directory, args.Name);
            AppDomain.CurrentDomain.AssemblyResolve += resolver;

            try
            {
                var assembly = Assembly.LoadFrom(fullPath);
                var concretePluginTypes = assembly.GetTypes()
                    .Where(IsConcretePluginType)
                    .OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var pluginTypes = concretePluginTypes
                    .Select(ReadPluginType)
                    .Where(t => t.Steps.Any())
                    .ToArray();

                return new DesiredRegistration(
                    assembly.GetName().Name,
                    pluginTypes,
                    concretePluginTypes.Select(t => t.FullName).ToArray());
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= resolver;
            }
        }

        private static Assembly ResolveFromDirectory(string directory, string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(directory)) return null;
            var name = new AssemblyName(assemblyName).Name + ".dll";
            var candidate = Path.Combine(directory, name);
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        }

        private static bool IsConcretePluginType(Type type)
        {
            return type != null
                && type.IsClass
                && !type.IsAbstract
                && typeof(IPlugin).IsAssignableFrom(type);
        }

        private static DesiredPluginType ReadPluginType(Type type)
        {
            var instance = Activator.CreateInstance(type);
            var method = FindRegisteredEventsMethod(type);
            var events = method == null
                ? Enumerable.Empty<object>()
                : ((IEnumerable)method.Invoke(instance, null)).Cast<object>();

            var steps = events.Select(e => ReadStep(type.FullName, e)).ToArray();
            return new DesiredPluginType(type.FullName, steps);
        }

        private static MethodInfo FindRegisteredEventsMethod(Type type)
        {
            while (type != null)
            {
                var method = type.GetMethod("GetRegisteredEvents", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (method != null) return method;
                type = type.BaseType;
            }

            return null;
        }

        private static DesiredStep ReadStep(string pluginTypeName, object registeredEvent)
        {
            var messageName = GetValue<string>(registeredEvent, "MessageName");
            var entityLogicalName = GetValue<string>(registeredEvent, "EntityLogicalName");
            var stage = Convert.ToInt32(GetProperty(registeredEvent, "Stage").GetValue(registeredEvent, null));
            var mode = Convert.ToInt32(GetProperty(registeredEvent, "Mode").GetValue(registeredEvent, null));
            var rankProperty = registeredEvent.GetType().GetProperty("Rank");
            var rank = rankProperty == null ? 1 : Convert.ToInt32(rankProperty.GetValue(registeredEvent, null));
            var runInUserContextProperty = registeredEvent.GetType().GetProperty("RunInUserContext");
            var runInUserContext = runInUserContextProperty == null ? "Calling User" : runInUserContextProperty.GetValue(registeredEvent, null)?.ToString();
            var descriptionProperty = registeredEvent.GetType().GetProperty("StepDescription");
            var description = descriptionProperty == null ? null : descriptionProperty.GetValue(registeredEvent, null)?.ToString();

            var images = new List<DesiredImage>();
            AddImage(images, pluginTypeName, messageName, entityLogicalName, stage, mode, registeredEvent, "RequiredPreImageName", "PreImageAttributes", PreImage);
            AddImage(images, pluginTypeName, messageName, entityLogicalName, stage, mode, registeredEvent, "RequiredPostImageName", "PostImageAttributes", PostImage);

            return new DesiredStep
            {
                PluginTypeName = pluginTypeName,
                MessageName = messageName,
                EntityLogicalName = entityLogicalName,
                Stage = stage,
                Mode = mode,
                Rank = rank,
                FilteringAttributes = AttributeList.From(GetValues(registeredEvent, "FilteringAttributes")),
                Images = images,
                RunInUserContext = runInUserContext,
                Description = description
            };
        }

        private static void AddImage(List<DesiredImage> images, string pluginTypeName, string messageName, string entityLogicalName, int stage, int mode, object registeredEvent, string aliasProperty, string attributesProperty, int imageType)
        {
            var alias = GetValue<string>(registeredEvent, aliasProperty);
            if (string.IsNullOrWhiteSpace(alias)) return;

            images.Add(new DesiredImage
            {
                PluginTypeName = pluginTypeName,
                MessageName = messageName,
                EntityLogicalName = entityLogicalName,
                StepStage = stage,
                StepMode = mode,
                Alias = alias,
                ImageType = imageType,
                MessagePropertyName = SdkMessagePropertyNames.Target,
                Attributes = AttributeList.From(GetValues(registeredEvent, attributesProperty))
            });
        }

        private static T GetValue<T>(object instance, string propertyName)
        {
            var value = GetProperty(instance, propertyName).GetValue(instance, null);
            return value == null ? default(T) : (T)value;
        }

        private static IEnumerable<string> GetValues(object instance, string propertyName)
        {
            var value = GetProperty(instance, propertyName).GetValue(instance, null) as IEnumerable;
            if (value == null) return Enumerable.Empty<string>();
            return value.Cast<object>().Select(v => v?.ToString());
        }

        private static PropertyInfo GetProperty(object instance, string propertyName)
        {
            var property = instance.GetType().GetProperty(propertyName);
            if (property == null)
                throw new InvalidOperationException($"RegisteredEvent metadata is missing property '{propertyName}'.");
            return property;
        }
    }
}

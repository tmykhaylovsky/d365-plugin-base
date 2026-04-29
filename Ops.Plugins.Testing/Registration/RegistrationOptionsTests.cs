using System;
using System.IO;
using Ops.Plugins.Registration;
using Ops.Plugins.Shared;
using Xunit;

namespace Ops.Plugins.Testing.Registration
{
    public class RegistrationOptionsTests
    {
        [Fact]
        public void Parse_LoadsRunInUserContextArray()
        {
            var id = Guid.NewGuid();
            var path = WriteTempJson($@"[
  {{ ""label"": ""Calling User"", ""systemuserid"": null, ""fullname"": ""Calling User"" }},
  {{ ""label"": ""System Admin"", ""systemuserid"": ""{id:D}"", ""fullname"": ""# crm-prod-dataenrichment"" }}
]");

            try
            {
                var options = RegistrationOptions.Parse(new[] { "--userMap", path });

                Assert.True(options.UserAliases.TryGetValue(RunInUserContext.SystemAdmin, out var actualId));
                Assert.Equal(id, actualId);
                Assert.Equal("# crm-prod-dataenrichment", options.UserReferences[RunInUserContext.SystemAdmin].FullName);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ValidateRunInUserContexts_RejectsMissingFixedLabel()
        {
            var options = new RegistrationOptions();
            var desired = new DesiredRegistration(
                "Ops.Plugins",
                new[]
                {
                    new DesiredPluginType("Ops.Plugins.TestPlugin", new[]
                    {
                        new DesiredStep { RunInUserContext = RunInUserContext.SystemAdmin }
                    })
                });

            var ex = Assert.Throws<InvalidOperationException>(() => options.ValidateRunInUserContexts(desired));
            Assert.Contains(RunInUserContext.SystemAdmin, ex.Message);
        }

        [Theory]
        [InlineData("https://<org>.crm.dynamics.com")]
        [InlineData("https://<your-org>.crm.dynamics.com")]
        [InlineData("https://org.crm.dynamics.com")]
        [InlineData("https://your-org.crm.dynamics.com")]
        public void ValidateForRun_RejectsPlaceholderEnvironmentUrl(string environmentUrl)
        {
            var assemblyPath = WriteTempJson("{}");
            var options = new RegistrationOptions
            {
                AssemblyPath = assemblyPath,
                EnvironmentUrl = environmentUrl
            };

            try
            {
                var ex = Assert.Throws<ArgumentException>(() => options.ValidateForRun());

                Assert.Contains("placeholder", ex.Message);
                Assert.Contains(environmentUrl, ex.Message);
                Assert.Contains("https://contoso.crm.dynamics.com", ex.Message);
            }
            finally
            {
                File.Delete(assemblyPath);
            }
        }

        private static string WriteTempJson(string content)
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, content);
            return path;
        }
    }
}

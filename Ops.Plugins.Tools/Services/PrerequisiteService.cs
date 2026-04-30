namespace Ops.Plugins.Tools.Services;

public sealed class PrerequisiteService
{
    public IReadOnlyList<PrerequisiteStatus> Check()
    {
        return
        [
            CheckDotNetSdk(),
            CheckNetFrameworkTargetingPack(),
            CheckPowerShell(),
            CheckPac()
        ];
    }

    private static PrerequisiteStatus CheckDotNetSdk()
    {
        var dotnet = ProcessRunner.FindOnPath("dotnet.exe");
        if (dotnet is null)
        {
            return PrerequisiteStatus.Missing(
                ".NET SDK",
                "Install the .NET 8 SDK x64. It is required to build projects and run dotnet-based scripts.");
        }

        var sdkRoot = Path.Combine(Path.GetDirectoryName(dotnet) ?? string.Empty, "sdk");
        var hasSdk = Directory.Exists(sdkRoot) && Directory.EnumerateDirectories(sdkRoot).Any();
        return hasSdk
            ? PrerequisiteStatus.Found(".NET SDK", dotnet)
            : PrerequisiteStatus.Missing(".NET SDK", "Install the .NET 8 SDK x64. The dotnet host exists, but no SDK folder was found.");
    }

    private static PrerequisiteStatus CheckNetFrameworkTargetingPack()
    {
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var frameworkList = Path.Combine(
            programFilesX86,
            "Reference Assemblies",
            "Microsoft",
            "Framework",
            ".NETFramework",
            "v4.6.2",
            "RedistList",
            "FrameworkList.xml");

        return File.Exists(frameworkList)
            ? PrerequisiteStatus.Found(".NET Framework 4.6.2 targeting pack", frameworkList)
            : PrerequisiteStatus.Missing(
                ".NET Framework 4.6.2 targeting pack",
                "Install the .NET Framework 4.6.2 Developer Pack. The plugin project targets net462.");
    }

    private static PrerequisiteStatus CheckPowerShell()
    {
        var powershell = ProcessRunner.FindOnPath("pwsh.exe") ?? ProcessRunner.FindOnPath("powershell.exe");
        return powershell is null
            ? PrerequisiteStatus.Missing("PowerShell", "Install PowerShell 7 or enable Windows PowerShell. Scripts run through PowerShell.")
            : PrerequisiteStatus.Found("PowerShell", powershell);
    }

    private static PrerequisiteStatus CheckPac()
    {
        var pac = PacCommandFactory.FindPac();
        return pac is null
            ? PrerequisiteStatus.Missing("Power Platform CLI", "Install Microsoft Power Platform CLI so pac auth, modelbuilder, and plugin deploy commands can run.")
            : PrerequisiteStatus.Found("Power Platform CLI", pac);
    }
}

public sealed class PrerequisiteStatus
{
    private PrerequisiteStatus(string name, bool isFound, string detail)
    {
        Name = name;
        IsFound = isFound;
        Detail = detail;
    }

    public string Name { get; }
    public bool IsFound { get; }
    public string Detail { get; }

    public static PrerequisiteStatus Found(string name, string detail) => new(name, true, detail);

    public static PrerequisiteStatus Missing(string name, string detail) => new(name, false, detail);
}

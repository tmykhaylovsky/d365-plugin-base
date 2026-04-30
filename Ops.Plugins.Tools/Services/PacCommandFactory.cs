namespace Ops.Plugins.Tools.Services;

public static class PacCommandFactory
{
    public static (string FileName, string[] Arguments) CheckVersion() => Create(["help"]);

    public static (string FileName, string[] Arguments) AuthCreate(string url, string name) =>
        Create(["auth", "create", "--environment", url, "--deviceCode", "--name", name]);

    public static (string FileName, string[] Arguments) AuthList() => Create(["auth", "list"]);

    public static (string FileName, string[] Arguments) AuthSelect(string index) => Create(["auth", "select", "--index", index]);

    public static (string FileName, string[] Arguments) OrgWho() => Create(["env", "who"]);

    private static (string FileName, string[] Arguments) Create(string[] arguments)
    {
        var pacPath = FindPac();
        if (pacPath is null)
        {
            return ("pac", arguments);
        }

        if (pacPath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
            pacPath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            return ("cmd.exe", ["/d", "/c", pacPath, .. arguments]);
        }

        return (pacPath, arguments);
    }

    public static string? FindPac()
    {
        return ProcessRunner.FindOnPath("pac.exe") ??
            ProcessRunner.FindOnPath("pac.cmd") ??
            FindInLocalAppData();
    }

    private static string? FindInLocalAppData()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return null;
        }

        var path = Path.Combine(localAppData, "Microsoft", "PowerAppsCLI", "pac.cmd");
        return File.Exists(path) ? path : null;
    }
}

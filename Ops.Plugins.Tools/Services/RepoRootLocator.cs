namespace Ops.Plugins.Tools.Services;

public static class RepoRootLocator
{
    public static string FindRepoRoot(string? preferredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredPath) && IsRepoRoot(preferredPath))
        {
            return Path.GetFullPath(preferredPath);
        }

        return FindRepoRoot();
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (IsRepoRoot(directory.FullName))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (IsRepoRoot(directory.FullName))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root containing Ops.Plugins.slnx.");
    }

    public static bool IsRepoRoot(string path)
    {
        return Directory.Exists(path) &&
            (File.Exists(Path.Combine(path, "Ops.Plugins.slnx")) ||
                Directory.Exists(Path.Combine(path, "Scripts")));
    }
}

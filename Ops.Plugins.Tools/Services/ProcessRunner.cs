using System.Diagnostics;
using Ops.Plugins.Tools.Models;

namespace Ops.Plugins.Tools.Services;

public sealed class ProcessRunner
{
    private readonly string _repoRoot;
    private Process? _activeProcess;

    public ProcessRunner(string repoRoot) => _repoRoot = repoRoot;

    public bool IsRunning => _activeProcess is { HasExited: false };

    public async Task<CommandResult> RunAsync(string fileName, IEnumerable<string> arguments, Action<string> output, CancellationToken cancellationToken)
    {
        var start = DateTimeOffset.UtcNow;
        using var process = new Process();
        _activeProcess = process;
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = _repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output("ERR: " + e.Data); };

        output($"> {fileName} {string.Join(" ", arguments.Select(QuoteForDisplay))}");
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new CommandResult { ExitCode = process.ExitCode, Duration = DateTimeOffset.UtcNow - start };
        }
        catch (OperationCanceledException)
        {
            KillActiveProcessTree(output);
            return new CommandResult { ExitCode = -1, Duration = DateTimeOffset.UtcNow - start, Cancelled = true };
        }
        finally
        {
            _activeProcess = null;
        }
    }

    public void KillActiveProcessTree(Action<string> output)
    {
        try
        {
            if (_activeProcess is { HasExited: false })
            {
                _activeProcess.Kill(entireProcessTree: true);
                output("Process cancelled.");
            }
        }
        catch (Exception ex)
        {
            output("Cancel failed: " + ex.Message);
        }
    }

    public static string FindPowerShell()
    {
        return FindOnPath("pwsh.exe") ?? FindOnPath("powershell.exe") ?? "powershell.exe";
    }

    public static string? FindOnPath(string executable)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator);
        return paths.Select(path => Path.Combine(path, executable)).FirstOrDefault(File.Exists);
    }

    private static string QuoteForDisplay(string argument) => argument.Contains(' ') ? $"\"{argument}\"" : argument;
}

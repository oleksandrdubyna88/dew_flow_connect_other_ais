using System.Diagnostics;

namespace CoaiBench.Running;

/// <summary>One git call, captured. The bench needs three: a ref per run, a file at a commit, nothing else.</summary>
public static class Git
{
    public static async Task<(int ExitCode, string Output)> RunAsync(
        string repo, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var start = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repo,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var git = Process.Start(start);
        if (git is null)
        {
            return (-1, string.Empty);
        }

        var output = await git.StandardOutput.ReadToEndAsync(ct);
        await git.WaitForExitAsync(ct);

        return (git.ExitCode, output);
    }
}

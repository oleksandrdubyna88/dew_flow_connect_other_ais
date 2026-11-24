using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Install;

/// <summary>One vendor command's answer: whether it succeeded, and a sentence for when it did not.</summary>
internal sealed record VendorAnswer(bool Ok, string Reason)
{
    internal static VendorAnswer Success { get; } = new(true, string.Empty);
}

/// <summary>
/// Runs the clients' own commands (<c>claude mcp add-json</c>, <c>codex mcp add</c>, …) on this side, with the host's four variables as the child's and a bounded time (30 s).
/// </summary>
/// <remarks>
/// <para>The reason of a failure is the command's first line of stderr, cut — and with every environment VALUE the run knows scrubbed out of it, because a vendor's error text
/// may quote what it was handed, and the output of this mode never carries a value.</para>
/// </remarks>
internal sealed class VendorCommands(InstallHost host, IProcessLauncher launcher, TimeSpan timeout, IReadOnlyCollection<string> secrets)
{
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    internal async Task<VendorAnswer> RunAsync(string executable, IReadOnlyList<string> args, string stdin, CancellationToken ct)
    {
        try
        {
            var request = new ProcessRequest(executable, args, WorkingDirectory())
            {
                Environment = host.VendorEnvironment,
                StdIn = stdin,
                Timeout = timeout,
            };
            var answer = await launcher.RunAsync(request, ct);
            if (answer.TimedOut)
            {
                return new VendorAnswer(false, $"`{Describe(executable, args)}` did not finish within {timeout.TotalSeconds:0} s");
            }

            return answer.ExitCode == 0
                ? VendorAnswer.Success
                : new VendorAnswer(false, Cut(Scrub($"`{Describe(executable, args)}` exited {answer.ExitCode}: {FirstLine(answer.StdErr.Length > 0 ? answer.StdErr : answer.StdOut)}")));
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return new VendorAnswer(false, Scrub($"`{Describe(executable, args)}` could not start: {e.Message}"));
        }
    }

    /// <summary>The command's words for a sentence: the CLI's name and its subcommand — never its arguments, which can carry an environment value.</summary>
    private static string Describe(string executable, IReadOnlyList<string> args) =>
        $"{Path.GetFileNameWithoutExtension(executable)} {string.Join(' ', args.TakeWhile(arg => !arg.StartsWith('-')).Take(2))}".TrimEnd();

    private string WorkingDirectory() => host.HasHome && Directory.Exists(host.Home) ? host.Home : AppContext.BaseDirectory;

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

    /// <summary>Cut AFTER the scrub: a value that straddled the cut would otherwise leave its first half in the sentence.</summary>
    private static string Cut(string line) => line.Length > 200 ? line[..200] : line;

    private string Scrub(string text) =>
        secrets.Where(secret => secret.Length >= 3).Aggregate(text, (clean, secret) => clean.Replace(secret, "***", StringComparison.Ordinal));
}

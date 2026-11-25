using System.Text;

namespace CoaiMcp.Tests;

/// <summary>
/// What a person on the same machine could read about a live <c>systemd-run --user</c> unit of a coder job, read the way that person would (epoch 3 E2 s7, the second and third security judgements): the unit's <c>Environment</c> property, the transient unit
/// file systemd-run writes (mode 0644 in a 0700 folder), the command line of every process, and the environment of the processes that carry the unit's own name — the <c>systemd-run</c> client. A test polls these while a launch runs and asserts that no secret is in
/// any of them. Linux only; the callers skip where there is no user manager.
/// </summary>
internal static class UnitLeakWatch
{
    /// <summary>What <c>systemctl --user show -p Environment</c> says of the unit (empty when it is not there); read with the product's own program names, never through a shell.</summary>
    internal static string UnitProperties(string unit)
    {
        try
        {
            using var shown = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("systemctl") { ArgumentList = { "--user", "show", "-p", "Environment", unit }, RedirectStandardOutput = true, UseShellExecute = false })!;
            var text = shown.StandardOutput.ReadToEnd();
            shown.WaitForExit();

            return text;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            return string.Empty;
        }
    }

    /// <summary>The text of every transient unit file of this job under the user's runtime folder (the file systemd-run makes, mode 0644 in a 0700 folder).</summary>
    internal static string TransientFiles(string jobId)
    {
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } named ? named : "/run/user/" + Environment.GetEnvironmentVariable("UID");
        var folder = Path.Combine(runtime, "systemd", "transient");
        if (!Directory.Exists(folder))
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(folder, $"*{jobId}*"))
        {
            try
            {
                text.AppendLine(File.ReadAllText(file));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The unit ended while it was read.
            }
        }

        return text.ToString();
    }

    /// <summary>The command line of every process of the machine this user may read, joined; a process that ended while it was read is skipped.</summary>
    internal static string Cmdlines() => Read("cmdline", _ => true);

    /// <summary>
    /// The environment of every process whose command line names <paramref name="unit"/> — the <c>systemd-run</c> client of that unit, the one process that is started with the request's environment. The child inside the unit is NOT among them: its command
    /// line is the program's, and its environment is meant to hold what the CLI needs.
    /// </summary>
    internal static string ClientEnvironments(string unit) => Read("environ", cmdline => cmdline.Contains(unit, StringComparison.Ordinal));

    /// <summary>How many processes whose command line names <paramref name="unit"/> were found: the proof that <see cref="ClientEnvironments"/> had a client to read.</summary>
    internal static int Clients(string unit) => Read("cmdline", cmdline => cmdline.Contains(unit, StringComparison.Ordinal)).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

    private static string Read(string what, Func<string, bool> of)
    {
        var text = new StringBuilder();
        foreach (var folder in Directory.EnumerateDirectories("/proc").Where(candidate => long.TryParse(Path.GetFileName(candidate), out _)))
        {
            try
            {
                var cmdline = File.ReadAllText(Path.Combine(folder, "cmdline")).Replace('\0', ' ');
                if (of(cmdline))
                {
                    text.AppendLine(File.ReadAllText(Path.Combine(folder, what)).Replace('\0', ' '));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A process that ended while it was read, or one of another user.
            }
        }

        return text.ToString();
    }
}

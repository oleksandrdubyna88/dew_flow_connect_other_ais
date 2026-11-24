using System.Diagnostics;

namespace CoaiMcp.Tests;

/// <summary>
/// Whether this machine has a systemd USER manager a test can start units on — the condition the systemd cases of the detached-runner tests are skipped on, with the reason a person can act on. Asked by running the
/// product's own road: <c>systemd-run --user --collect</c> of a program that exits at once, exit code 0 or not (a missing manager is a fast non-zero exit of <c>systemd-run</c> itself, <c>v2/spikes/RESULTS_detach.md</c> §2).
/// </summary>
internal static class SystemdUser
{
    private static readonly Lazy<(bool Works, string Why)> Answer = new(Ask);

    internal static bool Works(out string why)
    {
        (var works, why) = Answer.Value;

        return works;
    }

    /// <summary>The text <c>systemctl --user show</c> prints for the properties of a unit, or the sentence that says it could not be asked.</summary>
    internal static string Show(string unit, params string[] properties)
    {
        var info = new ProcessStartInfo("systemctl") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("--user");
        info.ArgumentList.Add("show");
        info.ArgumentList.Add(unit + ".service");
        foreach (var property in properties)
        {
            info.ArgumentList.Add("-p");
            info.ArgumentList.Add(property);
        }

        using var show = Process.Start(info)!;
        var text = show.StandardOutput.ReadToEnd();
        show.WaitForExit(10_000);

        return text;
    }

    /// <summary>True while a unit of that name is loaded and not finished.</summary>
    internal static bool IsActive(string unit) => Show(unit, "ActiveState").Contains("ActiveState=active", StringComparison.Ordinal);

    private static (bool Works, string Why) Ask()
    {
        if (!OperatingSystem.IsLinux())
        {
            return (false, "systemd units exist on Linux only");
        }

        try
        {
            var info = new ProcessStartInfo("systemd-run") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "--user", "--quiet", "--collect", "--wait", "/bin/true" })
            {
                info.ArgumentList.Add(arg);
            }

            using var probe = Process.Start(info)!;
            var said = probe.StandardError.ReadToEnd().Trim();
            probe.WaitForExit(10_000);

            return probe.ExitCode == 0 ? (true, string.Empty) : (false, $"no systemd user manager here: `systemd-run --user` exited {probe.ExitCode} ({said})");
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            return (false, $"no systemd-run on this machine ({e.Message})");
        }
    }
}

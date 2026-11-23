using System.Globalization;

namespace CoaiMcp.Runners.Toolchain;

/// <summary>
/// The description of one check unit — the <c>systemd-run --user</c> properties, the environment and the argv — as pure values. Nothing here starts a process; <see cref="CheckBoundary"/> runs what this describes, and
/// the preflight asks the OS whether the description held.
/// </summary>
/// <remarks>
/// <para><b>These are requests until the preflight has seen the answer.</b> A property handed to <c>systemd-run</c> is the manager's decision (<c>common/security.md</c>, <i>a measure you have not OBSERVED working is a comment</i>): a sandbox
/// option can fail open, which the measured R7 did for claude's own sandbox. So no test of this class asserts that a property is in the list and calls it a boundary; the effects are asserted on a real unit
/// (<c>ACheckBoundaryUnitTests</c>) and by the preflight before each job (<c>BoundaryPreflight</c>).</para>
/// <para><b>The property set</b> is the must-have list of <c>v2/spikes/RESULTS_coder_write_boundary_judgement.md</c> § 2, plus four hardening properties (<c>RestrictSUIDSGID</c>, <c>LockPersonality</c>, <c>ProtectKernelTunables</c>,
/// <c>ProtectControlGroups</c>) that the real build and test of this repository ran under (<c>RESULTS_s2_check_boundary.md</c>).</para>
/// <para><b>The environment is set INSIDE the unit</b> by <c>env -i</c>. The program that starts a unit is <c>systemd-run</c>; the unit gets the user manager's environment — measured to hold <c>SSH_AUTH_SOCK</c>,
/// <c>DBUS_SESSION_BUS_ADDRESS</c>, <c>GPG_AGENT_INFO</c> and <c>XDG_RUNTIME_DIR</c> — and a list set on the client would never reach it.</para>
/// </remarks>
internal static class BoundaryUnit
{
    /// <summary>The prefix of every unit this class names; a leftover is found by it.</summary>
    internal const string UnitPrefix = "coai-check-";

    /// <summary>The unit's name for one part of a job (<c>pre-none</c>, <c>pre-host</c>, <c>s1</c>, <c>s2</c>…), without the <c>.service</c> suffix.</summary>
    internal static string UnitName(BoundaryJob job, string part) => $"{UnitPrefix}{job.JobId}-{part}";

    /// <summary>The <c>-p</c> properties of one unit, in the order they are given.</summary>
    internal static IReadOnlyList<string> Properties(BoundaryHost host, BoundaryJob job, CheckNetwork network, TimeSpan runtimeMax)
    {
        var tree = job.Tree;

        return
        [
            "ProtectSystem=strict",
            "ProtectHome=tmpfs",
            "TemporaryFileSystem=/run/user",
            "TemporaryFileSystem=/mnt",
            "TemporaryFileSystem=/dev/shm",
            "InaccessiblePaths=-/run/WSL",
            $"BindPaths={tree.Root}",
            $"BindReadOnlyPaths={tree.MainCheckout}",
            $"BindReadOnlyPaths={tree.GitEntry}",
            $"BindPaths={job.JobRoot}",
            $"BindPaths={job.TempFolder}:/tmp",
            $"BindPaths={job.VarTempFolder}:/var/tmp",
            .. host.ReadOnlyCaches.Select(cache => $"BindReadOnlyPaths={cache}"),
            "NoNewPrivileges=yes",
            "PrivateUsers=yes",
            "RestrictSUIDSGID=yes",
            "LockPersonality=yes",
            "ProtectKernelTunables=yes",
            "ProtectControlGroups=yes",
            network == CheckNetwork.Private ? "PrivateNetwork=yes" : "PrivateNetwork=no",
            $"RuntimeMaxSec={Math.Max(1, (long)Math.Ceiling(runtimeMax.TotalSeconds)).ToString(CultureInfo.InvariantCulture)}",
            $"MemoryMax={job.MemoryMegabytes.ToString(CultureInfo.InvariantCulture)}M",
            "MemorySwapMax=0",
        ];
    }

    /// <summary>
    /// The whole environment of a step, as it is set by <c>env -i</c> inside the unit: <c>PATH</c> of system folders, the job's own <c>HOME</c>, <c>DOTNET_CLI_HOME</c>, <c>NUGET_PACKAGES</c> and <c>npm_config_cache</c>, the
    /// shared package folder as a fallback, <c>TMPDIR</c>, a locale and the switches the step asked for — and nothing else. No vendor key, no <c>COAI_</c> name, no agent or bus socket.
    /// </summary>
    internal static IReadOnlyList<KeyValuePair<string, string>> Environment(BoundaryHost host, BoundaryJob job, IReadOnlyDictionary<string, string?> extra)
    {
        List<KeyValuePair<string, string>> list =
        [
            new("PATH", host.UnitPath),
            new("HOME", job.HomeFolder),
            new("DOTNET_CLI_HOME", job.DotnetFolder),
            new("NUGET_PACKAGES", job.NuGetFolder),
            new("npm_config_cache", job.NpmFolder),
            new("TMPDIR", "/tmp"),
            new("LANG", "C.UTF-8"),
            new("TERM", "dumb"),
            new("GIT_TERMINAL_PROMPT", "0"),
        ];
        if (host.NuGetPackages.Length > 0 && Directory.Exists(host.NuGetPackages))
        {
            list.Add(new("NUGET_FALLBACK_PACKAGES", host.NuGetPackages));
        }

        list.AddRange(extra.Where(item => item.Value is not null).Select(item => new KeyValuePair<string, string>(item.Key, item.Value!)));

        return list;
    }

    /// <summary>The argv of <c>systemd-run</c>: the unit, its folder, every property, then <c>env -i</c> with the allow-list and the command.</summary>
    /// <param name="degrade">For a test only (a RED that must see the preflight refuse): rewrites the finished argument list. Production passes nothing.</param>
    /// <exception cref="ArgumentException">The program's name has an <c>=</c> (<c>env</c> would read it as an assignment), or the argv is empty.</exception>
    internal static IReadOnlyList<string> Arguments(BoundaryHost host, BoundaryJob job, string unit, CheckNetwork network, TimeSpan runtimeMax,
        IReadOnlyDictionary<string, string?> extra, IReadOnlyList<string> command, Func<IReadOnlyList<string>, IReadOnlyList<string>>? degrade = null)
    {
        if (command.Count == 0 || command[0].Contains('=', StringComparison.Ordinal) || command[0].StartsWith('-'))
        {
            throw new ArgumentException("the program to run in the unit is empty, starts with '-' or holds '='", nameof(command));
        }

        IReadOnlyList<string> arguments =
        [
            "--user", "--wait", "--pipe", "--collect", "--quiet",
            "--unit", unit,
            "--working-directory", job.Tree.Root,
            .. Properties(host, job, network, runtimeMax).SelectMany(property => new[] { "-p", property }),
            "--",
            "/usr/bin/env", "-i",
            .. Environment(host, job, extra).Select(pair => $"{pair.Key}={pair.Value}"),
            .. command,
        ];

        return degrade is null ? arguments : degrade(arguments);
    }
}

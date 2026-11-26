using System.Globalization;
using CoaiMcp.Runners.Detach;

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
/// <c>ProtectControlGroups</c>) and <c>Nice=10</c> (a check runs below the person's own work, <c>common/local-cpu-budget.md</c> § 2). The real build and the whole test suite of this repository ran under all of it (<c>RESULTS_s2_check_boundary.md</c>).</para>
/// <para><b>The environment is set INSIDE the unit</b> by <c>env -i</c>. The program that starts a unit is <c>systemd-run</c>; the unit gets the user manager's environment — measured to hold <c>SSH_AUTH_SOCK</c>,
/// <c>DBUS_SESSION_BUS_ADDRESS</c>, <c>GPG_AGENT_INFO</c> and <c>XDG_RUNTIME_DIR</c> — and a list set on the client would never reach it.</para>
/// </remarks>
internal static class BoundaryUnit
{
    /// <summary>The prefix of every unit this class names; a leftover is found by it.</summary>
    internal const string UnitPrefix = "coai-check-";

    /// <summary>What a step sees as <c>/tmp</c>: the job's own folder bound there. Named once so a test refers to it without a raw temp path of its own.</summary>
    internal const string UnitTemp = "/tmp";

    /// <summary>What a step sees as <c>/var/tmp</c>.</summary>
    internal const string UnitVarTemp = "/var/tmp";

    /// <summary>The unit's name for one part of a job (<c>pre-none</c>, <c>pre-host</c>, <c>s1</c>, <c>s2</c>…), without the <c>.service</c> suffix.</summary>
    internal static string UnitName(BoundaryJob job, string part) => $"{UnitPrefix}{job.JobId}-{part}";

    /// <summary>The <c>-p</c> properties of one unit, in the order they are given.</summary>
    internal static IReadOnlyList<string> Properties(BoundaryHost host, BoundaryJob job, CheckNetwork network, TimeSpan runtimeMax)
    {
        var tree = job.Tree;

        return
        [
            .. Isolation,
            $"BindPaths={tree.Root}",
            $"BindReadOnlyPaths={tree.MainCheckout}",
            $"BindReadOnlyPaths={tree.GitEntry}",
            .. Mounts(job),
            .. JobView(job),
            $"BindPaths={job.TempFolder}:{UnitTemp}",
            $"BindPaths={job.VarTempFolder}:{UnitVarTemp}",
            .. KeysHidden(job),
            .. SignInHidden(host),
            .. host.ReadOnlyCaches.Select(cache => $"BindReadOnlyPaths={cache}"),
            .. network == CheckNetwork.Host && host.ResolverFile.Length > 0 ? [$"BindReadOnlyPaths={host.ResolverFile}"] : Array.Empty<string>(),
            .. Hardening,
            network == CheckNetwork.Private ? "PrivateNetwork=yes" : "PrivateNetwork=no",
            .. Limits(job, runtimeMax),
        ];
    }

    /// <summary>
    /// The submodule mounts of the job, one property each (epoch 3 E2 s6, the recorded change): a folder of coai's own bound READ-ONLY at its path in the worktree (<c>BindReadOnlyPaths=&lt;source&gt;:&lt;target&gt;</c>), after the worktree's own <c>.git</c> entry and
    /// before anything that binds a folder of the job. Nothing else of the list changes: a job with no mount has exactly the list E2 recorded, and one with a mount has that list and these elements.
    /// </summary>
    internal static IReadOnlyList<string> Mounts(BoundaryJob job) => [.. job.Mounts.Select(mount => $"BindReadOnlyPaths={mount.Source}:{mount.Target}")];

    /// <summary>
    /// What hides the claude key pool's folder and the keys file from a unit, by their REAL paths (resolved once where the job is made, <see cref="BoundaryJob.RealOrSpelled"/>) (<c>InaccessiblePaths=-&lt;path&gt;</c>: the dash — a path that is not there is no error). A path under a hidden folder (<c>/home</c>) is out of sight already; this makes it true where the data
    /// folder is somewhere else (<c>/var/lib/coai</c>). One list for a check unit and a coder's unit (<see cref="CoderUnit.KeysHidden"/> only adds the device switch): the key material is in no unit of this family, and each has a preflight row that reads it from inside.
    /// </summary>
    internal static IReadOnlyList<string> KeysHidden(BoundaryJob job) =>
        [.. new[] { job.KeysFolder, job.KeysFile }.Where(path => path.Length > 0).Select(path => $"InaccessiblePaths=-{path}")];

    /// <summary>The person's own sign-in file at its real path, hidden from a check unit where the home is not under a hidden folder: a check never needs it (a coder's unit gets it bound at a path of its own home, never at this one).</summary>
    internal static IReadOnlyList<string> SignInHidden(BoundaryHost host) =>
        host.ResolvedHome is { Length: > 0 } home && System.IO.Path.IsPathFullyQualified(home) && !home.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is ':' or '\\')
            ? [$"InaccessiblePaths=-{BoundaryJob.RealOrSpelled(System.IO.Path.Combine(home, ".claude", ".credentials.json"))}"]
            : [];

    /// <summary>
    /// What a check unit sees of the job's own folder (epoch 3 E2, the E3 judgement of the write boundary): an empty READ-ONLY tmpfs over the job folder, and on top of it only the folders the checks use (<see cref="BoundaryJob.CheckFolders"/>), each bound writable. The job
    /// folder also holds the CODER's home and config (<see cref="BoundaryJob.CoderFolder"/>) — its CLI's settings, its transcripts, its caches — which a check, running code the coder wrote, must neither read nor change; before this, the whole job folder was bound writable and the
    /// coder's folder with it.
    /// </summary>
    internal static IReadOnlyList<string> JobView(BoundaryJob job) =>
    [
        $"TemporaryFileSystem={job.JobCover}:ro",
        .. job.CheckFolders.Select(folder => $"BindPaths={folder}"),
    ];

    /// <summary>
    /// What hides the host from a unit, for a check and for a coder alike: <c>/home</c>, <c>/root</c> and <c>/run/user</c> are empty tmpfs (<c>ProtectHome=tmpfs</c>), <c>/mnt</c> and <c>/dev/shm</c> too, the rest of the file system is read-only, and the WSL
    /// interop folder is closed. A folder the unit needs is bound back afterwards, one by one.
    /// </summary>
    internal static readonly IReadOnlyList<string> Isolation =
    [
        "ProtectSystem=strict",
        "ProtectHome=tmpfs",
        "TemporaryFileSystem=/run/user",
        "TemporaryFileSystem=/mnt",
        "TemporaryFileSystem=/dev/shm",
        "InaccessiblePaths=-/run/WSL",
    ];

    /// <summary>The hardening properties of every unit of this family, and <c>Nice=10</c>: a unit runs below the person's own work (<c>common/local-cpu-budget.md</c> § 2).</summary>
    internal static readonly IReadOnlyList<string> Hardening =
    [
        "NoNewPrivileges=yes",
        "PrivateUsers=yes",
        "RestrictSUIDSGID=yes",
        "LockPersonality=yes",
        "ProtectKernelTunables=yes",
        "ProtectControlGroups=yes",
        "Nice=10",
        // No core file of a unit that may hold a pool key in its memory or its environment (the second judgement).
        "LimitCORE=0",
    ];

    /// <summary>The deadline backstop and the memory ceiling that kills: <c>RuntimeMaxSec</c>, <c>MemoryMax</c> and <c>MemorySwapMax=0</c>.</summary>
    internal static IReadOnlyList<string> Limits(BoundaryJob job, TimeSpan runtimeMax) =>
    [
        $"RuntimeMaxSec={Math.Max(1, (long)Math.Ceiling(runtimeMax.TotalSeconds)).ToString(CultureInfo.InvariantCulture)}",
        $"TimeoutStopSec={UnitControl.UnitStopSeconds.ToString(CultureInfo.InvariantCulture)}",
        .. DetachedLauncher.MemoryProperties(job.MemoryMegabytes),
    ];

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
            new("TMPDIR", UnitTemp),
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
    /// <param name="workingDirectory">The folder the command starts in: the worktree (the default) or a real folder inside it (E4 s3 t2). It changes no property of the unit: the worktree is bound whole.</param>
    /// <exception cref="ArgumentException">The program's name has an <c>=</c> (<c>env</c> would read it as an assignment), or the argv is empty.</exception>
    internal static IReadOnlyList<string> Arguments(BoundaryHost host, BoundaryJob job, string unit, CheckNetwork network, TimeSpan runtimeMax,
        IReadOnlyDictionary<string, string?> extra, IReadOnlyList<string> command, Func<IReadOnlyList<string>, IReadOnlyList<string>>? degrade = null, string? workingDirectory = null)
    {
        if (command.Count == 0 || command[0].Contains('=', StringComparison.Ordinal) || command[0].StartsWith('-'))
        {
            throw new ArgumentException("the program to run in the unit is empty, starts with '-' or holds '='", nameof(command));
        }

        return Wrap(unit, workingDirectory ?? job.Tree.Root, [], Properties(host, job, network, runtimeMax),
            ["/usr/bin/env", "-i", .. Environment(host, job, extra).Select(pair => $"{pair.Key}={pair.Value}"), .. command], degrade);
    }

    /// <summary>
    /// The argv of <c>systemd-run</c> for any unit of this family: <c>--wait --pipe</c>, the unit and its folder, the extra client options (<c>--setenv=NAME</c> copies a variable from the client by NAME, so a value never
    /// reaches an argv), every property, then <paramref name="inner"/> — the command the unit runs, which sets its own environment.
    /// </summary>
    internal static IReadOnlyList<string> Wrap(string unit, string workingDirectory, IReadOnlyList<string> options, IReadOnlyList<string> properties, IReadOnlyList<string> inner,
        Func<IReadOnlyList<string>, IReadOnlyList<string>>? degrade)
    {
        IReadOnlyList<string> arguments =
        [
            "--wait", "--pipe",
            .. DetachedLauncher.UnitPrefix(unit, workingDirectory),
            .. options,
            .. properties.SelectMany(property => new[] { "-p", property }),
            "--",
            .. inner,
        ];

        return degrade is null ? arguments : degrade(arguments);
    }
}

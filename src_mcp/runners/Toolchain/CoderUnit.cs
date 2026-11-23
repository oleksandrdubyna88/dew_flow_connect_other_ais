using System.Text.RegularExpressions;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Toolchain;

/// <summary>
/// The description of the unit the claude CODER runs in — the <c>systemd-run --user</c> properties, the environment and the argv — as pure values, the twin of <see cref="BoundaryUnit"/> for the one process that is allowed to write
/// (epoch 3 E2 s2 t5, option (a) of <c>v2/spikes/RESULTS_coder_write_boundary_judgement.md</c> § 4). It reuses that class's isolation, hardening and limits; what differs is what the CLI needs and the checks do not.
/// </summary>
/// <remarks>
/// <para><b>These are requests until the preflight has seen the answer</b>, exactly as for the checks: <see cref="BoundaryPreflight.CoderItems"/> asks the OS, and the tests read the effects off a real unit.</para>
/// <para><b>What the CLI needs, and what it does not get.</b> The unit still hides <c>/home</c>, <c>/root</c>, <c>/run/user</c> and <c>/mnt</c>; the main checkout, the coder root and the worktree's <c>.git</c> are read-only and ONLY the worktree is writable
/// among the source folders. Unlike a check it has the HOST network (the CLI talks to its vendor) and one file of the real home — the sign-in file, read-only, bound into a home of the coder's own. Measured with the real CLI on 2.1.330
/// (<c>RESULTS_s2_check_boundary.md</c>, t5): with that home and the resolver file the CLI signed in and answered; everything it wrote was in its own home and <c>/tmp</c>, and the real <c>~/.claude</c>, <c>~/.claude.json</c> and <c>settings.json</c> were never needed
/// — so none of them is bound, and a coder that wrote a hook into them would write it into a folder that is gone with the job.</para>
/// <para><b>The environment is set INSIDE the unit</b> by <c>env -i</c>, as for the checks. A value that must reach the CLI — the row's own key, a proxy — travels by NAME (<c>--setenv=NAME</c> copies it from the client), never in an argv.</para>
/// </remarks>
internal static partial class CoderUnit
{
    /// <summary>The prefix of every coder unit; a leftover is found by it. Not <c>coai-check-</c>: a coder's unit is not a check.</summary>
    internal const string UnitPrefix = "coai-coder-";

    /// <summary>The unit's name for one launch of a job (<c>c1</c>, <c>c2</c>… — a resumed turn is a launch), without the <c>.service</c> suffix.</summary>
    internal static string UnitName(BoundaryJob job, string part) => $"{UnitPrefix}{job.JobId}-{part}";

    /// <summary>The names a launch may carry from coai's own environment into the unit besides its explicit ones: the proxy, certificate and time-zone names a box behind a corporate proxy needs to reach its vendor.</summary>
    private static readonly string[] PassableNames =
        ["HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY", "http_proxy", "https_proxy", "no_proxy", "SSL_CERT_FILE", "SSL_CERT_DIR", "NODE_EXTRA_CA_CERTS", "TZ"];

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,15}$")]
    private static partial Regex Part();

    /// <summary>Whether <paramref name="part"/> can be the last word of a unit's name: it is spelled into the name and into <c>systemctl</c> commands, so only the safe shape is.</summary>
    internal static bool IsPart(string part) => Part().IsMatch(part);

    /// <summary>The <c>-p</c> properties of the coder's unit, in the order they are given.</summary>
    internal static IReadOnlyList<string> Properties(BoundaryHost host, BoundaryJob job, CoderProgram program, TimeSpan runtimeMax)
    {
        var tree = job.Tree;

        return
        [
            .. BoundaryUnit.Isolation,
            $"BindReadOnlyPaths={tree.MainCheckout}",
            $"BindReadOnlyPaths={job.CoderRoot}",
            $"BindPaths={tree.Root}",
            $"BindReadOnlyPaths={tree.GitEntry}",
            $"BindPaths={job.CoderFolder}",
            $"BindPaths={job.CoderTempFolder}:{BoundaryUnit.UnitTemp}",
            $"BindPaths={job.CoderVarTempFolder}:{BoundaryUnit.UnitVarTemp}",
            .. host.SignInFile.Length > 0 ? [$"BindReadOnlyPaths={host.SignInFile}:{job.CoderSignInFile}"] : Array.Empty<string>(),
            .. program.BindFolder.Length > 0 ? [$"BindReadOnlyPaths={program.BindFolder}"] : Array.Empty<string>(),
            .. host.ResolverFile.Length > 0 ? [$"BindReadOnlyPaths={host.ResolverFile}"] : Array.Empty<string>(),
            .. BoundaryUnit.Hardening,
            "PrivateNetwork=no",
            .. BoundaryUnit.Limits(job, runtimeMax),
        ];
    }

    /// <summary>
    /// The fixed part of the unit's environment, as <c>env -i</c> sets it inside: <c>PATH</c> of system folders, the coder's own <c>HOME</c>, <c>TMPDIR</c>, a locale and the switches that keep it quiet — and nothing else. No vendor key, no
    /// <c>COAI_</c> name, no agent or bus socket; the values that must cross are <see cref="ByName"/>.
    /// </summary>
    internal static IReadOnlyList<KeyValuePair<string, string>> Environment(BoundaryHost host, BoundaryJob job) =>
    [
        new("PATH", BoundaryHost.SystemPath),
        new("HOME", job.CoderHomeFolder),
        new("TMPDIR", BoundaryUnit.UnitTemp),
        new("LANG", "C.UTF-8"),
        new("TERM", "dumb"),
        new("NO_COLOR", "1"),
        new("GIT_TERMINAL_PROMPT", "0"),
    ];

    /// <summary>
    /// The names whose VALUE reaches the unit: the explicit variables of the request (the row's own key) and, of the passable names, those coai itself has. Each must be a plain identifier — the name is spelled into the unit's own script, never its value.
    /// </summary>
    /// <exception cref="ArgumentException">A name that is not an identifier.</exception>
    internal static IReadOnlyList<string> ByName(ProcessRequest request, Func<string, string?> coaiEnvironment)
    {
        List<string> names =
        [
            .. request.Environment.Where(pair => pair.Value is not null).Select(pair => pair.Key),
            .. PassableNames.Where(name => request.Passthrough.Contains(name) && coaiEnvironment(name) is not null),
        ];
        var bad = names.FirstOrDefault(name => !Identifier().IsMatch(name));
        if (bad is not null)
        {
            throw new ArgumentException($"the variable name '{bad}' cannot be handed to the coder's unit: only plain names are", nameof(request));
        }

        return [.. names.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>The names that cross by NAME, as the client is started with them: its own few, and these.</summary>
    internal static IReadOnlySet<string> ClientNames(IReadOnlyList<string> byName) =>
        new HashSet<string>([.. UnitControl.Client, .. byName], StringComparer.Ordinal);

    /// <summary>
    /// What the unit runs: a shell that clears the environment, sets the fixed list and copies each name in <paramref name="byName"/> from the unit's own (where <c>--setenv</c> put it), then execs the program with its argv. The program and its argv are
    /// positional arguments of the shell, never text of its script.
    /// </summary>
    internal static IReadOnlyList<string> Inner(BoundaryHost host, BoundaryJob job, CoderProgram program, IReadOnlyList<string> arguments, IReadOnlyList<string> byName) =>
    [
        "/bin/sh", "-c",
        $"exec /usr/bin/env -i {string.Join(' ', byName.Select(name => $"\"{name}=${name}\""))} \"$@\"",
        "coai-coder",
        .. Environment(host, job).Select(pair => $"{pair.Key}={pair.Value}"),
        program.Real,
        .. arguments,
    ];

    /// <summary>The argv of <c>systemd-run</c> for one coder launch.</summary>
    /// <param name="degrade">For a test only: rewrites the finished argument list. Production passes nothing.</param>
    internal static IReadOnlyList<string> Arguments(BoundaryHost host, BoundaryJob job, CoderProgram program, string unit, TimeSpan runtimeMax, IReadOnlyList<string> arguments,
        IReadOnlyList<string> byName, Func<IReadOnlyList<string>, IReadOnlyList<string>>? degrade = null) =>
        BoundaryUnit.Wrap(unit, job.Tree.Root, [.. byName.Select(name => $"--setenv={name}")], Properties(host, job, program, runtimeMax), Inner(host, job, program, arguments, byName), degrade);
}

using System.Text.RegularExpressions;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// Whether the installed claude can be given the coder's write launch — a release at or above <see cref="VersionFloor"/> whose <c>--help</c> declares
/// <c>--restricted</c>, <c>--permission-prompts</c> and <c>acceptEdits</c> as a permission mode — or why not, with the cure (epoch 3 E2 s2 t3, d3).
/// </summary>
/// <remarks>
/// <para><b>A refusal, never a weaker launch.</b> Every flag of the coder's argv (<c>ClaudeConsultant</c>'s coder case) is part of the boundary that was measured
/// (<c>v2/spikes/RESULTS_coder_write_boundary.md</c>); a claude that lacks one cannot be given the argv with that flag left out. On this machine the user settings add
/// <c>/home/jinx/git</c> to the working directories unless <c>--restricted</c> ignores them, so a CLI without it is the one case the results file names as refused.</para>
/// <para><b>Two asks, both through the one launcher</b>, each under <see cref="ClaudeCapability.ProbeTimeout"/>: <c>--version</c> once, <c>--help</c> as the consultant asks it
/// (<see cref="ClaudeCapability.ProbeWithHelpAsync"/>: once more when the first ask could not tell). Asked on every launch and never cached, for the reason
/// <see cref="ClaudeCapability"/> gives.</para>
/// <para>The floor is the release the boundary was measured on, not the first release that has the flags: a flag that exists is a request, and what it does was observed on
/// 2.1.330 only.</para>
/// </remarks>
public abstract partial record ClaudeWriteCapability
{
    /// <summary>The first release the write boundary was measured on (<c>v2/spikes/RESULTS_coder_write_boundary.md</c>).</summary>
    public static readonly Version VersionFloor = new(2, 1, 330);

    /// <summary>The permission mode of the coder's launch; it must be one of the CLI's declared choices.</summary>
    public const string AcceptEdits = "acceptEdits";

    /// <summary>The flag that says nobody answers a permission prompt.</summary>
    public const string PermissionPromptsFlag = "--permission-prompts";

    private const string PermissionModeFlag = "--permission-mode";

    private const string UpdateCure = "update claude (`claude update`) to 2.1.330 or newer, then start the coder again";

    /// <summary>Launchable. <paramref name="Version"/> is the release as the CLI's own <c>--version</c> printed it (<c>2.1.330</c>).</summary>
    public sealed record Ready(string Version) : ClaudeWriteCapability;

    /// <summary>Not launchable: what is wrong in one sentence, the cure, and whether the CLI could not be started at all.</summary>
    public sealed record Refused(string Said, string Cure, bool CouldNotStart = false) : ClaudeWriteCapability;

    private ClaudeWriteCapability() { }

    [GeneratedRegex(@"^(?<release>\d+\.\d+\.\d+)(?:\s|\z)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ReleaseAtTheStart();

    /// <summary>What a <c>--version</c> line and a <c>--help</c> text say about the write launch — pure, so every edge is a unit test.</summary>
    public static ClaudeWriteCapability Judge(string versionText, string helpText) =>
        JudgeVersion(versionText) switch
        {
            Ready release => JudgeHelp(release.Version, helpText),
            var refused => refused,
        };

    /// <summary>The release a <c>--version</c> answer names, judged against the floor. Only its FIRST line counts: a banner before it is not believed.</summary>
    public static ClaudeWriteCapability JudgeVersion(string versionText)
    {
        var first = versionText.Split('\n')[0].Trim();
        var named = ReleaseAtTheStart().Match(first);

        return !named.Success || !Version.TryParse(named.Groups["release"].Value, out var release)
            ? new Refused($"claude --version did not name a release ('{Shown(first)}'), so coai cannot tell whether the write boundary was measured on it", UpdateCure)
            : release < VersionFloor
                ? new Refused($"claude {release} is older than {VersionFloor}, the first release the coder's write boundary was measured on", UpdateCure)
                : new Ready(release.ToString());
    }

    /// <summary>The help of a release at the floor: every flag and mode of the coder's argv must be DECLARED, as an option and as a choice — never a word in prose.</summary>
    public static ClaudeWriteCapability JudgeHelp(string release, string helpText)
    {
        var missing = new[]
        {
            (ClaudeCapability.RestrictedFlag, ClaudeCapability.Declares(helpText, ClaudeCapability.RestrictedFlag)),
            (PermissionPromptsFlag, ClaudeCapability.Declares(helpText, PermissionPromptsFlag)),
            ($"{AcceptEdits} as a {PermissionModeFlag} choice", ChoiceOf(helpText, AcceptEdits)),
        }.Where(declared => !declared.Item2).Select(declared => declared.Item1).ToList();

        return missing.Count == 0
            ? new Ready(release)
            : new Refused($"claude {release} does not declare {string.Join(", ", missing)} in its --help, and the coder is never launched with a weaker boundary", UpdateCure);
    }

    /// <summary>Whether the description of <c>--permission-mode</c> lists <paramref name="mode"/> among its choices — the quoted word, in that option's own block.</summary>
    private static bool ChoiceOf(string helpText, string mode) =>
        ClaudeCapability.DescriptionOf(helpText, PermissionModeFlag).Contains($"\"{mode}\"", StringComparison.Ordinal);

    /// <summary>
    /// Asks <paramref name="executable"/> for its <c>--version</c> and its <c>--help</c> through the shared launcher and judges both. A CLI that cannot be started, a
    /// version that did not answer and a help that never came back are each a refusal that says so — none is read as "an old claude".
    /// </summary>
    public static async Task<ClaudeWriteCapability> ProbeAsync(IProcessLauncher launcher, string executable, string workingDirectory, CancellationToken ct)
    {
        var version = await VersionAsync(launcher, executable, workingDirectory, ct);
        if (version is not Ready release)
        {
            return version;
        }

        var help = await ClaudeCapability.ProbeWithHelpAsync(launcher, executable, workingDirectory, ct);

        return help.Capability switch
        {
            { Support: RestrictedSupport.Declared or RestrictedSupport.NotDeclared } => JudgeHelp(release.Version, help.Text),
            { CouldNotStart: true } => new Refused(help.Capability.Reason, "install claude or point the coder's row at it", CouldNotStart: true),
            _ => new Refused($"{help.Capability.Reason}, so coai cannot tell whether the write boundary is declared and will not launch the coder without it", "check the CLI (`claude --help`), then start the coder again"),
        };
    }

    private static async Task<ClaudeWriteCapability> VersionAsync(IProcessLauncher launcher, string executable, string workingDirectory, CancellationToken ct)
    {
        try
        {
            var result = await launcher.RunAsync(
                new ProcessRequest(executable, ["--version"], workingDirectory) { Timeout = ClaudeCapability.ProbeTimeout },
                ct);
            ct.ThrowIfCancellationRequested();

            return result switch
            {
                { TimedOut: true } => new Refused($"claude --version did not answer within {ClaudeCapability.ProbeTimeout.TotalSeconds:0} s", "check the CLI (`claude --version`), then start the coder again"),
                { ExitCode: not 0 } => new Refused($"claude --version exited {result.ExitCode}", "check the CLI (`claude --version`), then start the coder again"),
                _ => JudgeVersion(result.StdOut),
            };
        }
        // What a missing or unrunnable executable throws out of Process.Start — the same three ClaudeCapability catches.
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return new Refused($"claude --version could not be started ({e.Message})", "install claude or point the coder's row at it", CouldNotStart: true);
        }
    }

    /// <summary>A value safe to put in a message: control characters shown as a space, a bounded length.</summary>
    private static string Shown(string value)
    {
        var flat = string.Concat(value.Select(c => char.IsControl(c) ? ' ' : c));

        return flat.Length <= 80 ? flat : flat[..80] + "…";
    }
}

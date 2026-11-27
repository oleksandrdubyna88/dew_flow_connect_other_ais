using System.Text.RegularExpressions;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// Whether the installed agy can be given the coder's write launch — its help declares every flag of the coder's argv — or why not, with the cure (epoch 3 E3 s6 t1, V6; the owner's decision of 2026-11-26). The RELEASE is a record, never a condition: an older, a newer and an unnamed
/// release are each launched, and the job records the installed release beside <see cref="LastMeasuredRelease"/>.
/// </summary>
/// <remarks>
/// <para><b>What it asks is what the installed CLI declares.</b> The argv of the coder (<c>AgyCoderVendor</c>) is the measured flag set AG1: <c>--print=</c>, <c>--input-format stream-json</c>, <c>--output-format stream-json</c>, <c>--mode accept-edits</c>, <c>--agent</c>, <c>--model</c>,
/// <c>--add-dir</c> and, on a fix turn, <c>--conversation</c>. A flag the help does not declare is a refusal that names the flag; no release decides it. Each flag is read as an OPTION in the option table, and a choice (<c>accept-edits</c>, <c>stream-json</c>) is read in that option's
/// own description — never a word in prose elsewhere.</para>
/// <para><b>Where the help is.</b> The usage text of agy's flag parser goes to standard error, so both streams are read, and a non-zero exit does not hide a table that was printed. A help that never came back whole (a timeout, a cut-short text, nothing printed) is a refusal that says so, after two asks,
/// because the first start after a self-update can be slow. Asked on every launch and never cached.</para>
/// </remarks>
public abstract partial record AgyWriteCapability
{
    /// <summary>The release the rows of the agy coder were measured on: history, named beside the installed release in the job's record. It decides nothing.</summary>
    public const string LastMeasuredRelease = "1.5.18";

    /// <summary>What the record says when <c>--version</c> named no number coai can read.</summary>
    public const string UnknownRelease = "unknown";

    /// <summary>The mode of the coder's launch.</summary>
    public const string AcceptEdits = "accept-edits";

    private const string CheckCure = "check the CLI (`agy --help`), then start the coder again";

    /// <summary>Launchable. <paramref name="Version"/> is the release as the CLI's own <c>--version</c> printed it, or <see cref="UnknownRelease"/>.</summary>
    public sealed record Ready(string Version) : AgyWriteCapability;

    /// <summary>Not launchable: what is wrong in one sentence, the cure, and whether the CLI could not be started at all.</summary>
    public sealed record Refused(string Said, string Cure, bool CouldNotStart = false) : AgyWriteCapability;

    private AgyWriteCapability() { }

    /// <summary>The flags of the coder's argv, each with the choice it needs the help to list (empty: none).</summary>
    private static readonly (string Flag, string Choice)[] Declared =
    [
        ("--print", string.Empty),
        ("--input-format", "stream-json"),
        ("--output-format", "stream-json"),
        ("--mode", AcceptEdits),
        ("--agent", string.Empty),
        ("--model", string.Empty),
        ("--add-dir", string.Empty),
        ("--conversation", string.Empty),
    ];

    [GeneratedRegex(@"\d+\.\d+\.\d+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Number();

    /// <summary>What a <c>--version</c> text and a help text say about the write launch — pure, so every edge is a unit test.</summary>
    public static AgyWriteCapability Judge(string versionText, string helpText) =>
        ReadRelease(versionText) is Ready release ? JudgeHelp(release.Version, helpText) : ReadRelease(versionText);

    /// <summary>The release a <c>--version</c> answer names: the first number of the form <c>x.y.z</c> on its first line. None is <see cref="UnknownRelease"/> — recorded, and the launch goes on.</summary>
    public static AgyWriteCapability ReadRelease(string versionText)
    {
        var first = versionText.Split('\n')[0];
        var named = Number().Match(first);

        return new Ready(named.Success ? named.Value : UnknownRelease);
    }

    /// <summary>The help of the installed CLI: every flag of the coder's argv is DECLARED, as an option and, where the argv needs one, as a listed choice.</summary>
    public static AgyWriteCapability JudgeHelp(string release, string helpText)
    {
        var missing = Declared
            .Where(item => !ClaudeCapability.Declares(helpText, item.Flag))
            .Select(item => item.Flag)
            .Concat(Declared
                .Where(item => item.Choice.Length > 0 && ClaudeCapability.Declares(helpText, item.Flag) && !ClaudeCapability.DescriptionOf(helpText, item.Flag).Contains(item.Choice, StringComparison.Ordinal))
                .Select(item => $"{item.Flag} with {item.Choice} among its choices"))
            .ToList();

        return missing.Count == 0
            ? new Ready(release)
            : new Refused($"agy does not declare {string.Join(", ", missing)} in its help, and the coder is never launched with a weaker set of flags", CheckCure);
    }

    /// <summary>
    /// Asks <paramref name="executable"/> for its <c>--version</c> and its <c>--help</c> through the shared launcher and judges both. A CLI that cannot be started and a help that does not answer are each a refusal that says so; a version that did not answer or cannot be read is recorded as
    /// unknown and refuses nothing.
    /// </summary>
    public static async Task<AgyWriteCapability> ProbeAsync(IProcessLauncher launcher, string executable, string workingDirectory, CancellationToken ct)
    {
        var release = await VersionAsync(launcher, executable, workingDirectory, ct);
        if (release is not Ready ready)
        {
            return release;
        }

        var first = await HelpAsync(launcher, executable, workingDirectory, ct);
        var help = first.Text.Length > 0 || first.NotStarted ? first : await HelpAsync(launcher, executable, workingDirectory, ct);
        if (help.NotStarted)
        {
            return new Refused($"agy --help could not be started ({help.Said})", "install agy or point the coder's row at it", CouldNotStart: true);
        }

        return help.Text.Length == 0
            ? new Refused($"agy --help did not come back whole ({help.Said}), so coai cannot tell whether the flags of the coder are declared and will not launch it without them", CheckCure)
            : JudgeHelp(ready.Version, help.Text);
    }

    private sealed record Help(string Text, string Said, bool NotStarted = false);

    private static async Task<Help> HelpAsync(IProcessLauncher launcher, string executable, string workingDirectory, CancellationToken ct)
    {
        try
        {
            var result = await launcher.RunAsync(new ProcessRequest(executable, ["--help"], workingDirectory) { Timeout = ClaudeCapability.ProbeTimeout }, ct);
            ct.ThrowIfCancellationRequested();
            var text = string.Join('\n', new[] { result.StdOut, result.StdErr }.Where(part => !string.IsNullOrWhiteSpace(part)));

            return result switch
            {
                { TimedOut: true } => new Help(string.Empty, $"no answer within {ClaudeCapability.ProbeTimeout.TotalSeconds:0} s"),
                { Truncated: true } => new Help(string.Empty, "the text was cut short, so the rest of the options were never read"),
                _ when text.Length == 0 => new Help(string.Empty, "it printed nothing"),
                { ExitCode: not 0 } when !ClaudeCapability.Declares(text, "--model") => new Help(string.Empty, $"it exited {result.ExitCode} and printed no option table"),
                _ => new Help(text, string.Empty),
            };
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return new Help(string.Empty, e.Message, NotStarted: true);
        }
    }

    private static async Task<AgyWriteCapability> VersionAsync(IProcessLauncher launcher, string executable, string workingDirectory, CancellationToken ct)
    {
        try
        {
            var result = await launcher.RunAsync(new ProcessRequest(executable, ["--version"], workingDirectory) { Timeout = ClaudeCapability.ProbeTimeout }, ct);
            ct.ThrowIfCancellationRequested();

            // An answer that did not come, or came with a failure, names no release: the release is a record, so it is recorded as unknown and the launch goes on to the help (a CLI that is really broken fails there, or in its own launch).
            return result is { TimedOut: true } or { ExitCode: not 0 } ? new Ready(UnknownRelease) : ReadRelease(result.StdOut);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return new Refused($"agy --version could not be started ({e.Message})", "install agy or point the coder's row at it", CouldNotStart: true);
        }
    }
}

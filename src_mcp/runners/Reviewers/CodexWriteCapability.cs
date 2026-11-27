using System.Text.RegularExpressions;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// Whether the installed codex can be given the coder's write launch — its <c>exec --help</c> and its <c>exec resume --help</c> each DECLARE every flag of the coder's argv — or why not, with the cure (epoch 3 E3 s5 t1, V6). The release is a record, never a condition (owner decision of
/// 2026-11-26; it replaces id CX-P1 of the judgement, the pin by release and bytes): an older, a newer and an unnamed release are each launched, and the job records the installed release beside the last measured one.
/// </summary>
/// <remarks>
/// <para><b>A flag the CLI does not declare is not a version gate.</b> The argv of a codex coder (<c>CodexCoderVendor</c>) is the measured boundary (<c>v2/spikes/RESULTS_codex_coder.md</c> section 2, judgement section 13.4): a codex whose help does not declare one of its flags cannot be given the argv
/// with that flag left out, and the refusal names the FLAG and the help it was missing from — never the release.</para>
/// <para><b>Three asks, all through the one launcher</b>, each under <see cref="ClaudeCapability.ProbeTimeout"/>: <c>--version</c>, <c>exec --help</c>, <c>exec resume --help</c>. Asked on every launch and never cached.</para>
/// </remarks>
public abstract partial record CodexWriteCapability
{
    /// <summary>What the record says when <c>--version</c> named no release coai can read.</summary>
    public const string UnknownRelease = "unknown";

    /// <summary>
    /// The long flags of the argv that BOTH helps must declare: the stream (<c>--json</c>), the model, the config overrides, the feature switches (<c>--disable</c>), the output schema and the output file. A resumed turn carries every flag of the fresh one, so the same list is asked of
    /// <c>exec resume --help</c>.
    /// </summary>
    public static IReadOnlyList<string> ArgvFlags { get; } = ["--json", "--model", "--config", "--disable", "--output-schema", "--output-last-message"];

    /// <summary>Launchable. <paramref name="Version"/> is the release as the CLI's own <c>--version</c> printed it (<c>0.171.0</c>), or <see cref="UnknownRelease"/>.</summary>
    public sealed record Ready(string Version) : CodexWriteCapability;

    /// <summary>Not launchable: what is wrong in one sentence, the cure, and whether the CLI could not be started at all.</summary>
    public sealed record Refused(string Said, string Cure, bool CouldNotStart = false) : CodexWriteCapability;

    private CodexWriteCapability()
    {
    }

    [GeneratedRegex(@"(?<release>\d+\.\d+\.\d+)(?:\s|\z|-)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ReleaseInTheLine();

    /// <summary>What the three answers say about the write launch — pure, so every edge is a unit test. The release is read for the record only.</summary>
    public static CodexWriteCapability Judge(string versionText, string execHelp, string resumeHelp)
    {
        var missingInExec = ArgvFlags.Where(flag => !ClaudeCapability.Declares(execHelp, flag)).ToList();
        var missingInResume = ArgvFlags.Where(flag => !ClaudeCapability.Declares(resumeHelp, flag)).ToList();
        if (missingInExec.Count == 0 && missingInResume.Count == 0)
        {
            return new Ready(ReadRelease(versionText));
        }

        var said = string.Join("; ", new[]
        {
            missingInExec.Count > 0 ? $"`codex exec --help` does not declare {string.Join(", ", missingInExec)}" : string.Empty,
            missingInResume.Count > 0 ? $"`codex exec resume --help` does not declare {string.Join(", ", missingInResume)}" : string.Empty,
        }.Where(part => part.Length > 0));

        return new Refused($"{said}, and the coder is never launched with a flag of its boundary left out", "install a codex whose help declares these flags or point the coder's row at one, then start the coder again");
    }

    /// <summary>The release a <c>--version</c> answer names: the first one found in its FIRST line (<c>codex-cli 0.171.0</c>). None is <see cref="UnknownRelease"/> — recorded, and the launch goes on.</summary>
    public static string ReadRelease(string versionText)
    {
        var first = versionText.Split('\n')[0].Trim();
        var named = ReleaseInTheLine().Match(first);

        return named.Success ? named.Groups["release"].Value : UnknownRelease;
    }

    /// <summary>
    /// Asks <paramref name="executable"/> for its <c>--version</c>, <c>exec --help</c> and <c>exec resume --help</c> through the shared launcher and judges them. A CLI that cannot be started and a help that never came back are each a refusal that says so; a version that did not answer or cannot be
    /// read is recorded as unknown and refuses nothing.
    /// </summary>
    public static async Task<CodexWriteCapability> ProbeAsync(IProcessLauncher launcher, string executable, string workingDirectory, CancellationToken ct)
    {
        var version = await AskAsync(launcher, executable, ["--version"], workingDirectory, ct);
        if (!version.Started)
        {
            return new Refused($"codex --version could not be started ({version.Why})", "install codex or point the coder's row at it", CouldNotStart: true);
        }

        var exec = await AskAsync(launcher, executable, ["exec", "--help"], workingDirectory, ct);
        var resume = await AskAsync(launcher, executable, ["exec", "resume", "--help"], workingDirectory, ct);
        foreach (var (asked, name) in new[] { (exec, "codex exec --help"), (resume, "codex exec resume --help") })
        {
            if (!asked.Started)
            {
                return new Refused($"{name} could not be started ({asked.Why})", "install codex or point the coder's row at it", CouldNotStart: true);
            }

            if (asked.Text.Length == 0)
            {
                return new Refused($"{name} did not answer, so coai cannot tell whether the flags of the write boundary are declared and will not launch the coder without them", "check the CLI (`codex exec --help`), then start the coder again");
            }
        }

        return Judge(version.Text, exec.Text, resume.Text);
    }

    private sealed record Answer(bool Started, string Text, string Why);

    private static async Task<Answer> AskAsync(IProcessLauncher launcher, string executable, string[] arguments, string workingDirectory, CancellationToken ct)
    {
        try
        {
            var result = await launcher.RunAsync(new ProcessRequest(executable, arguments, workingDirectory) { Timeout = ClaudeCapability.ProbeTimeout }, ct);
            ct.ThrowIfCancellationRequested();

            // An answer that did not come, or came with a failure, is no text: for --version it is "unknown", for a help it is a refusal that says so.
            return result is { TimedOut: true } or { ExitCode: not 0 } ? new Answer(true, string.Empty, string.Empty) : new Answer(true, result.StdOut, string.Empty);
        }
        // What a missing or unrunnable executable throws out of Process.Start — the same three ClaudeWriteCapability catches.
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return new Answer(false, string.Empty, e.Message);
        }
    }
}

using System.Text.RegularExpressions;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>What the installed claude's own <c>--help</c> said about <c>--restricted</c> — or that nobody asked.</summary>
public enum RestrictedSupport
{
    /// <summary>Nobody asked. A launch built from this is a contract violation, never an unconfined launch.</summary>
    Unprobed = 0,

    /// <summary>The help came back and declares <c>--restricted</c> as a flag.</summary>
    Declared,

    /// <summary>The help came back and does not declare it (or declares it taking a value, which our argv cannot feed).</summary>
    NotDeclared,

    /// <summary>The help did not come back whole: a timeout, a non-zero exit, nothing printed, a help cut short, a CLI that would not start.</summary>
    Unknown,
}

/// <summary>
/// What the INSTALLED claude CLI can be asked to do — today, whether it knows <c>--restricted</c> — and why we
/// believe it.
/// </summary>
/// <param name="Reason">One sentence for the log and, on <see cref="RestrictedSupport.Unknown"/>, for the caller.</param>
/// <param name="CouldNotStart">
/// The help was <see cref="RestrictedSupport.Unknown"/> because the executable could not be STARTED at all — a missing or
/// unrunnable file — which is a CLI that is not there rather than one that would not say (the whole-branch review, L):
/// the turn is then refused as <c>cli-not-found</c> with the install cure, never as the vendor refusing anything.
/// </param>
/// <remarks>
/// <para><b>Why the argv has to ask.</b> Measured 2026-10-02 (research/RESULTS_claude_consultant_confinement.md,
/// harness scripts/probe-claude-consultant-confinement.mjs): claude 2.1.258 on Windows, launched with
/// <c>--restricted --tools Read,Glob,Grep</c>, was confined to the repository 9 of 9 — fresh and resumed, sign-in
/// intact — while claude 2.1.197 in WSL refuses any launch that carries the flag
/// (<c>error: unknown option '--restricted'</c>, exit 1). One fixed argv loses every consultation on one of them or
/// leaves the other unconfined.</para>
/// <para><b>Three answers, not two</b> (epic 3's code round, security). The first version read every failure to get
/// the help as "not declared" — and "not declared" LAUNCHES the consultant without <c>--restricted</c>, so a hung or
/// crashed <c>--help</c> on a claude that has the flag ran a consultant that could read outside the repository.
/// <see cref="RestrictedSupport.Unknown"/> is asked once more and then refuses the turn
/// (<c>ClaudeConsultant.PrepareAsync</c>); only a help that CAME BACK without the flag launches without it.</para>
/// <para><b>Asked on every launch, never cached</b> (risk consultation 264fbcf2, 2026-10-03). A long-lived server's
/// remembered answer survives an in-place upgrade or downgrade of the CLI while the panel's one-shot probe shows
/// the fresh one; WSL's <c>claude</c> is a symlink into an npm package, so an mtime-keyed cache would watch the wrong
/// file; and the question costs 0.26–0.86 s on Windows and 0.35–0.49 s in WSL (measured) against turns of 5–30+ s.</para>
/// <para><b>In <c>Reviewers</c>, not <c>Consultation</c>,</b> because the claude REVIEWER is next
/// (todo/PLAN_the_claude_reviewer_is_confined_by_an_allowlist.md) and reads the same fact through
/// <see cref="ReviewerSettings.ClaudeCli"/>.</para>
/// </remarks>
public sealed partial record ClaudeCapability(RestrictedSupport Support, string Reason, bool CouldNotStart = false)
{
    /// <summary>The default of <see cref="ReviewerSettings.ClaudeCli"/> — which the consultant's builder refuses.</summary>
    public static ClaudeCapability Unprobed { get; } = new(RestrictedSupport.Unprobed, "the installed claude was never asked");

    /// <summary>A CLI whose help declares <c>--restricted</c>.</summary>
    public static ClaudeCapability WithRestricted { get; } = new(RestrictedSupport.Declared, "claude --help declares --restricted");

    /// <summary>A CLI whose help came back without <c>--restricted</c>.</summary>
    public static ClaudeCapability NoRestricted { get; } = new(RestrictedSupport.NotDeclared, "claude --help does not declare --restricted");

    /// <summary>A help that did not come back, and why.</summary>
    public static ClaudeCapability Unknown(string reason) => new(RestrictedSupport.Unknown, reason);

    /// <summary>The flag itself, as the argv carries it and as the help declares it.</summary>
    public const string RestrictedFlag = "--restricted";

    /// <summary>
    /// How long the CLI has to print its help, per ask.
    /// </summary>
    /// <remarks>
    /// Ten seconds against a measured 0.26–0.86 s: a CLI that cannot print its own help in that time is not going
    /// to answer a consultation either. Short on purpose — this runs before every claude turn, up to twice.
    /// </remarks>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Where the help may wrap a description onto a line of its own. An option is declared at a shallower
    /// indentation than this; a description's continuation sits deeper.
    /// </summary>
    /// <remarks>
    /// commander prints option lines two columns in and wraps descriptions at column 40 — and claude 2.1.258's own
    /// table has a continuation line BEGINNING <c>--tools names them, …</c> (in <c>--restricted</c>'s description).
    /// </remarks>
    private const int DeepestOptionIndent = 8;

    /// <summary>Supported, and the flag will be sent.</summary>
    public bool Restricted => Support == RestrictedSupport.Declared;

    /// <summary>
    /// The word <c>shared/consultant-limitations.json</c> qualifies a claude row with — <c>restricted</c> or
    /// <c>no-restricted</c> — and empty for a capability nobody could tell, which selects no claude row.
    /// </summary>
    public string Qualifier => Support switch
    {
        RestrictedSupport.Declared => "restricted",
        RestrictedSupport.NotDeclared => "no-restricted",
        _ => string.Empty,
    };

    /// <summary>
    /// Asks <paramref name="executable"/> for its <c>--help</c> through the shared launcher — under
    /// <see cref="ProbeTimeout"/>, the tree killed if it overruns — and once more when the first ask could not tell.
    /// </summary>
    /// <remarks>
    /// One retry, because a CLI's first start after an update or on a cold disk is the slow one, and refusing a
    /// consultation over it would be a false alarm; a second failure is a CLI that is not answering. The caller's
    /// cancellation is not an answer: it is rethrown, so a withdrawn turn is never recorded as a refusal.
    /// </remarks>
    public static async Task<ClaudeCapability> ProbeAsync(IProcessLauncher launcher, string executable, string workingDirectory, CancellationToken ct)
    {
        var first = await AskAsync(launcher, executable, workingDirectory, ct);
        if (first.Support != RestrictedSupport.Unknown)
        {
            return first;
        }

        var second = await AskAsync(launcher, executable, workingDirectory, ct);

        return second.Support == RestrictedSupport.Unknown ? second with { Reason = $"asked twice: {second.Reason}" } : second;
    }

    /// <summary>Whether a help text DECLARES <c>--restricted</c> as a flag — never a word in prose, never one taking a value.</summary>
    /// <remarks>
    /// <para>ANSI colour is stripped first: a CLI forced into colour prints <c>\e[36m--restricted\e[39m</c>, which is
    /// not the word. Then a line counts only in the option column format — option tokens and value placeholders,
    /// then two or more spaces before the description, or nothing after them (a header whose description wraps
    /// below) — at an option's indentation. Every long name on that line is read (<c>--sandbox, --restricted</c>).</para>
    /// <para>A declaration followed by a REQUIRED value (<c>--restricted &lt;mode&gt;</c>, <c>--restricted=&lt;mode&gt;</c>)
    /// is NOT support: sent bare before <c>--tools</c>, it would take <c>--tools</c> as its value, and the turn would
    /// run with the user's whole tool set.</para>
    /// </remarks>
    public static bool RestrictedIn(string helpText) =>
        Ansi().Replace(helpText, string.Empty).Split('\n')
            .Select(RestrictedOn)
            .FirstOrDefault(found => found != Declaration.Absent) == Declaration.Flag;

    private static async Task<ClaudeCapability> AskAsync(IProcessLauncher launcher, string executable, string workingDirectory, CancellationToken ct)
    {
        try
        {
            var result = await launcher.RunAsync(
                new ProcessRequest(executable, ["--help"], workingDirectory) { Timeout = ProbeTimeout },
                ct);
            ct.ThrowIfCancellationRequested();

            return Read(result);
        }
        // What a missing or unrunnable executable throws out of Process.Start — the same three VendorProbe catches.
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return Unknown($"claude --help could not be started ({e.Message})") with { CouldNotStart = true };
        }
    }

    /// <summary>What one finished <c>--help</c> says: an answer only when it came back, cleanly, with something in it.</summary>
    private static ClaudeCapability Read(ProcessResult result) => result switch
    {
        { TimedOut: true } => Unknown($"claude --help did not answer within {ProbeTimeout.TotalSeconds:0} s"),
        { ExitCode: not 0 } => Unknown($"claude --help exited {result.ExitCode}"),
        // Before "not declared", and failing CLOSED: a help cut off by the launcher's ceiling is a head without the rest
        // of the option table, and a --restricted line further down is simply not in it (the whole-branch review, L).
        { Truncated: true } => Unknown("claude --help was cut short, so the rest of its options were never read"),
        _ when string.IsNullOrWhiteSpace(result.StdOut) => Unknown("claude --help printed nothing"),
        _ => RestrictedIn(result.StdOut) ? WithRestricted : NoRestricted,
    };

    private enum Declaration
    {
        Absent,
        Flag,
        TakesValue,
    }

    /// <summary>What one help line declares about <c>--restricted</c>.</summary>
    private static Declaration RestrictedOn(string line)
    {
        var tokens = OptionHead(line);
        var at = Array.FindIndex(tokens, token => NameOf(token) == RestrictedFlag);

        return at < 0 ? Declaration.Absent : TakesAValue(tokens, at) ? Declaration.TakesValue : Declaration.Flag;
    }

    /// <summary>
    /// The tokens of an option declaration — <c>-r, --resume [value]</c> — or none for any other line: prose, a
    /// wrapped description, a usage line.
    /// </summary>
    private static string[] OptionHead(string line)
    {
        var trimmed = line.TrimEnd('\r', ' ', '\t');
        var body = trimmed.TrimStart();
        var tokens = HeadOf(body).Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);

        return trimmed.Length - body.Length < DeepestOptionIndent && tokens.Length > 0 && tokens.All(IsOptionToken)
            ? tokens
            : [];
    }

    /// <summary>The declaration column: the text before the first run of two spaces or a tab — the whole line when there is none.</summary>
    private static string HeadOf(string body)
    {
        var gap = body.IndexOf("  ", StringComparison.Ordinal);
        var tab = body.IndexOf('\t');
        var end = gap < 0 ? tab : tab < 0 ? gap : Math.Min(gap, tab);

        return end < 0 ? body : body[..end];
    }

    /// <summary>An option (<c>-r</c>, <c>--name</c>, <c>--name=&lt;v&gt;</c>) or a value placeholder (<c>&lt;v&gt;</c>, <c>[v]</c>).</summary>
    private static bool IsOptionToken(string token) =>
        token[0] is '<' or '[' || (token.Length > 1 && token[0] == '-' && NameOf(token).All(c => c == '-' || char.IsAsciiLetterOrDigit(c)));

    /// <summary>An option's name without an attached value: <c>--name=x</c> and <c>--name[=x]</c> are <c>--name</c>.</summary>
    private static string NameOf(string token) => token.Split('=', '[')[0];

    /// <summary>A REQUIRED value follows: <c>--restricted &lt;mode&gt;</c> or <c>--restricted=&lt;mode&gt;</c>.</summary>
    private static bool TakesAValue(string[] tokens, int at) =>
        tokens[at].Contains("=<", StringComparison.Ordinal) || (at + 1 < tokens.Length && tokens[at + 1].StartsWith('<'));

    /// <summary>An ANSI CSI sequence — colour, bold, reset.</summary>
    [GeneratedRegex(@"\x1b\[[0-9;]*[A-Za-z]", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Ansi();
}

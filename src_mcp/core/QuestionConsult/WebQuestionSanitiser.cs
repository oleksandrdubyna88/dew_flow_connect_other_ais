using System.Text.RegularExpressions;

namespace CoaiMcp.Core.QuestionConsult;

/// <summary>What a web question is checked against: the checkout and the roots, whose names must not travel.</summary>
/// <param name="RepoPath">The checkout the question was asked from — its path and its folder name are refused.</param>
/// <param name="Roots">The disk rows' roots — each path and each folder name is refused too.</param>
public sealed record WebQuestionContext(string RepoPath, IReadOnlyList<string> Roots)
{
    public static WebQuestionContext None { get; } = new(string.Empty, []);
}

/// <summary>A web question as it leaves the check: unchanged, or refused with its class and cure.</summary>
public abstract record WebQuestion
{
    /// <summary>The question exactly as it arrived — nothing is ever redacted, trimmed or rewritten.</summary>
    public sealed record Clean(string Text) : WebQuestion;

    /// <param name="Class">One of <see cref="WebQuestionSanitiser.RefusalClasses"/>.</param>
    /// <param name="Reason">What was found, by kind — never the text itself.</param>
    /// <param name="Cure">What the caller does about it.</param>
    public sealed record Refused(string Class, string Reason, string Cure) : WebQuestion;

    private WebQuestion() { }
}

/// <summary>
/// A web row is given strictly the question (PLAN_question_consultant.md, A2): no code, no path, no
/// config line, no stack trace, no secret, nothing that names this checkout, its roots or its network,
/// and not more than <see cref="MaxChars"/> characters. The sanitiser REFUSES and never redacts — every
/// refusal names its class and its cure, in the order below, first found first.
/// </summary>
/// <remarks>
/// <para>Refuse rather than redact because a web row's model is unconfined on the operator's own
/// example runtime (codex, F2): what the row is GIVEN is the only thing ours to control, and a question
/// with a hole where a path was still tells the model that there was a path. The caller rewrites the
/// question; the cure says how.</para>
/// <para>Every pattern is bounded — no nesting, every repetition capped, a match ceiling — because the
/// text is the calling AI's and nobody chose it. The URL-shaped tokens are set aside before the path
/// checks, so a public link may be cited; the host checks run over the original, so a private one may not.</para>
/// </remarks>
public static partial class WebQuestionSanitiser
{
    /// <summary>The most a web question may be: a search is a sentence or two (§4).</summary>
    public const int MaxChars = 600;

    private const int MatchTimeoutMs = 1000;

    /// <summary>The refusal classes, in the order they are asked — a secret first, the length last.</summary>
    public static IReadOnlyList<string> RefusalClasses { get; } =
        ["empty", "secret", "code-fence", "inline-code", "stack-trace", "config-line", "repository", "root", "path", "internal-host", "too-long"];

    /// <summary>A folder name shorter than this is not matched as a NAME — a checkout called <c>api</c> must not make every API question unaskable.</summary>
    private const int ShortestNameMatched = 4;

    public static WebQuestion Check(string question, WebQuestionContext context) =>
        FirstRefusal(question, context) is { } refused ? refused : new WebQuestion.Clean(question);

    private static WebQuestion.Refused? FirstRefusal(string question, WebQuestionContext context) =>
        Text(question) ?? Shape(question) ?? Place(question, context) ?? Length(question);

    /// <summary>Empty, a secret, a code fence, inline code, a stack trace, a config line.</summary>
    private static WebQuestion.Refused? Text(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return Refuse("empty", "the question is empty", "ask the question in a sentence or two");
        }

        var secret = SecretCheck.WhichSecret(question);

        return secret.Length > 0
            ? Refuse("secret", $"the question carries a secret shape ({secret})",
                "remove it — a web row's question leaves this machine and is kept in the vendor's own store; describe the failure without the value")
            : Code(question);
    }

    private static WebQuestion.Refused? Code(string question)
    {
        if (Fence().IsMatch(question))
        {
            return Refuse("code-fence", "the question carries a code fence", "describe the code in words — a web search needs the question, not the code");
        }

        var spans = InlineCode().Count(question);

        return spans >= 2
            ? Refuse("inline-code", $"the question carries {spans} inline code spans", "name at most one identifier in backticks, or none — the rest is the code ban")
            : Trace(question);
    }

    private static WebQuestion.Refused? Trace(string question) =>
        StackTrace().IsMatch(question)
            ? Refuse("stack-trace", "the question carries a stack-trace line", "state the error in one sentence — a trace carries paths, local names and line numbers")
            : null;

    /// <summary>A config line.</summary>
    private static WebQuestion.Refused? Shape(string question) =>
        ConfigAssignment().IsMatch(question) || ConfigColon().IsMatch(question)
            ? Refuse("config-line", "the question carries a key=value or key: value line", "write it as prose — a config line names this machine's setup")
            : null;

    /// <summary>The checkout, a root, any path shape, an internal host.</summary>
    private static WebQuestion.Refused? Place(string question, WebQuestionContext context) =>
        Named(question, "repository", [context.RepoPath])
        ?? Named(question, "root", context.Roots)
        ?? Paths(question)
        ?? Hosts(question);

    private static WebQuestion.Refused? Named(string question, string cls, IReadOnlyList<string> places)
    {
        foreach (var place in places.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            if (NamesThePlace(question, place))
            {
                return Refuse(cls, $"the question names the {cls} by its path or its folder name",
                    "do not name this checkout or a folder of this machine — ask about the shape of the problem");
            }
        }

        return null;
    }

    /// <summary>The place's path with either separator, or its folder name as a whole word — case-insensitively.</summary>
    private static bool NamesThePlace(string question, string place)
    {
        var slashed = place.Trim().Replace('\\', '/').TrimEnd('/');
        var asked = question.Replace('\\', '/');
        if (slashed.Length > 0 && asked.Contains(slashed, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var name = slashed[(slashed.LastIndexOf('/') + 1)..];

        return name.Length >= ShortestNameMatched
            && Regex.IsMatch(question, $@"(?<![\w-]){Regex.Escape(name)}(?![\w-])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(MatchTimeoutMs));
    }

    private static WebQuestion.Refused? Paths(string question)
    {
        var withoutUrls = Url().Replace(question, " ");
        var shape = DrivePath().IsMatch(withoutUrls) ? "a drive-qualified path"
            : UncPath().IsMatch(withoutUrls) ? "a UNC path"
            : RelativePath().IsMatch(withoutUrls) ? "a home or relative path"
            : FileScheme().IsMatch(question) ? "a file:// address"
            : SegmentedPath().IsMatch(withoutUrls) || FileName().IsMatch(withoutUrls) ? "a file name"
            : string.Empty;

        return shape.Length > 0
            ? Refuse("path", $"the question carries {shape}", "describe the file by what it does, not by its name or where it lives")
            : null;
    }

    private static WebQuestion.Refused? Hosts(string question) =>
        InternalHost().IsMatch(question)
            ? Refuse("internal-host", "the question names an internal host or address", "an internal address names this network — describe the service, not where it answers")
            : null;

    private static WebQuestion.Refused? Length(string question) =>
        question.Length > MaxChars
            ? Refuse("too-long", $"the question is {question.Length} characters", $"cut it to {MaxChars} characters — a web search is a sentence or two")
            : null;

    private static WebQuestion.Refused Refuse(string cls, string reason, string cure) => new(cls, reason, cure);

    // ---------- the shapes, every one bounded ----------

    [GeneratedRegex(@"(?m)^[ \t]{0,3}(```|~~~)", RegexOptions.CultureInvariant, MatchTimeoutMs)]
    private static partial Regex Fence();

    [GeneratedRegex(@"`[^`\n]{1,200}`", RegexOptions.CultureInvariant, MatchTimeoutMs)]
    private static partial Regex InlineCode();

    /// <summary>C#/JS frames, Python's, PHP's, Rust's panic line, and an exception named with its message.</summary>
    [GeneratedRegex(
        @"(?m)(^[ \t]*at [^\s(]{1,200}(\(|$))|(^[ \t]*File ""[^""\n]{1,300}"", line \d{1,7})|(Traceback \(most recent call last\))|(^[ \t]*#\d{1,4} )|(panicked at )|((^|\s)[A-Za-z_][\w.]{0,120}(Exception|Error):\s)",
        RegexOptions.CultureInvariant, MatchTimeoutMs)]
    private static partial Regex StackTrace();

    /// <summary>A <c>key=value</c> token on its own — a two-character key at least, no spaces, not <c>==</c>, not a URL's <c>?a=b</c> or <c>&amp;a=b</c>.</summary>
    [GeneratedRegex(@"(?m)(?<=^|\s)[A-Za-z_][\w.-]{1,120}=[^\s=]\S{0,400}", RegexOptions.CultureInvariant, MatchTimeoutMs)]
    private static partial Regex ConfigAssignment();

    /// <summary>A <c>key: value</c> LINE whose key reads as a setting — ALL_CAPS, or carrying <c>.</c>, <c>_</c> or <c>-</c>; <c>Question:</c> is prose.</summary>
    [GeneratedRegex(@"(?m)^[ \t]*([A-Z][A-Z0-9_]{2,120}|[A-Za-z_]\w{0,60}[._-][\w.-]{0,120})[ \t]{0,8}:[ \t]{0,8}\S", RegexOptions.CultureInvariant, MatchTimeoutMs)]
    private static partial Regex ConfigColon();

    [GeneratedRegex(@"\b[a-z][a-z0-9+.-]{0,15}://\S{1,2000}", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, MatchTimeoutMs)]
    private static partial Regex Url();

    [GeneratedRegex(@"\b[A-Za-z]:[\\/]", RegexOptions.CultureInvariant, MatchTimeoutMs)]
    private static partial Regex DrivePath();

    [GeneratedRegex(@"(?<!\S)\\\\[A-Za-z0-9_.$-]{1,255}\\", RegexOptions.CultureInvariant, MatchTimeoutMs)]
    private static partial Regex UncPath();

    [GeneratedRegex(@"(?<!\S)(~|\.{1,2})[\\/]", RegexOptions.CultureInvariant, MatchTimeoutMs)]
    private static partial Regex RelativePath();

    [GeneratedRegex(@"\bfile:/{2,3}", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, MatchTimeoutMs)]
    private static partial Regex FileScheme();

    private const string Extensions =
        "cs|ts|tsx|js|mjs|cjs|json|md|yml|yaml|toml|xml|csproj|sln|slnx|py|rs|go|java|kt|php|rb|sh|ps1|cmd|bat|env|ini|cfg|conf|txt|log|sql|html|css|razor|cshtml|vue|svelte|lock|pem|key|dll|exe|vsix";

    /// <summary>Two or more segments ending in a source or config extension — <c>src/Server/PanelService.cs</c>.</summary>
    [GeneratedRegex(@"(?<![\w/\\.])[\w.-]{1,120}([\\/][\w.-]{1,120}){1,40}\.(" + Extensions + @")\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, MatchTimeoutMs)]
    private static partial Regex SegmentedPath();

    /// <summary>A bare file name with such an extension — <c>appsettings.json</c>.</summary>
    [GeneratedRegex(@"(?<![\w/\\.-])[\w-]{1,120}\.(" + Extensions + @")\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, MatchTimeoutMs)]
    private static partial Regex FileName();

    /// <summary>localhost, the loopback and private IPv4 ranges, the unspecified address, IPv6 loopback, and the internal suffixes.</summary>
    [GeneratedRegex(
        @"\blocalhost\b|\b127\.\d{1,3}\.\d{1,3}\.\d{1,3}\b|\b10\.\d{1,3}\.\d{1,3}\.\d{1,3}\b|\b192\.168\.\d{1,3}\.\d{1,3}\b|\b172\.(1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3}\b|\b0\.0\.0\.0\b|::1\b|\b[\w-]{1,63}(\.[\w-]{1,63}){0,10}\.(local|internal|lan|corp|home|intranet)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, MatchTimeoutMs)]
    private static partial Regex InternalHost();
}

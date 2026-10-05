using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Security;

/// <summary>A pattern the table could not use, said for what it was and why (PLAN_one_model_catalog.md E2.4).</summary>
public sealed record PatternRefusal(string Signal, string Pattern, string Why);

/// <summary>A card's own words: the prompt they belong to, and the words — each a word, a phrase, code or a /regex/.</summary>
public sealed record OwnWords(string Prompt, IReadOnlyList<string> Words);

/// <summary>What one signal matches: words (a case-insensitive substring each) and compiled patterns.</summary>
public sealed record SignalMatcher(IReadOnlyList<string> Words, IReadOnlyList<Regex> Patterns)
{
    /// <summary>Whether the text matches — a pattern that runs out of time throws <see cref="RegexMatchTimeoutException"/>.</summary>
    public bool Matches(string text) =>
        Words.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)) || Patterns.Any(pattern => pattern.IsMatch(text));
}

/// <summary>
/// Every signal the security lane detects, with what each matches — the shipped words, a person's words for a signal
/// in their place, and a card's own words as a signal of its own (PLAN_one_model_catalog.md, epic 2, story 4).
/// </summary>
/// <remarks>
/// <para><b>A signal's words REPLACE its shipped words; its shipped pattern stays.</b> The SQL statement shapes are part
/// of how <c>sql</c> is detected, and a person editing the words never saw them.</para>
/// <para><b>Patterns run without backtracking.</b> An entry written <c>/…/</c> is compiled once, with
/// <see cref="RegexOptions.NonBacktracking"/> and a match timeout, so a catastrophic pattern over a megabyte of diff is
/// linear rather than a hung round. What that engine cannot compile — lookaround, a backreference — is refused BY NAME
/// into <see cref="Refused"/>, and the rest of the list goes on working.</para>
/// </remarks>
public sealed record SignalTable(IReadOnlyDictionary<string, SignalMatcher> Signals, IReadOnlyList<PatternRefusal> Refused)
{
    /// <summary>The longest pattern a person may write, in characters.</summary>
    public const int MaxPatternLength = 200;

    /// <summary>The most patterns across the whole table — the signals' and every card's together.</summary>
    public const int MaxPatterns = 32;

    /// <summary>How long one pattern may take over one file before that file's detection is left incomplete.</summary>
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private const RegexOptions PatternOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;

    // Ordinary words such as "where" and "update", and querySelector/executeCommand, are not SQL.
    // Keep common query calls and statement shapes, including removed code and configuration strings.
    private static readonly Regex SqlCode = new(
        @"\b(?:(?:query(?:first|single|multiple)?(?:ordefault)?|execute(?:reader|nonquery|scalar)?)(?:async)?\s*(?:<[^>\r\n]{1,160}>)?\s*\(|select\s+[\s\S]{1,256}\s+from\b|insert\s+into\b|update\s+\S+\s+set\b|delete\s+from\b|(?:create|alter|drop)\s+table\b)",
        PatternOptions,
        MatchTimeout);

    /// <summary>The shipped words, per signal, in the order the lane has always listed them.</summary>
    internal static readonly IReadOnlyList<KeyValuePair<string, string[]>> ShippedWords =
    [
        new("sql", ["sql", "dbcontext", "dbconnection", "dbcommand", "migrationbuilder", "dapper"]),
        new("auth-token", ["jwt", "bearer", "cookie", "session", "authenticate", "oauth", "openid", "oidc", "pkce", "tokenvalidationparameters", "validateissuersigningkey", "redirect_uri", "client_secret"]),
        new("oauth", ["oauth", "openid", "oidc", "pkce", "redirect_uri", "redirecturi", "code_verifier", "code_challenge", "authorization_code", "acquiretoken", "msal"]),
        new("authz", ["authorize", "authorization", "permission", "tenant", "role", "allowanonymous", "mapget", "mappost", "mapput", "mapdelete", "httppost", "httpget", "controller", "route(", "user.claims", "companyid", "frombody", "dbcontext.update", ".updateasync", "patch", "endpoint"]),
        new("xss", ["innerhtml", "outerhtml", "dangerouslysetinnerhtml", "document.write", "webview", "<script", "markupstring", "htmlstring", "response.writeasync", "v-html"]),
        new("ssrf", ["httpclient", "httprequestmessage", "fetch(", "axios", "webrequest", "restsharp", "requests.get", "urllib", "redirect", "url"]),
        new("path", ["path.", "file.", "directory.", "readfile", "writefile", "extract", "archive"]),
        new("upload", ["iformfile", "multipart", "uploadedfile", "uploadfile", "fileupload", "multer", "formdata", "request.files", "request.form.files"]),
        new("command", ["process", "exec(", "execfile", "spawn(", "shell", "subprocess", "cmd.exe", "/bin/sh"]),
        new("deserialize", ["deserialize", "pickle", "yaml.load", "binaryformatter", "json.parse", "typenamehandling", "dtdprocessing", "type.gettype"]),
        new("secrets", ["secret", "password", "credential", "apikey", "api_key", "connectionstring", "ilogger", "loginformation", "logerror", "bearer"]),
        new("crypto", ["encrypt", "decrypt", "sha1", "md5", "random", "cryptograph", "cipher"]),
        new("concurrency", ["stripe", "paymentintent", "rowversion", "dbupdateconcurrencyexception", "balance", "credit", "transactionscope", "semaphoreslim", "lock (", "lock("]),
        new("webhooks", ["webhook", "stripe-signature", "x-hub-signature", "hmacsha256", "fixedtimeequals", "crypto.createhmac", "timestamp"]),
        new("prompt-injection", ["ichatclient", "kernel", "openaiclient", "anthropic", "tooldefinition", "system_prompt", "user_input"]),
        new("entry-point", ["mapget", "mappost", "controller", "endpoint", "handler", "route", "main("]),
    ];

    /// <summary>The table with nothing changed — what the lane detects when the setting says no words.</summary>
    public static SignalTable Shipped { get; } = Build(new Dictionary<string, IReadOnlyList<string>>(), []);

    /// <summary>The signals with a person's words in place of the shipped ones, and each card's own words as a signal.</summary>
    /// <param name="words">Per signal, the words that replace its shipped words; a signal not named keeps its own.</param>
    public static SignalTable Build(IReadOnlyDictionary<string, IReadOnlyList<string>> words, IReadOnlyList<OwnWords> own)
    {
        var compiler = new PatternCompiler();
        var signals = new Dictionary<string, SignalMatcher>(StringComparer.Ordinal);
        foreach (var (signal, shipped) in ShippedWords)
        {
            signals[signal] = ShippedSignal(compiler, signal, words.GetValueOrDefault(signal, shipped));
        }
        foreach (var card in own)
        {
            signals[SecurityPrompt.OwnSignalOf(card.Prompt)] = compiler.Matcher(SecurityPrompt.OwnSignalOf(card.Prompt), card.Words);
        }

        return new(signals, compiler.Refused);
    }

    /// <summary>A shipped signal's matcher over the words it is given — with the SQL statement shapes kept on <c>sql</c>.</summary>
    private static SignalMatcher ShippedSignal(PatternCompiler compiler, string signal, IReadOnlyList<string> words)
    {
        var matcher = compiler.Matcher(signal, words);

        return signal == "sql" ? matcher with { Patterns = [SqlCode, .. matcher.Patterns] } : matcher;
    }

    /// <summary>Compiles one table's patterns, counting them against the one cap and recording every refusal by name.</summary>
    private sealed class PatternCompiler
    {
        private readonly List<PatternRefusal> _refused = [];
        private int _patterns;

        public IReadOnlyList<PatternRefusal> Refused => _refused;

        public SignalMatcher Matcher(string signal, IEnumerable<string> entries)
        {
            var list = entries.Where(entry => entry.Trim().Length > 0).ToList();

            return new(
                [.. list.Where(entry => !IsPattern(entry)).Select(entry => entry.Trim())],
                [.. list.Where(IsPattern).SelectMany(entry => Compiled(signal, entry.Trim()))]);
        }

        private IEnumerable<Regex> Compiled(string signal, string entry)
        {
            var refusal = Admission(entry);
            if (refusal.Length > 0)
            {
                _refused.Add(new(signal, entry, refusal));
                return [];
            }
            _patterns += 1;

            return Compile(signal, entry);
        }

        private string Admission(string entry) =>
            entry.Length - 2 > MaxPatternLength ? $"a pattern is at most {MaxPatternLength} characters; this one is {entry.Length - 2}"
            : _patterns >= MaxPatterns ? $"the lane takes at most {MaxPatterns} patterns across its signals and cards; this one is past that"
            : string.Empty;

        private IEnumerable<Regex> Compile(string signal, string entry)
        {
            try
            {
                return [new Regex(entry[1..^1], PatternOptions, MatchTimeout)];
            }
            catch (NotSupportedException e)
            {
                _refused.Add(new(signal, entry, WhyNotSupported(e.Message)));
            }
            catch (ArgumentException e)
            {
                _refused.Add(new(signal, entry, $"not a valid pattern: {e.Message}"));
            }

            return [];
        }

        private static string WhyNotSupported(string message) =>
            message.Contains("look", StringComparison.OrdinalIgnoreCase)
                ? "lookaround — (?=…), (?!…), (?<=…), (?<!…) — needs backtracking, which this engine refuses so a pattern cannot hang a round"
                : message.Contains("backreference", StringComparison.OrdinalIgnoreCase)
                ? "a backreference needs backtracking, which this engine refuses so a pattern cannot hang a round"
                : $"this construct needs backtracking, which this engine refuses: {message}";
    }

    /// <summary>An entry written <c>/…/</c> — a pattern; anything else is a word, matched as a substring.</summary>
    public static bool IsPattern(string entry)
    {
        var trimmed = entry.Trim();

        return trimmed.Length > 2 && trimmed[0] == '/' && trimmed[^1] == '/';
    }
}

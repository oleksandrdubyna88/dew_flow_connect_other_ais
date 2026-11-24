using System.Text.Json;
using System.Text.RegularExpressions;

namespace CoaiMcp.Server.Keys;

/// <summary>What a source said: the keys it lists, and the lines it listed that cannot be keys (by number and rule, never the line).</summary>
internal sealed record SourceRead(IReadOnlyList<KeyInput> Inputs, IReadOnlyList<KeyRefusedLine> Refused, IReadOnlySet<string> SeenLabels);

/// <summary>
/// Reading the generic sources of keys (KEY_POOL_SPEC.md, the owner's correction of 2026-11-23): the environment of the coai process, and a file at a path the person chose, in the env-line format or as the creds JSON.
/// </summary>
/// <remarks>
/// <para><b>Nothing here echoes input.</b> A refused line is named by its number and the rule; a variable whose label is wrong is named by its position. The text of a value is never in a sentence.</para>
/// <para><b>A source that cannot be read changes nothing.</b> The caller sweeps ("a key the source no longer lists is disabled") only after a read that succeeded: a file with a wrong mode, an unreadable file or a creds JSON with an
/// unknown key is refused whole, and the keys of that source stay as they were.</para>
/// <para>The creds code-access key (the variable the creds CLI reads it from) is never read here: the creds import takes the entry's BODY on stdin from the person's own command, and coai keeps no second copy of anything.</para>
/// </remarks>
internal static partial class KeySourceReader
{
    internal const string KeyPrefix = "COAI_CLAUDE_KEY_";
    internal const string AccountPrefix = "COAI_CLAUDE_ACCOUNT_";
    internal const long MaxFileBytes = 1024 * 1024;

    /// <summary>A label from a source: 1 to 40 of <c>a-z 0-9 _ -</c> after lower-casing (the underscore because a shell cannot name a variable with a hyphen).</summary>
    [GeneratedRegex("^[a-z0-9_-]{1,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex SourceLabel();

    internal static bool IsAccount(string account) => Account().IsMatch(account);

    /// <summary>An account: an email-shaped text without a space or control character, 254 characters at most.</summary>
    [GeneratedRegex(@"^[^\s\p{C}]{1,254}$", RegexOptions.CultureInvariant)]
    private static partial Regex Account();

    // -------------------------------------------------------------------------------------------------------------
    // the environment
    // -------------------------------------------------------------------------------------------------------------

    public static SourceRead FromEnvironment(IEnumerable<KeyValuePair<string, string>> variables)
    {
        var all = variables.ToList();
        var accounts = all.Where(pair => pair.Key.StartsWith(AccountPrefix, StringComparison.Ordinal))
            .GroupBy(pair => pair.Key[AccountPrefix.Length..].ToLowerInvariant())
            .ToDictionary(group => group.Key, group => group.First().Value);
        var builder = new SourceBuilder(KeySources.Environment);
        var position = 0;
        foreach (var pair in all.Where(pair => pair.Key.StartsWith(KeyPrefix, StringComparison.Ordinal)).OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            builder.Take(++position, pair.Key[KeyPrefix.Length..], pair.Value.Trim(), accounts);
        }

        return builder.Done();
    }

    // -------------------------------------------------------------------------------------------------------------
    // a file
    // -------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The keys of the file at <paramref name="path"/>, or why it is refused as a whole. On Linux the file must not be readable by group or other; on Windows the owner-only ACL check is not built, so the file is refused.
    /// </summary>
    public static KeyResult<SourceRead> FromFile(string path, Func<string, UnixFileMode> modeOf)
    {
        if (!File.Exists(path))
        {
            return KeyResult<SourceRead>.Refused("the keys file is not there");
        }

        if (WrongMode(path, modeOf) is { Length: > 0 } wrong)
        {
            return KeyResult<SourceRead>.Refused(wrong);
        }

        string text;
        try
        {
            if (new FileInfo(path).Length > MaxFileBytes)
            {
                return KeyResult<SourceRead>.Refused($"the keys file is larger than {MaxFileBytes} bytes");
            }

            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return KeyResult<SourceRead>.Refused($"the keys file could not be read: {e.GetType().Name}", KeyExit.Io);
        }

        return Parse(text, KeySources.File(Path.GetFullPath(path)));
    }

    private static string WrongMode(string path, Func<string, UnixFileMode> modeOf)
    {
        if (OperatingSystem.IsWindows())
        {
            return "the keys file is refused on Windows: its owner-only ACL check is not built yet — use the environment variables or --keys import";
        }

        const UnixFileMode others = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

        return (modeOf(path) & others) == 0
            ? string.Empty
            : "the keys file is readable by group or other — make it private first (chmod 600) and start again; nothing was read";
    }

    /// <summary>The format is detected: a first character <c>{</c> is the creds JSON, anything else the env lines.</summary>
    public static KeyResult<SourceRead> Parse(string text, string source) =>
        text.TrimStart().StartsWith('{') ? FromCredsJson(text, source) : KeyResult<SourceRead>.Of(FromEnvLines(text, source));

    // -------------------------------------------------------------------------------------------------------------
    // env lines
    // -------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>COAI_CLAUDE_KEY_&lt;label&gt;=&lt;token&gt;</c> and optional <c>COAI_CLAUDE_ACCOUNT_&lt;label&gt;=&lt;email&gt;</c>; comments (<c>#</c>) and blank lines are fine; any other line is refused, by its number.
    /// </summary>
    public static SourceRead FromEnvLines(string text, string source)
    {
        var accounts = new Dictionary<string, string>(StringComparer.Ordinal);
        var keys = new List<(int Line, string Label, string Value)>();
        var builder = new SourceBuilder(source);
        var number = 0;
        foreach (var raw in text.Split('\n'))
        {
            number++;
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            Classify(builder, accounts, keys, number, line);
        }

        foreach (var (line, label, value) in keys)
        {
            builder.Take(line, label, value, accounts);
        }

        return builder.Done();
    }

    private static void Classify(SourceBuilder builder, Dictionary<string, string> accounts, List<(int Line, string Label, string Value)> keys, int number, string line)
    {
        var split = line.IndexOf('=');
        var name = split > 0 ? line[..split] : string.Empty;
        var value = split > 0 ? line[(split + 1)..].Trim() : string.Empty;
        if (name.StartsWith(KeyPrefix, StringComparison.Ordinal))
        {
            keys.Add((number, name[KeyPrefix.Length..], value));
        }
        else if (name.StartsWith(AccountPrefix, StringComparison.Ordinal))
        {
            accounts[name[AccountPrefix.Length..].ToLowerInvariant()] = value;
        }
        else
        {
            builder.Refuse(number, "this line is neither a comment, nor COAI_CLAUDE_KEY_<label>=<token>, nor COAI_CLAUDE_ACCOUNT_<label>=<email>");
        }
    }

    // -------------------------------------------------------------------------------------------------------------
    // creds json
    // -------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The body of the owner's CredsForDevs entry: <c>{"version":1,"keys":[{"label","account","token"}]}</c>. An unknown key anywhere, a wrong version or a repeated label refuses the whole text.
    /// </summary>
    public static KeyResult<SourceRead> FromCredsJson(string text, string source)
    {
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8, AllowTrailingCommas = false });

            return CredsRead(document.RootElement, source);
        }
        catch (JsonException)
        {
            return KeyResult<SourceRead>.Refused("the creds JSON is not valid JSON (nothing was read)");
        }
    }

    private static KeyResult<SourceRead> CredsRead(JsonElement root, string source)
    {
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(property => property.Name is not ("version" or "keys")))
        {
            return KeyResult<SourceRead>.Refused("the creds JSON must be an object with only `version` and `keys` (nothing was read)");
        }

        if (root.TryGetProperty("version", out var version) && !(version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var number) && number == 1))
        {
            return KeyResult<SourceRead>.Refused("the creds JSON version must be 1 (nothing was read)");
        }

        if (!root.TryGetProperty("keys", out var keys) || keys.ValueKind != JsonValueKind.Array)
        {
            return KeyResult<SourceRead>.Refused("the creds JSON needs a `keys` list (nothing was read)");
        }

        return CredsKeys(keys, source);
    }

    private static KeyResult<SourceRead> CredsKeys(JsonElement keys, string source)
    {
        var builder = new SourceBuilder(source);
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var position = 0;
        foreach (var item in keys.EnumerateArray())
        {
            position++;
            if (CredsEntry(item) is not { } entry)
            {
                return KeyResult<SourceRead>.Refused($"key {position} of the creds JSON must be an object with only `label`, `account` and `token`, all text (nothing was read)");
            }

            if (!labels.Add(entry.Label))
            {
                return KeyResult<SourceRead>.Refused($"key {position} of the creds JSON repeats a label (nothing was read)");
            }

            builder.TakeAsGiven(position, entry.Label, entry.Token, entry.Account);
        }

        return KeyResult<SourceRead>.Of(builder.Done());
    }

    private static (string Label, string Account, string Token)? CredsEntry(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Any(property => property.Name is not ("label" or "account" or "token") || property.Value.ValueKind != JsonValueKind.String))
        {
            return null;
        }

        string Text(string name) => item.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;

        return (Text("label"), Text("account"), Text("token"));
    }

    // -------------------------------------------------------------------------------------------------------------
    // the one builder
    // -------------------------------------------------------------------------------------------------------------

    private sealed class SourceBuilder(string source)
    {
        private readonly List<KeyInput> _inputs = [];
        private readonly List<KeyRefusedLine> _refused = [];
        private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

        public void Refuse(int line, string reason) => _refused.Add(new KeyRefusedLine(line, reason));

        /// <summary>A key of the env or env-line shape: the label is a source label (lower-cased), the account comes from the matching account variable.</summary>
        public void Take(int line, string rawLabel, string value, IReadOnlyDictionary<string, string> accounts)
        {
            var label = rawLabel.ToLowerInvariant();
            if (!SourceLabel().IsMatch(label))
            {
                Refuse(line, "the label must be 1 to 40 of a-z 0-9 _ - (after lower-casing)");

                return;
            }

            _seen.Add(label);
            TakeAsGiven(line, label, value, accounts.GetValueOrDefault(label, string.Empty));
        }

        public void TakeAsGiven(int line, string label, string value, string account)
        {
            _seen.Add(label);
            if (value.Length == 0)
            {
                Refuse(line, "the key has no value");
            }
            else if (account.Length > 0 && !Account().IsMatch(account))
            {
                Refuse(line, "the account is not an email-shaped text without spaces");
            }
            else
            {
                _inputs.Add(new KeyInput(line, label, value, account, source));
            }
        }

        public SourceRead Done() => new(_inputs, _refused, _seen);
    }
}

using System.Text.Json.Serialization;

namespace CoaiMcp.Server.Keys;

/// <summary>The states of a key (KEY_POOL_SPEC.md section 3). A key is never "cooling" as a state: a cooldown is a time on an <c>enabled</c> key.</summary>
internal static class KeyStates
{
    public const string Enabled = "enabled";
    public const string Disabled = "disabled";
    public const string Dead = "dead";
}

/// <summary>Why a key is <c>dead</c>.</summary>
internal static class KeyDeadReasons
{
    /// <summary>The vendor refused the key (only a signal with a recorded fixture counts).</summary>
    public const string Auth = "auth";

    /// <summary>The exact text of the key was found in what its own launch printed or in the coder's changed files.</summary>
    public const string Exposed = "exposed";
}

/// <summary>What is wrong with a key's files, as a list row says it — empty when nothing is.</summary>
internal static class KeyProblems
{
    /// <summary>The metadata is there and the key file is not.</summary>
    public const string Missing = "missing";

    /// <summary>The key file has a wider mode than the store makes: it is not used, and the row says so.</summary>
    public const string WideMode = "wide-mode";
}

/// <summary>
/// The metadata of one key — everything about it except its text (KEY_POOL_SPEC.md section 3). Written beside the key file as <c>k-&lt;12 hex&gt;.json</c>.
/// </summary>
/// <remarks>
/// <para>There is NO member that holds the token. A record prints its members, and a metadata record is logged, listed and sent to a page: it must be safe to print whatever a caller does with it.</para>
/// <para><b>A field the file omitted is null under the source generator whatever its initialiser says</b> (csharp doctrine 4a), so every read goes through <see cref="Normalised"/>.</para>
/// </remarks>
internal sealed record KeyRecord
{
    public const string TokenKind = "claude-setup-token";

    public string Id { get; init; } = string.Empty;
    public string Kind { get; init; } = TokenKind;
    public string Label { get; init; } = string.Empty;
    public string State { get; init; } = KeyStates.Enabled;
    public string DeadReason { get; init; } = string.Empty;

    /// <summary>The subscription the key belongs to (an email), when the source said so: keys of one account share one limit.</summary>
    public string Account { get; init; } = string.Empty;

    /// <summary>Where the key came from: <c>local</c> (add, import), <c>env</c>, <c>file:&lt;path&gt;</c> or a name the importer was given. Only a sync of a source touches the keys of that source.</summary>
    public string Source { get; init; } = KeySources.Local;

    /// <summary>The key was disabled by a sync because its source no longer lists it (so a sync that lists it again may enable it; a person's own disable stays).</summary>
    public bool SyncDisabled { get; init; }

    public DateTime AddedUtc { get; init; }
    public DateTime? LastUsedUtc { get; init; }
    public DateTime? CooldownUntilUtc { get; init; }

    /// <summary>The id of the OTHER key whose limit set this cooldown (its account was this key's account then), or empty when the cooldown is this key's own: a changed account drops only a cooldown that came from another key.</summary>
    public string CooldownFrom { get; init; } = string.Empty;

    public int ConsecutiveCooldowns { get; init; }
    public int Launches { get; init; }
    public int LimitStops { get; init; }
    public int AuthStops { get; init; }

    /// <summary>The last stop, through <c>Redaction.SafeText</c>.</summary>
    public string Note { get; init; } = string.Empty;

    /// <summary>The record with every string a deserializer may have left null made empty.</summary>
    public KeyRecord Normalised() => this with
    {
        Id = Id ?? string.Empty,
        Kind = Kind ?? TokenKind,
        Label = Label ?? string.Empty,
        State = State ?? KeyStates.Enabled,
        DeadReason = DeadReason ?? string.Empty,
        Account = Account ?? string.Empty,
        Source = Source ?? KeySources.Local,
        CooldownFrom = CooldownFrom ?? string.Empty,
        Note = Note ?? string.Empty,
    };

    public bool InCooldownAt(DateTime nowUtc) => CooldownUntilUtc is { } until && until > nowUtc;
}

/// <summary><c>pool.json</c>: the cursor and the counters that belong to the pool, not to a key.</summary>
internal sealed record PoolFile
{
    public int Schema { get; init; } = 1;

    /// <summary>Moves on a change by a person (add, relabel, disable, enable, remove, a sync that changed something) and NEVER on a pick or a counter.</summary>
    public int AdminRevision { get; init; }

    /// <summary>The id of the key the last pick answered; gone = the next pick starts at the first key.</summary>
    public string Cursor { get; init; } = string.Empty;

    /// <summary>When the last <c>claude-keys-exhausted</c> notice was written: at most one in 30 minutes.</summary>
    public DateTime? ExhaustedNoticeUtc { get; init; }

    /// <summary>
    /// The ids (hash prefixes, never texts) of every key that was ever found exposed (second security judgement): EXPOSED IS FINAL. Kept when the key is removed, so a remove followed by an add, or a sync that lists the same text again, cannot make it a usable key.
    /// </summary>
    public IReadOnlyList<string> ExposedIds { get; init; } = [];

    public PoolFile Normalised() => this with { Cursor = Cursor ?? string.Empty, ExposedIds = ExposedIds ?? [] };
}

/// <summary>One row of a list: the metadata, and whether the key can be picked now. Never a token.</summary>
internal sealed record KeyRow(
    string Id,
    string Kind,
    string Label,
    string State,
    string DeadReason,
    string Account,
    string Source,
    DateTime AddedUtc,
    DateTime? LastUsedUtc,
    DateTime? CooldownUntilUtc,
    int ConsecutiveCooldowns,
    int Launches,
    int LimitStops,
    int AuthStops,
    string Note,
    string Problem,
    bool Usable);

/// <summary>The answer of <c>--keys list</c> and the shape of the Keys page's list (section 8).</summary>
internal sealed record KeyListAnswer(int AdminRevision, int Usable, bool PersonSignInSwitch, IReadOnlyList<KeyRow> Rows, int UnreadableMetadata);

/// <summary>The answer of <c>--keys add</c>.</summary>
internal sealed record KeyAddedAnswer(string Id, string Label, string State, DateTime AddedUtc);

/// <summary>The answer of <c>--keys remove</c>.</summary>
internal sealed record KeyRemovedAnswer(string Removed);

/// <summary>One line the importer did not take: its number and the rule — never the line.</summary>
internal sealed record KeyRefusedLine(int Line, string Reason);

/// <summary>The answer of <c>--keys import</c> and <c>--keys sync</c>: counts only.</summary>
internal sealed record KeyImportAnswer(int Added, int AlreadyThere, int Replaced, int Disabled, int Enabled, IReadOnlyList<KeyRefusedLine> Refused);

/// <summary>A file under the scanned path that holds the exact text of a pool key: where, and WHICH key — never the text.</summary>
internal sealed record KeyScanHit(string Path, string KeyId);

/// <summary>The answer of <c>--keys scan</c>.</summary>
internal sealed record KeyScanAnswer(int Scanned, int Unreadable, IReadOnlyList<KeyScanHit> Hits);

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(KeyRecord))]
[JsonSerializable(typeof(PoolFile))]
[JsonSerializable(typeof(KeyListAnswer))]
[JsonSerializable(typeof(KeyAddedAnswer))]
[JsonSerializable(typeof(KeyRemovedAnswer))]
[JsonSerializable(typeof(KeyImportAnswer))]
[JsonSerializable(typeof(KeyScanAnswer))]
[JsonSerializable(typeof(KeyRow))]
internal sealed partial class KeysJson : JsonSerializerContext;

/// <summary>What one source's sync came to: counts, or why the source was refused whole (and left as it was).</summary>
internal sealed record KeySourceResult(string Source, KeyImportAnswer? Counts, string Refusal);

/// <summary>The answer of <c>--keys sync</c>: one entry per source that is configured or has keys in the pool.</summary>
internal sealed record KeySyncAnswer(IReadOnlyList<KeySourceResult> Sources);

/// <summary>One-line answers: the same types the files use, without the indentation.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(KeyListAnswer))]
[JsonSerializable(typeof(KeyAddedAnswer))]
[JsonSerializable(typeof(KeyRow))]
[JsonSerializable(typeof(KeyRemovedAnswer))]
[JsonSerializable(typeof(KeyImportAnswer))]
[JsonSerializable(typeof(KeyScanAnswer))]
[JsonSerializable(typeof(KeySyncAnswer))]
internal sealed partial class KeysAnswerJson : JsonSerializerContext;

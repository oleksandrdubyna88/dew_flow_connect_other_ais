namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// Where an effort or a tier on a ledger line came from — the closed vocabulary of <c>effortFrom</c> and <c>tierFrom</c>
/// (story 3.4 of todo/PLAN_every_round_is_counted.md).
/// </summary>
/// <remarks>
/// <para>Here, in the library both halves compile against, for the reason <see cref="UsageKinds"/> and
/// <see cref="LaunchAttempts"/> live here: the local roster and the Team server write these words into one ledger, and
/// two spellings of one field is how a writer and a reader come to disagree about what a line means.</para>
/// <para><b>Why a source at all.</b> The plan's symptom 6: a codex row's effort was configured and never sent, so every
/// local codex launch ran at whatever <c>~/.codex/config.toml</c> said — <c>high</c> on the operator's machine — while the
/// row said something else, and nothing anywhere recorded which of the two a launch had used. A value without its source
/// reads as the row's choice; the source is what tells "I asked for this" from "this is what I got".</para>
/// </remarks>
public static class OptionSources
{
    /// <summary>The value was SENT, from coai's settings for the row — its own value, or what coai fills in for it (the
    /// panel's local-engine level for a local row with none, an api module's effective level). On a Team server, the
    /// effort the request carried and the server took as asked.</summary>
    public const string Row = "row";

    /// <summary>The value was sent, and the Team server chose it: an effort lowered to its cap (<c>Coai:MaxEffort</c>), or
    /// a tier its launch path sets because the request carries none.</summary>
    public const string Server = "server";

    /// <summary>The row set a value and this launch path does not send it — a codex row's effort, which codex is never
    /// told (<c>RosterBuilder.EffortFor</c>), or a tier the installed release cannot take. The value is then empty: what
    /// the row asked for did not run, and nothing is claimed about what did.</summary>
    public const string Ignored = "ignored";

    /// <summary>The Team server refused the value the request carried (<c>ClientOptions</c>'s <c>Dropped</c>) — a codex
    /// effort, which no server applies. The value is empty, for the same reason as <see cref="Ignored"/>.</summary>
    public const string Dropped = "dropped";

    /// <summary>Nothing was asked and nothing was sent, and the CLI's own configuration names a value — which is what the
    /// CLI then ran with. Read only to LABEL the line: reading it never adds a flag to the launch.</summary>
    public const string CliConfig = "cli-config";

    /// <summary>Every source this build names. An EMPTY source is the sixth answer: nothing was sent and nothing known
    /// names a value — the vendor's own default, unknown here.</summary>
    public static IReadOnlyList<string> Known { get; } = [Row, Server, Ignored, Dropped, CliConfig];
}

/// <summary>What one launch put on its CLI's command line — an effort and a tier, each empty when none was sent.</summary>
/// <param name="Effort">The reasoning effort on the argv (or, for a Team server row, in the request), exactly as sent.</param>
/// <param name="Tier">The service tier the CLI was told, in codex's words: <c>default</c> (the standard tier) or <c>fast</c>.</param>
/// <remarks>
/// <para>Set by each adapter's <c>Build</c> from the very values it puts on the argv, so "what was sent" is never
/// re-derived from settings somewhere else. Beside <see cref="ReviewerInvocation.Effort"/> rather than instead of it: that
/// one is the display's (issue #129: the effort THIS machine's CLI applied), and a Team server row's effort is a request
/// the server may refuse, so the display does not show it and the ledger does — labelled.</para>
/// <para>Claude's fast mode is spelled in codex's words too — <c>fastMode:true</c> is <c>fast</c>, <c>false</c> is
/// <c>default</c> — so one column compares across vendors; the argv itself is what the test reads to hold that.</para>
/// </remarks>
public sealed record SentOptions(string Effort = "", string Tier = "")
{
    /// <summary>A launch that sent neither.</summary>
    public static SentOptions Nothing { get; } = new();

    /// <summary>The tier a codex <c>-c service_tier=…</c> pair names — or empty when the arguments name none.</summary>
    public static string TierIn(IReadOnlyList<string> tierArgs) =>
        tierArgs is ["-c", var setting] && setting.StartsWith(TierKey, StringComparison.Ordinal) ? setting[TierKey.Length..] : string.Empty;

    private const string TierKey = "service_tier=";
}

/// <summary>What the CLI's own configuration names, for a value nothing sent — empty when it names none, or cannot be read.</summary>
public sealed record CliDefaults(string Effort = "", string Tier = "")
{
    /// <summary>A CLI whose configuration is not read: nothing is labelled from it.</summary>
    public static CliDefaults None { get; } = new();

    /// <summary>
    /// codex's <c>config.toml</c> under the launch's environment: <c>model_reasoning_effort</c>, and <c>service_tier</c> only
    /// when the row is on codex's own service (<paramref name="hasTier"/>) — somebody else's endpoint has no codex tier.
    /// </summary>
    /// <param name="env">The environment the LAUNCH will have — on a Team server, the slot's <c>CODEX_HOME</c>/<c>HOME</c>.</param>
    public static CliDefaults OfCodex(Func<string, string?> env, bool hasTier)
    {
        var toml = CodexConfigFile.TextIn(env);

        return new(
            CodexConfigFile.RootValue(toml, "model_reasoning_effort"),
            hasTier ? CodexConfigFile.RootValue(toml, "service_tier") : string.Empty);
    }
}

/// <summary>One option of a launch: what was sent and on whose say, else why the value asked for was not.</summary>
/// <param name="Sent">The value on the argv; empty when none was.</param>
/// <param name="SentFrom"><see cref="OptionSources.Row"/> or <see cref="OptionSources.Server"/> — who chose a value that was sent.</param>
/// <param name="NotSentAs"><see cref="OptionSources.Ignored"/> or <see cref="OptionSources.Dropped"/> when a value was asked
/// for and did not reach the CLI; empty when none was asked.</param>
public sealed record OptionChoice(string Sent, string SentFrom, string NotSentAs = "")
{
    /// <summary>
    /// The value and its source, in this order: what was sent; else why what was asked was not; else what the CLI's own
    /// configuration names; else nothing — the vendor's default, which nothing here knows.
    /// </summary>
    /// <remarks>
    /// Asked before sent is never possible: a value on the argv is what the launch ran with, whatever the row said. And
    /// the configuration is read LAST, only for a value nothing chose — a row's ignored effort is not relabelled as the
    /// configuration's, because the line's question is whether the row's setting ran, and it did not.
    /// </remarks>
    public (string Value, string From) Labelled(string configured) => (Sent, NotSentAs, configured) switch
    {
        ({ Length: > 0 }, _, _) => (Sent, SentFrom),
        (_, { Length: > 0 }, _) => (string.Empty, NotSentAs),
        (_, _, { Length: > 0 }) => (configured, OptionSources.CliConfig),
        _ => (string.Empty, string.Empty),
    };
}

/// <summary>
/// What a launch RAN WITH, as its ledger line says it: which CLI, which release of it, and the effort and tier each with
/// its source (story 3.4 of todo/PLAN_every_round_is_counted.md).
/// </summary>
/// <param name="Cli">The runtime that ran the launch, in the catalog's words — <c>codex</c>, <c>claude</c>, <c>antigravity</c>,
/// <c>gemini</c>, <c>local</c>, <c>api</c>, <c>remote</c>. A cross-vendor measurement must say which CLI ran each model, and
/// a model id alone cannot (one model can run on two).</param>
/// <param name="CliVersion">The first line of the CLI's own <c>--version</c>, where one was ALREADY asked — never a spawn per
/// launch: locally the codex tier probe's answer (an Off codex row on codex's own service), on a Team server the catalog's
/// cached health probe. Empty where no version was asked, which is every other launch.</param>
public sealed record LaunchOptions(
    string Cli = "",
    string CliVersion = "",
    string Effort = "",
    string EffortFrom = "",
    string Tier = "",
    string TierFrom = "")
{
    /// <summary>Nothing known — a line written by a path that does not label its launches (a consultation, a probe).</summary>
    public static LaunchOptions None { get; } = new();

    /// <summary>The line's fields for one launch, from what it sent, why anything asked was not, and the CLI's configuration.</summary>
    public static LaunchOptions Of(string cli, string cliVersion, OptionChoice effort, OptionChoice tier, CliDefaults configured)
    {
        var (effortValue, effortFrom) = effort.Labelled(configured.Effort);
        var (tierValue, tierFrom) = tier.Labelled(configured.Tier);

        return new(cli, cliVersion, effortValue, effortFrom, tierValue, tierFrom);
    }

    /// <summary>The first line of a <c>--version</c> answer — what a ledger line keeps of it.</summary>
    public static string FirstLine(string printed) => printed.Trim().Split('\n')[0].Trim();
}

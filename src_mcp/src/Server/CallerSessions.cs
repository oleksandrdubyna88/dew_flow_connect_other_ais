namespace CoaiMcp.Server;

/// <summary>
/// Who is calling us — the AI's OWN session, which is not the same thing as ours.
/// </summary>
/// <remarks>
/// <para>Our session is keyed by repo+branch and its plan stage happens ONCE: after a plan proceeds
/// the stage advances and a second <c>review_plan</c> on that session is refused outright. So the
/// epics produced by a split come back on their own branches, as their own sessions — which is
/// exactly why a per-session memory cannot see them, and why the caller has to be identified.</para>
/// <para>Claude Code exports <c>CLAUDE_CODE_SESSION_ID</c> to the children it spawns, and an MCP
/// server on stdio is one of those children. Nothing has to be passed, remembered or trusted to the
/// model's own bookkeeping. <c>COAI_CALLER_SESSION</c> is first so a client without an id of its own
/// can still be given one.</para>
/// </remarks>
/// <param name="Vendor">
/// Whose client it is, from the variable that matched — <see cref="Unknown"/> when none did.
/// </param>
/// <param name="Id">That client's own session id, or empty.</param>
public readonly record struct CallerIdentity(string Vendor = CallerIdentity.Unknown, string Id = "")
{
    /// <summary>Nothing identified this caller. A state with a name, never a blank.</summary>
    /// <remarks>
    /// The first build of this returned an empty string for it, and the plan round was right about
    /// what that does: a blank renders as a gap, and a gap beside a model reads as though the vendor
    /// were known and merely not worth printing.
    /// </remarks>
    public const string Unknown = "unknown";

    /// <summary>
    /// The id was handed to us by the operator, so it says nothing about which vendor this is.
    /// </summary>
    /// <remarks>
    /// <c>COAI_CALLER_SESSION</c> exists so a client with no id of its own can be given one. Naming
    /// a vendor from it would be an invention, and this whole change is about not doing that.
    /// </remarks>
    public const string Stated = "stated";

    /// <summary>Which vendor each variable belongs to. The order is the precedence.</summary>
    private static readonly (string Variable, string Vendor)[] Variables =
    [
        ("COAI_CALLER_SESSION", Stated),
        ("CLAUDE_CODE_SESSION_ID", "claude"),
        ("CODEX_SESSION_ID", "codex"),
        ("GEMINI_CLI_SESSION_ID", "gemini"),
    ];

    /// <summary>The first of them that is set, with the vendor that variable names.</summary>
    public static CallerIdentity From(Func<string, string?> read)
    {
        foreach (var (variable, vendor) in Variables)
        {
            if (read(variable) is { } value && !string.IsNullOrWhiteSpace(value))
            {
                return new CallerIdentity(vendor, value.Trim());
            }
        }

        return new CallerIdentity(Unknown, string.Empty);
    }

    public static CallerIdentity Current() => From(Environment.GetEnvironmentVariable);

    /// <summary>The caller kinds a consultant can be configured for.</summary>
    public const string Claude = "claude";
    public const string Codex = "codex";
    public const string Gemini = "gemini";
    public const string Other = "other";

    public static IReadOnlyList<string> Kinds { get; } = [Claude, Codex, Gemini, Other];

    /// <summary>
    /// WHICH vendor is calling — decided by the vendor variables alone.
    /// </summary>
    /// <remarks>
    /// <para>A second question beside <see cref="From"/>, not an index into its array:
    /// <c>COAI_CALLER_SESSION</c> is an identity override with no vendor meaning, and reading the
    /// kind off it would call every scripted client "other" while it names a Claude session.</para>
    /// <para>Sound because this server is a stdio child of exactly ONE client per process, which
    /// exports its own session variable to it — the same fact <see cref="From"/> rests on. (The plan
    /// round asked for per-request identity; there is no request-level caller on stdio.)</para>
    /// </remarks>
    public static string KindFrom(Func<string, string?> read)
    {
        // The SAME table `From` reads, minus the override row. It used to repeat the three variable
        // names, and nothing would have failed at compile time if the two lists drifted: a rename in
        // `Variables` would leave this reading a variable nobody sets, and every caller of that
        // vendor would quietly become `other` — a routing decision made by a typo. (CodeRabbit.)
        foreach (var (variable, vendor) in Variables)
        {
            if (vendor != Stated && !string.IsNullOrWhiteSpace(read(variable)))
            {
                return vendor;
            }
        }

        return Other;
    }
}

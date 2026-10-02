using CoaiMcp.Core.QuestionConsult;

namespace CoaiMcp.Server;

/// <summary>
/// The question consultant's settings — the <c>COAI_QCONSULT_*</c> keys of <c>todo/PLAN_question_consultant.md</c> §4,
/// in a record of their own (D12: <c>PanelSettings</c> grows by one property, not eight).
/// </summary>
/// <remarks>
/// Every default is the operator's accepted one: on, <c>require</c>, no rows, the shipped prompts, no roots,
/// five minutes a row, ten questions a session, two free batches. <see cref="Mode"/> and <see cref="FreeBatches"/>
/// are READ here so the file has no key the server does not know, and acted on by S3's gate.
/// </remarks>
public sealed record QuestionConsultSettings
{
    public bool Enabled { get; init; } = true;

    /// <summary><c>off</c>, <c>remind</c> or <c>require</c> — S3's gate reads it; S2 only keeps it.</summary>
    public string Mode { get; init; } = Modes.Require;

    /// <summary>The rows, at most <see cref="QuestionRows.MaxActive"/> of them on — see <see cref="QuestionRows.Parse"/>.</summary>
    public IReadOnlyList<QuestionRow> Rows { get; init; } = [];

    /// <summary>Whether <c>COAI_QCONSULT_ROWS</c> could not be read at all — the tool then refuses by name.</summary>
    public bool RowsUnreadable { get; init; }

    /// <summary>The shipped prompts with the person's own after them.</summary>
    public QuestionPromptSet Prompts { get; init; } = QuestionPromptSet.Shipped;

    /// <summary>The folders a <c>disk</c> row may read — validated (D14 c), absolute, existing.</summary>
    public IReadOnlyList<string> Roots { get; init; } = [];

    /// <summary>How long one row may run; past it the row is <c>timed_out</c> and the others still answer (A1).</summary>
    public TimeSpan RowBudget { get; init; } = TimeSpan.FromMinutes(DefaultRowMinutes);

    public int QuestionsPerSession { get; init; } = DefaultQuestionsPerSession;

    /// <summary>S3: how many question batches after the plan's <c>proceed</c> go to the person before the consultant is required (A7).</summary>
    public int FreeBatches { get; init; } = DefaultFreeBatches;

    public const int DefaultRowMinutes = 5;

    public const int DefaultQuestionsPerSession = 10;

    public const int DefaultFreeBatches = 2;

    public static QuestionConsultSettings Default { get; } = new();

    /// <summary>The enforcement words, lower case — the same three the cadence spells.</summary>
    public static class Modes
    {
        public const string Off = "off";
        public const string Remind = "remind";
        public const string Require = "require";

        public static IReadOnlyList<string> All { get; } = [Off, Remind, Require];
    }
}

/// <summary>What the eight keys turned out to be: the settings, and every sentence a person should read about them.</summary>
public sealed record QuestionConsultSetting(QuestionConsultSettings Settings, IReadOnlyList<UnrecognisedSetting> Complaints);

/// <summary>The keys, spelled once — a notice groups on the key, and a key recovered from a sentence stops matching when it is reworded.</summary>
public static class QuestionConsultKeys
{
    public const string Enabled = "COAI_QCONSULT_ENABLED";
    public const string Mode = "COAI_QCONSULT_MODE";
    public const string Rows = "COAI_QCONSULT_ROWS";
    public const string Prompts = "COAI_QCONSULT_PROMPTS";
    public const string Roots = "COAI_QCONSULT_ROOTS";
    public const string RowMinutes = "COAI_QCONSULT_ROW_MINUTES";
    public const string QuestionsPerSession = "COAI_QCONSULT_QUESTIONS_PER_SESSION";
    public const string FreeBatches = "COAI_QCONSULT_FREE_BATCHES";
}

/// <summary>
/// The places on THIS machine a disk root may not be (D14 c): the profile directory itself, and the system
/// directories. Injected, so a test can name temp folders as them.
/// </summary>
/// <param name="UserProfile">The person's own home — a root equal to it is the whole of their files.</param>
/// <param name="SystemDirectories">Windows, Program Files, ProgramData (and what the platform calls them) — a root in or under one.</param>
public sealed record SystemPlaces(string UserProfile, IReadOnlyList<string> SystemDirectories)
{
    public static SystemPlaces Current { get; } = new(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        [.. new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            }.Where(p => p.Length > 0)]);
}

/// <summary>Reads the eight keys and validates the roots. Pure over <paramref name="env"/>; the disk is asked only whether a root exists.</summary>
public static class QuestionConsultReader
{
    public static QuestionConsultSetting Read(Func<string, string?> env, string dataDir, SystemPlaces places, Func<string, bool>? isDirectory = null)
    {
        var rows = QuestionRows.Parse(env(QuestionConsultKeys.Rows));
        var prompts = QuestionPromptSet.ParseCustom(env(QuestionConsultKeys.Prompts));
        var roots = QuestionRoots.Validate(Listed(env(QuestionConsultKeys.Roots)), dataDir, places, isDirectory ?? Directory.Exists);
        var (mode, modeComplaint) = ModeOf(env(QuestionConsultKeys.Mode));

        var settings = new QuestionConsultSettings
        {
            Enabled = PanelSettings.NotSwitchedOff(env, QuestionConsultKeys.Enabled),
            Mode = mode,
            Rows = rows.Rows,
            RowsUnreadable = rows.Unreadable,
            Prompts = QuestionPromptSet.With(prompts.Prompts),
            Roots = roots.Accepted,
            RowBudget = TimeSpan.FromMinutes(PanelSettings.IntVar(env, QuestionConsultKeys.RowMinutes, QuestionConsultSettings.DefaultRowMinutes)),
            QuestionsPerSession = PanelSettings.IntVar(env, QuestionConsultKeys.QuestionsPerSession, QuestionConsultSettings.DefaultQuestionsPerSession),
            FreeBatches = PanelSettings.IntVar(env, QuestionConsultKeys.FreeBatches, QuestionConsultSettings.DefaultFreeBatches),
        };

        return new QuestionConsultSetting(settings,
        [
            .. rows.Complaints.Select(c => new UnrecognisedSetting(QuestionConsultKeys.Rows, c)),
            .. prompts.Complaints.Select(c => new UnrecognisedSetting(QuestionConsultKeys.Prompts, c)),
            .. roots.Refused.Select(c => new UnrecognisedSetting(QuestionConsultKeys.Roots, c)),
            .. modeComplaint,
        ]);
    }

    /// <summary>The roots as the file carries them: a JSON array of strings, or one path per line / semicolon for a hand-set variable.</summary>
    private static IReadOnlyList<string> Listed(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith('['))
        {
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize(trimmed, ServerJsonContext.Default.ListString)?
                    .Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).ToList() ?? [];
            }
            catch (System.Text.Json.JsonException)
            {
                return [trimmed]; // refused below, by name, as a path that is not one
            }
        }

        return [.. trimmed.Split([';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
    }

    private static (string Mode, IReadOnlyList<UnrecognisedSetting> Complaint) ModeOf(string? value)
    {
        var word = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (word.Length == 0 || QuestionConsultSettings.Modes.All.Contains(word, StringComparer.Ordinal))
        {
            return (word.Length == 0 ? QuestionConsultSettings.Modes.Require : word, []);
        }

        return (QuestionConsultSettings.Modes.Require,
        [
            new UnrecognisedSetting(QuestionConsultKeys.Mode,
                $"{QuestionConsultKeys.Mode} is '{value}', which this server does not know — the question consultant is required, as it is by default. The values are 'off', 'remind' and 'require'."),
        ]);
    }
}

/// <summary>
/// D14 (c): a disk root is refused when it is a drive root, the user profile directory itself, a system
/// directory (or inside one), inside the data directory, not absolute, or not an existing directory.
/// </summary>
/// <remarks>
/// The reviewer's scenario is concrete: a drive root is the whole disk, and a <c>--restricted</c> claude
/// with <c>--add-dir C:\</c> is a confined reviewer of everything. Refused by NAME, each with what to set
/// instead; what is accepted is the full path, so two spellings of one folder are one root.
/// </remarks>
public static class QuestionRoots
{
    public sealed record Verdict(IReadOnlyList<string> Accepted, IReadOnlyList<string> Refused);

    public static Verdict Validate(IEnumerable<string> roots, string dataDir, SystemPlaces places, Func<string, bool> isDirectory)
    {
        var accepted = new List<string>();
        var refused = new List<string>();
        foreach (var root in roots)
        {
            var why = WhyNot(root, dataDir, places, isDirectory);
            if (why.Length > 0)
            {
                refused.Add(why);
            }
            else if (!accepted.Contains(Full(root), Comparer))
            {
                accepted.Add(Full(root));
            }
        }

        return new Verdict(accepted, refused);
    }

    /// <summary>Why a root may not be read, or empty. The checks in the order a person would fix them.</summary>
    internal static string WhyNot(string root, string dataDir, SystemPlaces places, Func<string, bool> isDirectory)
    {
        if (!IsAbsolute(root))
        {
            return $"{QuestionConsultKeys.Roots}: '{root}' is not an absolute path — a disk root is spelled from a drive or from /";
        }

        var full = Full(root);

        return Place(full, dataDir, places) is { Length: > 0 } place ? place
            : !isDirectory(full) ? $"{QuestionConsultKeys.Roots}: '{root}' is not a directory on this machine — a disk row needs a folder that exists"
            : string.Empty;
    }

    private static string Place(string full, string dataDir, SystemPlaces places)
    {
        if (IsDriveRoot(full))
        {
            return $"{QuestionConsultKeys.Roots}: '{full}' is a drive root — a disk row would read the whole disk; name the project folders instead";
        }

        if (Same(full, places.UserProfile))
        {
            return $"{QuestionConsultKeys.Roots}: '{full}' is the user profile directory itself — every file of yours; name the project folders under it instead";
        }

        return Within(full, dataDir)
            ? $"{QuestionConsultKeys.Roots}: '{full}' is inside the data directory ({Full(dataDir)}) — the records, the ledger and the sessions are not another project to read"
            : places.SystemDirectories.FirstOrDefault(system => Same(full, system) || Within(full, system)) is { } found
                ? $"{QuestionConsultKeys.Roots}: '{full}' is a system directory ({Full(found)}) — not a project, and not a disk row's to read"
                : string.Empty;
    }

    /// <summary>Lexical, like the planner's: a <c>D:/</c> root is not rooted to <c>Path.IsPathRooted</c> on Linux.</summary>
    private static bool IsAbsolute(string root)
    {
        var text = root.Trim();

        return text.Length >= 3 && char.IsAsciiLetter(text[0]) && text[1] == ':' && text[2] is '/' or '\\'
            || text.Length >= 1 && text[0] is '/' or '\\';
    }

    private static bool IsDriveRoot(string full)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(full);

        return trimmed.Length == 0 || trimmed.Length == 2 && trimmed[1] == ':' || Path.GetPathRoot(full) is { } r && Same(r, full);
    }

    private static bool Within(string full, string parent)
    {
        var p = Path.TrimEndingDirectorySeparator(Full(parent));

        return p.Length > 0 && Path.TrimEndingDirectorySeparator(full).StartsWith(p + Path.DirectorySeparatorChar, Comparison)
            || Same(full, parent);
    }

    private static bool Same(string one, string other) =>
        other.Length > 0 && string.Equals(Path.TrimEndingDirectorySeparator(Full(one)), Path.TrimEndingDirectorySeparator(Full(other)), Comparison);

    private static string Full(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        }
        catch (Exception e) when (e is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return path.Trim();
        }
    }

    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer Comparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}

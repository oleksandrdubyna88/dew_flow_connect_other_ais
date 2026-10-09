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

    /// <summary>
    /// The roots spelled for the OTHER operating system — a WSL path read by a Windows server, a Windows path read by a
    /// Linux one — that are no directory on this machine, as written. Skipped here, never refused: the server on that side
    /// reads them (<see cref="QuestionRoots.OtherSideHere"/>). One that exists here is this side's, in <see cref="Roots"/>.
    /// </summary>
    public IReadOnlyList<string> OtherSideRoots { get; init; } = [];

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
    /// <summary>Whether this machine spells paths the Windows way — what decides which roots are the OTHER side's.</summary>
    public bool Windows { get; init; } = OperatingSystem.IsWindows();

    /// <summary>
    /// The drive a root-relative Windows root (<c>/work</c>, <c>\tools</c>) is qualified with — <c>%SystemDrive%</c>, or
    /// <c>C:</c> when it is unset. Never the CURRENT drive, which the extension host and this server need not share
    /// (<see cref="QuestionRoots.Qualified"/>).
    /// </summary>
    public string SystemDrive { get; init; } = SystemDriveOf(Environment.GetEnvironmentVariable("SystemDrive"));

    /// <summary>The environment's system drive, or <c>C:</c> when it says none.</summary>
    public static string SystemDriveOf(string? value) => string.IsNullOrWhiteSpace(value) ? "C:" : value.Trim();

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
            OtherSideRoots = roots.OtherSide,
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
/// directory (or inside one), inside the data directory, not absolute, or not an existing directory — and
/// (S4b item 2) when it CONTAINS the profile, the data directory or a system directory, when it is a
/// credential directory or inside one, or contains one, judged on the path as written AND on what its
/// junctions and symlinks resolve to. A root spelled for the OTHER operating system that is no directory here is
/// none of these: it is skipped before any check, the other side's folder (<see cref="OtherSideHere"/>, operator
/// 2026-10-09) — and one that IS a directory here (<c>/work</c> on Windows) is this side's, checked as any other.
/// </summary>
/// <remarks>
/// <para>The reviewer's scenario is concrete: a drive root is the whole disk, and a <c>--restricted</c> claude
/// with <c>--add-dir C:\</c> is a confined reviewer of everything. Refused by NAME, each with what to set
/// instead; what is accepted is the full path, so two spellings of one folder are one root.</para>
/// <para>A descendant check alone left the other direction open: <c>C:\Users</c> is no drive root and not the
/// profile, and reading it reads the profile, <c>AppData</c> and this product's own records. A link closes
/// nothing either way — <c>D:\work\home</c> junctioned to the profile IS the profile — so every component is
/// followed (<see cref="DocumentReader.Canonical"/>, the document reader's own walk), and so are the places'.</para>
/// </remarks>
public static class QuestionRoots
{
    /// <param name="OtherSide">
    /// The roots spelled for the other operating system that are no directory here, trimmed and as written — skipped,
    /// never judged against this machine's places, and never refused (<see cref="OtherSideHere"/>).
    /// </param>
    public sealed record Verdict(IReadOnlyList<string> Accepted, IReadOnlyList<string> Refused, IReadOnlyList<string> OtherSide);

    /// <param name="followLink">
    /// Resolves ONE path entry's link (<see cref="DocumentReader.FollowLink"/> by default): every component of a root,
    /// and of each refused place, is walked through it (<see cref="DocumentReader.Canonical"/>), so a junction or a
    /// symlink is judged at its final target (S4b item 2).
    /// </param>
    public static Verdict Validate(
        IEnumerable<string> roots, string dataDir, SystemPlaces places, Func<string, bool> isDirectory, Func<string, string>? followLink = null)
    {
        // Every root is looked for, judged and kept QUALIFIED: a root-relative Windows root on the system drive, never on
        // whatever drive this process stands on (the code round, 2026-10-09). The other side's list keeps the spelling.
        string Here(string root) => Qualified(root, places.Windows, places.SystemDrive);
        bool TheOtherSides(string root) => OtherSideHere(root, places.Windows, OtherSide(root, places.Windows) && isDirectory(Full(Here(root))));
        // Decided ONCE per root: the decision may probe the disk, and its answer serves both lists (the code round).
        var decided = roots.Select(root => (Root: root, OtherSide: TheOtherSides(root))).ToList();
        var otherSide = decided.Where(one => one.OtherSide).Select(one => one.Root.Trim()).Distinct(StringComparer.Ordinal).ToList();
        var seen = Seen.Of(dataDir, places, followLink ?? DocumentReader.FollowLink);
        var accepted = new List<string>();
        var refused = new List<string>();
        foreach (var root in decided.Where(one => !one.OtherSide).Select(one => Here(one.Root)))
        {
            var why = WhyNot(root, seen, isDirectory);
            if (why.Length > 0)
            {
                refused.Add(why);
            }
            else if (!accepted.Contains(Full(root), Comparer))
            {
                accepted.Add(Full(root));
            }
        }

        return new Verdict(accepted, refused, otherSide);
    }

    /// <summary>
    /// Whether a root is spelled for the OTHER operating system — a POSIX path (<c>/home/…</c>, <c>/mnt/c/…</c>) on a
    /// Windows server, a Windows one (<c>C:\…</c>, <c>d:/…</c>, <c>\\server\share</c>) on Linux, WSL or macOS.
    /// </summary>
    /// <remarks>
    /// <para><b>Why it is skipped rather than refused</b> (operator, 2026-10-09): VS Code's user settings are shared by a
    /// WSL window and a plain Windows window on one machine, and each side runs its own server — so a root written from
    /// one side reaches the other. It is not missing; it is the other side's folder, and the server THERE reads it. Judged
    /// here it became "not a directory on this machine" and a failure notice on every start of this side.</para>
    /// <para>Lexical, so a test decides both directions on any machine. <c>//server/share</c> is neither side's alone — a UNC
    /// share on Windows, a path from <c>/</c> on POSIX — and a relative or drive-relative root is this side's, refused by
    /// name as before. <c>shared/path-family-vectors.json</c> is answered by this and by the extension's
    /// <c>spelledForTheOtherOs</c>. This is the SPELLING only: whether a root is skipped also asks whether it exists
    /// here (<see cref="OtherSideHere"/>), because <c>/work</c> is a real folder on a Windows drive.</para>
    /// </remarks>
    public static bool OtherSide(string root, bool windows)
    {
        var text = root.Trim();

        return windows ? IsPosixAbsolute(text) : IsWindowsAbsolute(text);
    }

    /// <summary>
    /// The DECISION: a root is the other side's when it is spelled for the other operating system AND is no directory on
    /// this machine. The spelling alone is not enough (the plan round, 2026-10-09): on Windows <c>/work</c> is a legal
    /// root-relative path to the folder <c>work</c> (looked for on the system drive, <see cref="Qualified"/>), and a person who typed an existing folder that
    /// way must not lose it — an existing one is this side's and goes through the ordinary checks. Answered by
    /// <c>shared/path-family-vectors.json</c>'s <c>existence</c> vectors, as the extension's <c>otherSideHere</c> is.
    /// </summary>
    public static bool OtherSideHere(string root, bool windows, bool existsHere) => !existsHere && OtherSide(root, windows);

    /// <summary>
    /// The root as this side looks for it and keeps it: on Windows a root-relative root — one leading <c>/</c>, or a
    /// <c>\</c> not followed by another — is qualified with the SYSTEM drive (<c>/work</c> on <c>D:</c> → <c>D:\work</c>);
    /// everything else, and every root elsewhere, is the root trimmed.
    /// </summary>
    /// <remarks>
    /// The code round, 2026-10-09: resolved against the CURRENT drive, the extension host and this server — which need
    /// not stand on the same drive — could decide differently, and the server could check one drive and read another.
    /// One explicit base on both sides, for the existence decision and for the root kept, so every later use (the grant,
    /// <c>--add-dir</c>, the prompt) names its drive. Answered by <c>shared/path-family-vectors.json</c>'s
    /// <c>resolution</c> vectors, as the extension's <c>qualified</c> is.
    /// </remarks>
    public static string Qualified(string root, bool windows, string systemDrive)
    {
        var text = root.Trim();

        return windows && IsRootRelative(text) ? systemDrive + text.Replace('/', '\\') : text;
    }

    private static bool IsRootRelative(string text) =>
        text.Length > 0 && text[0] is '/' or '\\' && !(text.Length > 1 && text[1] is '/' or '\\');

    /// <summary>What a server says about a root it skipped — the Information line its log carries (<c>StartupNotices</c>).</summary>
    public static string SkippedSentence(string root, bool windows) => windows
        ? $"{QuestionConsultKeys.Roots}: '{root}' is a Linux, WSL or macOS path and this server runs on Windows — it is the other side's folder, read by the server there; this side skips it"
        : $"{QuestionConsultKeys.Roots}: '{root}' is a Windows path and this server does not run on Windows — it is the Windows side's folder, read by the server there; this side skips it";

    /// <summary>Why a disk row is not asked on this side when every root it could read is the other side's.</summary>
    public static string InactiveSentence(IReadOnlyList<string> otherSide) =>
        $"inactive on this side — every folder a disk row may read ({string.Join(", ", otherSide.Select(root => $"'{root}'"))}) is spelled for "
        + "the other operating system, so the server there reads it and this one has none; add a folder of this machine in "
        + "ConnectOtherAIs > Question consultant to ask this row here too";

    private static bool IsPosixAbsolute(string text) => text.StartsWith('/') && !text.StartsWith("//", StringComparison.Ordinal);

    private static bool IsWindowsAbsolute(string text) =>
        text.Length >= 3 && char.IsAsciiLetter(text[0]) && text[1] == ':' && text[2] is '/' or '\\'
        || text.StartsWith('\\');

    /// <summary>The folders a root may be under but never ABOVE, as their credentials live in them: a vendor's sign-in, a machine's keys.</summary>
    /// <remarks>Matched as path segments wherever they are (a <c>.ssh</c> copied to a backup is still keys), and as directories under the profile.</remarks>
    public static IReadOnlyList<string> CredentialDirectories { get; } = [".ssh", ".aws", ".gnupg", ".config/gcloud", ".claude", ".codex", ".azure"];

    /// <summary>Why a root may not be read, or empty. The checks in the order a person would fix them — on the path as written AND on what it resolves to.</summary>
    private static string WhyNot(string root, Seen seen, Func<string, bool> isDirectory)
    {
        if (!IsAbsolute(root))
        {
            return $"{QuestionConsultKeys.Roots}: '{root}' is not an absolute path — a disk root is spelled from a drive or from /";
        }

        var full = Full(root);
        var real = seen.Resolved(full);

        return Place(full, seen) is { Length: > 0 } place ? place
            : Place(real, seen) is { Length: > 0 } target ? $"{target} ('{full}' resolves to it)"
            : !isDirectory(full) ? $"{QuestionConsultKeys.Roots}: '{root}' is not a directory on this machine — a disk row needs a folder that exists"
            : string.Empty;
    }

    /// <summary>The first place this path may not be — each check its own sentence, or empty.</summary>
    private static string Place(string full, Seen seen) =>
        new Func<string, Seen, string>[] { DriveRoot, Profile, DataDirectory, SystemDirectory, Credentials }
            .Select(check => check(full, seen))
            .FirstOrDefault(sentence => sentence.Length > 0) ?? string.Empty;

    private static string DriveRoot(string full, Seen _) => IsDriveRoot(full)
        ? $"{QuestionConsultKeys.Roots}: '{full}' is a drive root — a disk row would read the whole disk; name the project folders instead"
        : string.Empty;

    private static string Profile(string full, Seen seen) =>
        seen.Profile.Any(profile => Same(full, profile))
            ? $"{QuestionConsultKeys.Roots}: '{full}' is the user profile directory itself — every file of yours; name the project folders under it instead"
            : seen.Profile.FirstOrDefault(profile => Above(full, profile)) is { } held
                ? $"{QuestionConsultKeys.Roots}: '{full}' contains the user profile directory ({held}) — every file of yours; name the project folders instead"
                : string.Empty;

    private static string DataDirectory(string full, Seen seen) =>
        seen.Data.FirstOrDefault(data => Within(full, data)) is { } inside
            ? $"{QuestionConsultKeys.Roots}: '{full}' is inside the data directory ({inside}) — the records, the ledger and the sessions are not another project to read"
            : seen.Data.FirstOrDefault(data => Above(full, data)) is { } held
                ? $"{QuestionConsultKeys.Roots}: '{full}' contains the data directory ({held}) — the records, the ledger and the sessions are not another project to read"
                : string.Empty;

    private static string SystemDirectory(string full, Seen seen) =>
        seen.Systems.FirstOrDefault(system => Within(full, system)) is { } found
            ? $"{QuestionConsultKeys.Roots}: '{full}' is a system directory ({found}) — not a project, and not a disk row's to read"
            : seen.Systems.FirstOrDefault(system => Above(full, system)) is { } held
                ? $"{QuestionConsultKeys.Roots}: '{full}' contains a system directory ({held}) — not a project, and not a disk row's to read"
                : string.Empty;

    private static string Credentials(string full, Seen seen) =>
        CredentialSegment(full) is { Length: > 0 } named
            ? $"{QuestionConsultKeys.Roots}: '{full}' is a credential directory ({named}) or inside one — keys and vendor sign-ins are not a project to read"
            : seen.Credentials.FirstOrDefault(credentials => Above(full, credentials)) is { } held
                ? $"{QuestionConsultKeys.Roots}: '{full}' contains a credential directory ({held}) — keys and vendor sign-ins are not a project to read"
                : string.Empty;

    /// <summary>The credential directory a path is, or is inside, by its segments — <c>.ssh</c> anywhere, <c>.config/gcloud</c> as a pair.</summary>
    private static string CredentialSegment(string full)
    {
        var segments = full.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var joined = "/" + string.Join('/', segments) + "/";

        return CredentialDirectories.FirstOrDefault(credentials => joined.Contains("/" + credentials + "/", Comparison)) ?? string.Empty;
    }

    /// <summary>
    /// The places a root is compared against, each in every spelling that names it: as written and as its links resolve —
    /// a data directory reached through a junction is the same directory, and a root resolving into it is refused.
    /// </summary>
    private sealed record Seen(
        IReadOnlyList<string> Profile,
        IReadOnlyList<string> Data,
        IReadOnlyList<string> Systems,
        IReadOnlyList<string> Credentials,
        Func<string, string> FollowLink)
    {
        public static Seen Of(string dataDir, SystemPlaces places, Func<string, string> followLink)
        {
            IReadOnlyList<string> Spellings(string place) => place.Trim().Length == 0 ? [] : [.. new[] { Full(place), Real(place, followLink) }.Distinct(Comparer)];
            var profile = Spellings(places.UserProfile);

            return new Seen(
                profile,
                Spellings(dataDir),
                [.. places.SystemDirectories.SelectMany(Spellings)],
                [.. profile.SelectMany(home => CredentialDirectories.Select(credentials => Full(Path.Combine(home, credentials))))],
                followLink);
        }

        /// <summary>What a root resolves to with every component's link followed.</summary>
        public string Resolved(string full) => Real(full, FollowLink);
    }

    private static string Real(string path, Func<string, string> followLink)
    {
        try
        {
            return Full(DocumentReader.Canonical(Full(path), Full(path), followLink));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A link that cannot be followed is judged as written; the check on the written path still stands.
            return Full(path);
        }
    }

    /// <summary>Whether <paramref name="full"/> is an ANCESTOR of <paramref name="place"/> — reading it reads the place too.</summary>
    private static bool Above(string full, string place) => !Same(full, place) && Within(place, full);

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

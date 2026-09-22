using CoaiMcp.Core.Notices;

namespace CoaiMcp.Server;

/// <summary>
/// What a run says about ITSELF at startup, written where a person looks rather than only logged.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Three things were said at startup and said only to Serilog: a
/// legacy settings file this side adopted, every setting whose value this build could not use, and
/// what it found on the disk. The plan records what that silence cost once already — *"a
/// configuration that had been applied, read and reloaded correctly looked broken for twenty
/// minutes, and the one thing that would have ended it in a second was this line"* — a line only in
/// the log, which the panel does not read.</para>
///
/// <para><b>The log lines STAY.</b> An operator reading a terminal and a person reading the panel
/// are different people, which is the same decision <c>PanelService.Refused</c> made when it kept
/// its warning beside the refusal it returns.</para>
///
/// <para><b>Every offer goes through <see cref="Noticing.Offered"/></b> — story 2.3.2's boundary,
/// which cannot throw and reports a loss — so "nothing here can fail startup" is inherited rather
/// than rebuilt, and a second copy of that guarantee is a second one to keep true.</para>
///
/// <para><b>The classes are the extension's taxonomy</b> (<c>notifications.ts</c>): a setting this
/// build fell back on, and a database it is NOT using, are <c>stand-down</c> — the server stood down
/// from something somebody configured. A new directory and an adopted settings file are
/// <c>outcome</c>: they happened, and nothing was refused.</para>
/// </remarks>
internal static class StartupNotices
{
    private const string Source = "coai-mcp";

    private const string StoodDown = "stand-down";

    private const string Outcome = "outcome";

    /// <summary>A legacy settings file in the shared root that this side has taken over.</summary>
    /// <remarks>
    /// The subject is this side's own settings path, canonicalised — the resource the sentence is
    /// about. Canonical so the same file reached as <c>C:\data</c> and <c>C:/data</c> is one row
    /// with a count rather than two rows. (The plan round.)
    /// </remarks>
    internal static void Adopted(
        string sentence, string settingsPath, Noticing noticing, Serilog.ILogger log)
    {
        log.Warning("data directory: {Note}", sentence);
        noticing.Offered(() => Note(
            ServerNoticeCodes.SettingsAdopted, Outcome, Canonical(settingsPath), sentence));
    }

    /// <summary>Every unrecognised setting and every storage note this run has, written down.</summary>
    internal static void Record(
        PanelSettings settings, IReadOnlyList<StorageNote> storage, Noticing noticing,
        Serilog.ILogger log)
    {
        Unrecognised(settings, noticing, log);
        Storage(storage, noticing, log);
    }

    /// <summary>
    /// Every setting this build could not use — the half a settings RELOAD can produce.
    /// </summary>
    /// <remarks>
    /// Its own method because <see cref="PanelServiceHost.Build"/> calls it and must not call the
    /// other one: the storage notes come from a survey of the DISK taken once at startup, and a
    /// rebuild that re-ran it would stat a configured NAS on a settings change (issue #115). What a
    /// reload can newly produce is a value somebody just typed. (The code round, five findings.)
    /// </remarks>
    internal static void Unrecognised(PanelSettings settings, Noticing noticing, Serilog.ILogger log)
    {
        foreach (var setting in settings.UnrecognisedSettings)
        {
            log.Warning("{Mismatch}", setting.Sentence);
            noticing.Offered(() => Note(
                ServerNoticeCodes.UnrecognisedSetting, StoodDown, setting.Key, setting.Sentence));
        }
    }

    private static void Storage(
        IReadOnlyList<StorageNote> storage, Noticing noticing, Serilog.ILogger log)
    {
        foreach (var note in storage)
        {
            log.Warning("data directory: {Note}", note.Sentence);
            noticing.Offered(() => Note(ServerNoticeCodes.StorageNote, ClassOf(note), note.Subject, note.Sentence));
        }
    }

    /// <summary>
    /// Which class each storage KIND is — a map rather than a condition, so a kind nobody has
    /// decided about cannot pass as an outcome.
    /// </summary>
    /// <remarks>
    /// The first draft was <c>note.Kind == LooseDatabase ? StoodDown : Outcome</c>, and codex named
    /// what that costs: a third kind — a permission refusal, say — would reach the extension as a
    /// SUCCESSFUL outcome, with nothing failing anywhere to say so. The same answer story 2.3.2
    /// gave for reviewer endings: the mapping is data, an unmapped key throws, and a census test
    /// over the kinds this type declares is what makes the throw unreachable.
    /// </remarks>
    internal static IReadOnlyDictionary<string, string> ClassByKind { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [StorageNote.LooseDatabase] = StoodDown,
            [StorageNote.NewDirectory] = Outcome,
        };

    private static string ClassOf(StorageNote note) =>
        ClassByKind.TryGetValue(note.Kind, out var said)
            ? said
            : throw new ArgumentException(
                $"'{note.Kind}' is a storage kind nothing has decided a class for — put it in "
                + $"{nameof(ClassByKind)} rather than letting it reach the page as an outcome",
                nameof(note));

    private static ServerNotice Note(string code, string kind, string subject, string sentence) => new()
    {
        Utc = ServerNotice.Iso(DateTimeOffset.UtcNow),
        Class = kind,
        Source = Source,
        Code = code,
        // The SUBJECT is cut as well as the title, and was not: it is a path, and a path has no
        // length anybody controls. Both go through the one helper every producer shares —
        // `ServerNotice.Shortened` says why the cut is here rather than only at the line.
        // (gemini, on both code rounds.)
        Subject = ServerNotice.Shortened(subject),
        Title = ServerNotice.Shortened(sentence),
        // These sentences put an unbounded VALUE at the front and the instruction at the back, so a
        // cut takes the actionable half. The detail keeps it — four times the room — while the
        // title carries the mark saying there was more. (codex, on the code round.)
        Detail = Cut(sentence) ? sentence : "",
    };

    /// <summary>Whether a sentence is longer than a title is allowed to be.</summary>
    private static bool Cut(string text) => text.Length > Redaction.TitleLimit;

    /// <summary>
    /// One spelling per path, so the same file is one row rather than one per spelling.
    /// </summary>
    /// <remarks>
    /// Never an exception: a path this process cannot canonicalise is still a path worth naming, and
    /// losing the notice over the spelling of its subject would be the wrong trade.
    /// </remarks>
    private static string Canonical(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception failure) when (failure is ArgumentException or NotSupportedException
                                            or PathTooLongException or IOException
                                            or UnauthorizedAccessException)
        {
            return path;
        }
    }
}

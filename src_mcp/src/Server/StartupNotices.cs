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
    internal static void Adopted(string sentence, string settingsPath, Noticing noticing) =>
        noticing.Offered(() => Note(
            ServerNoticeCodes.SettingsAdopted, Outcome, Canonical(settingsPath), sentence));

    /// <summary>Every unrecognised setting and every storage note this run has, written down.</summary>
    internal static void Record(
        PanelSettings settings, IReadOnlyList<StorageNote> storage, Noticing noticing)
    {
        foreach (var setting in settings.UnrecognisedSettings)
        {
            noticing.Offered(() => Note(
                ServerNoticeCodes.UnrecognisedSetting, StoodDown, setting.Key, setting.Sentence));
        }

        foreach (var note in storage)
        {
            noticing.Offered(() => Note(ServerNoticeCodes.StorageNote, ClassOf(note), note.Subject, note.Sentence));
        }
    }

    /// <summary>
    /// A database this side is NOT using is a stand-down; a directory being created is an outcome.
    /// </summary>
    private static string ClassOf(StorageNote note) =>
        note.Kind == StorageNote.LooseDatabase ? StoodDown : Outcome;

    private static ServerNotice Note(string code, string kind, string subject, string sentence) => new()
    {
        Utc = ServerNotice.Iso(DateTimeOffset.UtcNow),
        Class = kind,
        Source = Source,
        Code = code,
        Subject = subject,
        // Cut where the record is BUILT, not only where the line is written: a notice waits in the
        // writer's queue until then. (Story 2.2's memory rule, applied to this road.)
        Title = Shorter(sentence),
    };

    private static string Shorter(string sentence) =>
        sentence.Length <= Redaction.TitleLimit ? sentence : sentence[..Redaction.TitleLimit];

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

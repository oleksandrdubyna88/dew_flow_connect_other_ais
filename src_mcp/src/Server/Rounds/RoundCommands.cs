using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server;

/// <summary>
/// The orders a round carries back to its caller — the operator's switches, the words a person
/// wrote for them and their own commands, each read for THIS call as the settings are.
/// </summary>
internal sealed class RoundCommands(PanelSettings settings, RolePrompts prompts, Serilog.ILogger log)
{
    private readonly PanelSettings _settings = settings;
    private readonly RolePrompts _prompts = prompts;
    private readonly Serilog.ILogger _log = log;

    /// <summary>Whether the caller may go and build: an order to split follows permission.</summary>
    internal static bool MayProceed(RoundVerdict verdict) =>
        verdict is RoundVerdict.Proceed or RoundVerdict.GoodEnough or RoundVerdict.ContinueAnyway;

    /// <summary>
    /// Whom the split order is remembered against.
    /// </summary>
    /// <remarks>
    /// <para>The calling AI's own session when it exports one — Claude Code does, to every child it
    /// spawns, so nothing has to be passed or remembered by the model.</para>
    /// <para>A client that identifies itself in no way at all falls back to the CHECKOUT, not to our
    /// session. Our session is repo+branch, and an epic arrives on its own branch: a session-keyed
    /// fallback would call every epic a fresh caller and re-order the split on each of them, which
    /// is the exact loop this exists to stop — raised as Blocking by gemini in this change's plan
    /// round. The checkout follows a caller across its branches, which is where the epics are. The
    /// price is stated rather than hidden: an anonymous client starting a SECOND, unrelated task in
    /// the same checkout within a day is told it is a piece. That is the cheaper error, and the
    /// piece's own order tells it to say so if it disagrees.</para>
    /// </remarks>
    internal static string CallerFor(PersistedSession session) =>
        CallerIdentity.Current().Id is { Length: > 0 } id ? id : $"repo:{session.State.RepoPath}";

    /// <summary>
    /// What a person wrote for each order part and each custom command, from <c>&lt;dataDir&gt;/prompts/</c>
    /// — through <see cref="RolePrompts"/>, so the same id guard and the same "empty is no override" rule.
    /// </summary>
    /// <remarks>
    /// <b>Best effort, text by text.</b> This runs AFTER the reviewers have answered and been paid for: a
    /// file another process holds at that moment — the extension restoring a default, a scanner, a share
    /// blinking — used to fail the finished round, so its findings were never saved and the caller paid
    /// for it again. A text that cannot be read is the shipped one this round (a custom command's, which
    /// ships none, is named as having no text), and the log says which. (our own reviewer, the code round.)
    /// </remarks>
    internal Core.Commands.CommandTexts CommandTextsNow() => new(
        Core.Commands.CommandTexts.ShippedIds
            .Concat(_settings.CustomCommands.Select(command => command.Id))
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(id => id, WrittenOrNothing, StringComparer.Ordinal));

    private string WrittenOrNothing(string id)
    {
        try
        {
            return _prompts.Written(id);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Warning(e, "the text {Id} could not be read; this round uses the shipped words", id);
            return string.Empty;
        }
    }

    /// <summary>The person's commands for a round of this stage; one with no text is named and logged, not sent.</summary>
    internal Core.Commands.CustomOrders CustomOrdersFor(Stage stage, Core.Commands.CommandTexts texts)
    {
        var custom = Core.Commands.CustomCommands.For(_settings.CustomCommands, CommandStageOf(stage), texts, _prompts.FileToWrite);
        foreach (var skipped in custom.Skipped)
        {
            _log.Warning("{Skipped}", skipped);
        }

        return custom;
    }

    /// <summary>
    /// Which of a person's commands a round of this stage is given — the stage's own row, so a
    /// document round (neither a plan nor a code round) gets only an <c>any</c> command, and a stage
    /// added later cannot inherit that by omission (§9.4 of the feature-review plan).
    /// </summary>
    internal static Core.Commands.CommandStage CommandStageOf(Stage stage) => Stages.Of(stage).Commands;

    /// <summary>Which models a split order named, as the log line says it (issue #117).</summary>
    internal static string ModelsInLog(Core.Commands.CommandContext context) =>
        context.SplitWithFable
            ? $"strongest model {NamedInLog(context.Models.Strongest)}, implementation {NamedInLog(context.Models.Implementation)}"
            : "no model order";

    private static string NamedInLog(string name) => name.Length > 0 ? name : "(generic words)";
}

namespace CoaiMcp.Core.Commands;

/// <summary>Which rounds a custom command is given in.</summary>
public enum CommandStage
{
    /// <summary>Every round — plan, code and document.</summary>
    Any,

    /// <summary>Plan rounds only.</summary>
    Plan,

    /// <summary>Code rounds only.</summary>
    Code,
}

/// <summary>A command a person added (issue #467): its text is the file <c>&lt;dataDir&gt;/prompts/&lt;Id&gt;.md</c>.</summary>
/// <param name="Id">Always <c>command-</c> plus the slug the person chose — the parser adds the prefix, so
/// the file sits beside the shipped command texts and can never shadow a role prompt.</param>
public sealed record CustomCommand(string Id, string Title, bool Enabled, CommandStage Stage);

/// <summary>What the custom commands add to one round: the orders, and the ones left out with why.</summary>
public sealed record CustomOrders(IReadOnlyList<string> Orders, IReadOnlyList<string> Skipped)
{
    public static readonly CustomOrders None = new([], []);
}

/// <summary>
/// The commands a person added, for one round — after the built-in orders, in the order they are listed.
/// </summary>
public static class CustomCommands
{
    /// <param name="commands">As configured, enabled or not.</param>
    /// <param name="round">The round being answered: <see cref="CommandStage.Plan"/>,
    /// <see cref="CommandStage.Code"/>, or <see cref="CommandStage.Any"/> for a round that is neither (a
    /// document) — which only an <c>Any</c> command is given in.</param>
    /// <param name="texts">The overrides, which is where a custom command's text lives.</param>
    /// <param name="fileOf">Where a command's text belongs, named when it has none.</param>
    public static CustomOrders For(
        IReadOnlyList<CustomCommand> commands, CommandStage round, CommandTexts texts, Func<string, string> fileOf)
    {
        // One read of each text, kept beside its command, so the two lists cannot disagree about it.
        var given = commands
            .Where(command => command.Enabled && GivenIn(command.Stage, round))
            .Select(command => (Command: command, Text: texts.Written(command.Id)))
            .ToList();

        return new CustomOrders(
            [.. given.Where(one => one.Text.Length > 0).Select(one => one.Text)],
            [.. given.Where(one => one.Text.Length == 0).Select(one => Unwritten(one.Command, fileOf))]);
    }

    private static bool GivenIn(CommandStage stage, CommandStage round) => stage == CommandStage.Any || stage == round;

    /// <summary>The sentence for a command with no text — left out, because an empty order tells the caller nothing.</summary>
    private static string Unwritten(CustomCommand command, Func<string, string> fileOf) =>
        $"custom command '{command.Title}' has no text — write it in {fileOf(command.Id)}";
}

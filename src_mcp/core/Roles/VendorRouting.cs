using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Roles;

/// <summary>
/// The vendor-routing rule (<c>.agents/rules/common/vendor-routing.md</c>) as a check on a roles entry: a Claude model runs only through the <c>claude</c> CLI, a GPT model through <c>codex</c> (never <c>agy</c>);
/// Gemini and GPT-OSS go through <c>agy</c>. A wrong route is invisible in the output and is drawn from a metered quota, so an entry that breaks it is refused, not passed on.
/// </summary>
/// <remarks>
/// <para>A model name this rule does not know is NOT refused: the table may name a model the repository has never seen, and a guess would turn a new name into a wrong refusal. Only a family the rule names
/// is judged — Claude (<c>opus</c>, <c>sonnet</c>, <c>haiku</c>, <c>fable</c>, <c>claude-…</c>) and GPT (<c>gpt-…</c> but NOT <c>gpt-oss-…</c>, <c>codex-…</c>, <c>terra</c>, <c>sol</c>, <c>luna</c>).</para>
/// </remarks>
internal static partial class VendorRouting
{
    /// <summary>The empty string when <paramref name="model"/> may run on <paramref name="vendor"/>, else the sentence that says why not.</summary>
    internal static string Refusal(string model, string vendor)
    {
        var cli = vendor.Trim().ToLowerInvariant();
        var name = model.Trim().ToLowerInvariant();
        if (IsClaude(name) && cli is "agy" or "codex")
        {
            return $"'{model}' is a Claude model and runs only through the claude CLI, not '{vendor}'";
        }

        return IsGpt(name) && cli == "agy"
            ? $"'{model}' is a GPT model and runs through the codex CLI, not '{vendor}'"
            : string.Empty;
    }

    /// <summary>
    /// The CLI the rule sends a model to — <c>claude</c>, <c>codex</c> or <c>agy</c> — or the empty string for a name the rule does not know (the router then refuses an explicit model it cannot place, and never guesses).
    /// </summary>
    internal static string CliFor(string model)
    {
        var name = model.Trim().ToLowerInvariant();

        return IsClaude(name) ? "claude" : IsGpt(name) ? "codex" : OnAgy().IsMatch(name) ? "agy" : string.Empty;
    }

    private static bool IsClaude(string name) => ClaudeName().IsMatch(name);

    private static bool IsGpt(string name) => GptName().IsMatch(name);

    [GeneratedRegex(@"^(gemini|gpt-?oss)(-|\.|\d|$)", RegexOptions.CultureInvariant)]
    private static partial Regex OnAgy();

    [GeneratedRegex(@"^(claude(-|$)|(opus|sonnet|haiku|fable)(-|\.|\d|$))", RegexOptions.CultureInvariant)]
    private static partial Regex ClaudeName();

    [GeneratedRegex(@"^((gpt(?!-?oss)(-|\d|$))|codex-|(terra|sol|luna)(-|$))", RegexOptions.CultureInvariant)]
    private static partial Regex GptName();
}

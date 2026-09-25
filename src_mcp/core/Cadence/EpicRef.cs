using System.Globalization;
using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Cadence;

/// <summary>
/// Where the caller says it is in a plan: epic <c>k</c> of a plan whose last epic is <c>N</c>, or nowhere.
/// </summary>
/// <remarks>
/// <para><c>todo/PLAN_consult_on_a_cadence.md</c>, story 1.1. The server does not COUNT rounds to know
/// which epic it is looking at: an <c>again: true</c> code round is as often a checkpoint or a retry as a
/// new epic. The caller declares it, the way it declares its model on <c>open</c>.</para>
/// <para><b><c>k</c> is the epic's own number as the plan writes it, <c>N</c> the plan's last.</b> A plan
/// may continue another — email-service's second plan numbers its epics 5 to 14 — so <c>N</c> is not a
/// count, and the ceiling of fourteen is applied to the count (<see cref="CadenceRule.RefuseIfTooMany"/>),
/// never to this number. Whether <c>k</c> is one of the plan's OWN numbers needs the plan text and is
/// the server's check, made where it reads the plan.</para>
/// </remarks>
public abstract partial record EpicRef
{
    /// <summary>No plan and no epic: work that was never split owes no cadence.</summary>
    public sealed record None : EpicRef;

    /// <summary>A declared position, well formed.</summary>
    /// <param name="Number">The epic's number as the plan writes it.</param>
    /// <param name="Last">The plan's last epic number.</param>
    /// <param name="Plan">The plan file, repo-relative, as the caller named it.</param>
    public sealed record Some(int Number, int Last, string Plan) : EpicRef;

    /// <summary>A declaration the gate cannot use, and the sentence that says how to fix it.</summary>
    public sealed record Refused(string Sentence) : EpicRef;

    private EpicRef() { }

    private const int MatchTimeoutMs = 1000;

    /// <summary>Reads the two arguments a <c>review_plan</c> or <c>review_code</c> call carries.</summary>
    public static EpicRef Parse(string epic, string plan)
    {
        var declared = (epic ?? string.Empty).Trim();
        var file = (plan ?? string.Empty).Trim();
        if (declared.Length == 0 && file.Length == 0)
        {
            return new None();
        }

        return declared.Length == 0 || file.Length == 0
            ? new Refused(OneWithoutTheOther(declared.Length == 0 ? "plan" : "epic"))
            : Numbers(declared, file);
    }

    /// <summary>
    /// The identity a plan's cadence is kept under: its FILE NAME, lower-cased.
    /// </summary>
    /// <remarks>
    /// Not its path. A finished plan moves from <c>todo/</c> to <c>research/</c>, and an unfinished tail
    /// may still be gated after it (epic 1's plan round, gemini). Lower-cased because a Windows path is
    /// case-blind and a caller will type it either way.
    /// </remarks>
    public static string PlanKey(string plan)
    {
        var normalised = (plan ?? string.Empty).Trim().Replace('\\', '/');
        var slash = normalised.LastIndexOf('/');

        return normalised[(slash + 1)..].ToLowerInvariant();
    }

    private static EpicRef Numbers(string declared, string plan)
    {
        var match = KOfN().Match(declared);
        if (!match.Success)
        {
            return new Refused(
                $"epic must be 'k/N' — this epic's number as the plan writes it, and the plan's last epic number, "
                + $"e.g. '5/14'. '{declared}' does not read that way.");
        }
        var number = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var last = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);

        return number >= 1 && number <= last
            ? new Some(number, last, plan)
            : new Refused($"epic '{declared}' is not inside its own plan: its number must be between 1 and {last}.");
    }

    private static string OneWithoutTheOther(string missing) =>
        $"plan and epic go together — the plan file, repo-relative (e.g. 'todo/PLAN_x.md'), and where you are in "
        + $"it (e.g. '5/14'). This call sent no {missing}; send both, or neither for work that was never split.";

    // [0-9], not \d: .NET's \d is every Unicode decimal digit, and "٥/١٤" passed the pattern only to throw
    // inside int.Parse — an exception where a refusal was owed (epic 1's code round).
    [GeneratedRegex(@"^([0-9]{1,3})\s*/\s*([0-9]{1,3})$", RegexOptions.CultureInvariant, MatchTimeoutMs)]
    private static partial Regex KOfN();
}

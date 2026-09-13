using CoaiMcp.Core.Consultation;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CoaiMcp;

/// <summary>
/// The one MCP prompt: the PERSON asking for a consultation, in their own words.
/// </summary>
/// <remarks>
/// <para>Four of the five triggers in <c>PLAN_consultant.md</c> are the assistant's own to notice —
/// the same test red after two fix attempts, two sources contradicting, an unmeasured design fork,
/// the person saying "not fixed" twice. The fifth is the person simply SAYING so, and a sentence in
/// a chat is a weak carrier for it: it competes with everything else in the conversation, and it
/// says nothing about which repository or what the tool is called.</para>
/// <para>A prompt is the protocol's own answer to that. The client lists it —
/// <c>/mcp__coai__consult</c> in Claude Code — and what comes back is a message addressed to the
/// assistant, in the imperative, naming the tool and the two rules that make advice usable. It calls
/// nothing itself: a prompt is text, the assistant decides, and the caps and the invariant still
/// apply to whatever it decides.</para>
/// <para><b>The problem is OPTIONAL.</b> A person who types the command alone is not being lazy —
/// they are saying "you know what we are stuck on", which the assistant does and they should not
/// have to restate. When they do give words, those words are the problem statement and the
/// assistant is told not to improve them.</para>
/// </remarks>
internal static class Prompts
{
    internal const string ConsultName = "consult";

    internal static IEnumerable<McpServerPrompt> All()
    {
        yield return McpServerPrompt.Create(
            // A DEFAULT, or the argument is required and the ordinary use — the command typed with no
            // words — comes back as a protocol error. The same rule the tools here follow, and the
            // plan round predicted this one before the wire test caught it.
            (string problem = "") => Ask(problem),
            new McpServerPromptCreateOptions
            {
                Name = ConsultName,
                Title = "Ask another vendor's model about what you are stuck on",
                Description = """
                    Hand the problem to the consultant configured for this kind of caller. Give it the
                    problem in your own words, or nothing at all and the assistant states what it is
                    stuck on. The consultant reads this checkout READ-ONLY with its uncommitted change
                    and answers advice to verify — it is never told to change anything.
                    """,
            });
    }

    /// <summary>
    /// What the assistant is handed. An instruction, not a question: the person has already decided.
    /// </summary>
    /// <remarks>
    /// The two rules at the end are the ones that make a consultation worth its tokens, and they are
    /// the ones an agent drops first: advice is MATERIAL to verify rather than a patch to apply, and
    /// the next turn of the same consultation must report what verifying it produced — which is also
    /// what the server's anti-ping-pong rule refuses a turn without.
    /// </remarks>
    internal static GetPromptResult Ask(string problem) => new()
    {
        Description = "Consult another vendor's model about what this session is stuck on",
        Messages =
        [
            new PromptMessage
            {
                Role = Role.User,
                Content = new TextContentBlock { Text = Text(problem) },
            },
        ],
    };

    /// <summary>
    /// How much of a problem statement travels: the SERVER's own bound, not a second one.
    /// </summary>
    /// <remarks>
    /// <para>A prompt that echoes a 10 MiB build log returns it to be sent again, which is the same
    /// bytes three times over before a consultation starts. Cut HERE, with a sentence saying so,
    /// rather than silently.</para>
    /// <para>And cut at exactly what <see cref="ConsultantPrompt.BoundedProblem"/> would cut it to a
    /// turn later, by taking that constant rather than declaring a second one — a prompt whose bound
    /// drifted from the tool's would truncate text the tool would have carried, or promise room the
    /// tool then takes away. Counted in CHARACTERS, which is the unit the server counts and says.
    /// (codex and gemini, both code rounds.)</para>
    /// </remarks>
    internal const int ProblemCap = ConsultantPrompt.ProblemBudget;

    /// <summary>What the person actually said, bounded — or nothing, when they said nothing.</summary>
    /// <remarks>
    /// Its own method because the two questions are different and reading them as one nested ternary
    /// was worth a finding: whether anything was said at all, and then whether what was said fits.
    /// (SonarCloud, Major, on the pull request.)
    /// </remarks>
    private static string Stated(string raw)
    {
        if (raw.Trim().Length == 0)
        {
            return string.Empty;
        }

        return raw.Length <= ProblemCap
            ? raw
            : raw[..ProblemCap] + $"\n\n[cut here at {ProblemCap} characters — send the rest as a follow-up turn if it matters]";
    }

    internal static string Text(string problem)
    {
        // Trimmed only to DECIDE whether anything was said. The words themselves travel as written:
        // the message tells the assistant to send them unrewritten, and leading whitespace in a code
        // block or a command with significant spacing is exactly what a stuck person pastes.
        var raw = problem ?? string.Empty;
        var stated = Stated(raw);

        return $"""
            Consult another vendor's model about this, with the `consult` tool of the `coai` server.

            {(stated.Length > 0
                ? $"The problem, in the person's own words — send it as `problem` without rewriting it:\n\n{stated}"
                : "They did not restate the problem, which means you are expected to know it. Say what is stuck in your own words: what was expected, what happens instead, and what you have already tried and ruled out.")}

            `repoPath` is this checkout's own top level (`git rev-parse --show-toplevel`). Name the
            files you suspect in `suspectedFiles` when you have a view, and leave it empty when you do
            not — a guess presented as a lead is worse than no lead.

            Two rules about the answer, and they are the ones that make this worth its tokens:

            1. **The advice is MATERIAL, not an instruction.** It comes from a model that cannot see
               your conversation, cannot run anything and cannot change this repository. Verify it —
               with a test, a run, a read of the code it names — before you act on any of it, and say
               plainly when it is wrong.
            2. **Report the verification back.** A follow-up on the same `consultationId` must say
               what you tried and what it produced; the server refuses a turn that merely repeats the
               question, because a consultant told nothing new can only repeat itself.
            """;
    }
}

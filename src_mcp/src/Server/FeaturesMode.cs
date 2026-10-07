using System.Text.Json;

namespace CoaiMcp.Server;

/// <summary>
/// <c>--features</c>: the capabilities this build has, printed as <c>{"features":[...]}</c>, and leave.
/// </summary>
/// <remarks>
/// <para>The extension writes a catalog field, or passes a flag, only when the installed binary lists it
/// (todo/PLAN_one_model_catalog.md, epic 2 as revised by its plan round): the binary is the one thing that knows what
/// it does, where a <c>*_SINCE</c> constant would guess a release number nobody has cut yet. An older binary exits 64
/// for this mode, which the extension reads as an empty list.</para>
/// <para><b>An entry is added in the commit that makes it true, never ahead of it</b> — a listed capability the build
/// does not have is a field the extension sends and this binary ignores, the silence the list exists to end.</para>
/// </remarks>
internal static class FeaturesMode
{
    /// <summary>What this build accepts, each with the story that added it.</summary>
    internal static readonly IReadOnlyList<string> Listed =
    [
        // E2.1: `--collect-bugs --runtime <runtime>` — the Bugz ranking model is allowed by its row's runtime.
        "bugzRuntime",
        // E2.2: a row's `systemPrompt`, delivered in the prompt body after the product's instruction, never in argv.
        "systemPrompt",
        // E2.2: a CLI row's `timeoutMinutes`, its launch timeout in place of the round's.
        "timeoutMinutes",
        // E2.2: a CLI row's `effort` — claude's levels as --effort, a local row's per call; a codex row's kept, not sent.
        "cliEffort",
        // E2.3: a consultant on an api row, or a codex row on somebody else's endpoint (its provider on every turn).
        "apiConsultant",
        // E2.4: COAI_SECURITY_LANE's `signals` (a signal's words) and a prompt's `words` — an older binary refuses the members.
        "securityWords",
        // E2.4: `--check-security [--validate]` on stdin — what the editor's "Try it" and save-time check ask.
        "checkSecurity",
        // E2.4: `--check-model` — the consultant's check of any catalog row, read on stdin (D10).
        "checkModel",
        // A row's `stream`: an api row asks for its answer as a stream (research/PLAN_api_streaming.md).
        "apiStream",
        // C2 (the epics 1–3 consultation): a consultant entry's and a question row's `row` — the whole catalog row, read
        // by the reviewer row's parser, its options applied to the consultation.
        "consultantRow",
    ];

    internal static async Task<int> RunAsync()
    {
        await Console.Out.WriteLineAsync(Answer());

        return 0;
    }

    /// <summary>The answer: one object, so a later field (a contract version, say) is an addition, not a new shape.</summary>
    internal static string Answer()
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteStartArray("features");
            foreach (var feature in Listed)
            {
                json.WriteStringValue(feature);
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}

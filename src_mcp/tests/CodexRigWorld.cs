using System.Text;
using System.Text.Json;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Server.RoleJobs;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The world of a codex coder scenario (epoch 3 E3 s5 t4 and t5): a <see cref="CoderRig"/> whose coder is the REAL <see cref="CodexCoderVendor"/> in a table that makes it launchable (the product's own table keeps the mark off until the last commit of s5), whose CLI is FakeCli, whose installed CLI answers the probes
/// (<c>--version</c>, <c>exec --help</c>, <c>exec resume --help</c>) from the recorded help of codex, and whose real home holds a MADE-UP sign-in the job's copy is cut from. Nothing real is read. The variables the probes read are cleared with the rig.
/// </summary>
[Collection("fakecli-env")]
internal sealed class CodexRigWorld : IAsyncDisposable
{
    private CodexRigWorld(CoderRig rig) => Rig = rig;

    public CoderRig Rig { get; }

    /// <summary>The table that makes codex launchable, with the real vendor in it.</summary>
    internal static CoderVendorSet Vendors() => CoderVendorSet.Of(CoderBoundaryTable.Parse("""
        { "version": 1, "judgement": "j.md", "shared": [],
          "vendors": [ { "id": "codex", "verdict": "ship", "results": "r.md",
            "launchable": true, "measured": [ { "release": "codex-cli 0.171.0", "resultsHeading": "H", "judgementHeading": "J", "programs": [ { "file": "x", "sha256": "0000000000000000000000000000000000000000000000000000000000000000" } ] } ],
            "properties": [], "pending": [] } ] }
        """), CodexCoderVendor.Instance);

    internal static async Task<CodexRigWorld> CreateAsync()
    {
        var world = new CodexRigWorld(await CoderRig.CreateAsync());
        var rig = world.Rig;
        rig.CodexUnit = true;
        rig.Vendors = Vendors();
        rig.Rig.Setting("COAI_ROLE_CODEX_CLI", RoleJobWorld.FakeCli);
        Put("FAKECLI_VERSION_STDOUT", "codex-cli 0.171.0\n");
        Put("FAKECLI_EXEC_HELP_FILE", Path.Combine(AppContext.BaseDirectory, "fixtures", "codex", "exec-help-0.164.0-wsl.txt"));
        Put("FAKECLI_EXEC_RESUME_HELP_FILE", Path.Combine(AppContext.BaseDirectory, "fixtures", "codex", "exec-resume-help-0.164.0-wsl.txt"));
        rig.RealHomeFile(Path.Combine(".codex", "auth.json"), JsonSerializer.Serialize(new
        {
            auth_mode = "chatgpt",
            tokens = new { id_token = "made-up-id-token", access_token = Jwt(DateTime.UtcNow.AddDays(2)), refresh_token = "made-up-refresh-token-not-a-credential", account_id = "made-up", oauth_provenance = "made-up", login_owner_id = "made-up" },
            last_refresh = "2026-11-26T10:00:00Z",
        }));

        return world;
    }

    private static void Put(string name, string value) => Environment.SetEnvironmentVariable(name, value);

    private static string Jwt(DateTime expires) =>
        $"{B64("{\"alg\":\"none\"}")}.{B64($"{{\"exp\":{new DateTimeOffset(expires).ToUnixTimeSeconds()}}}")}.{B64("signature-made-up")}";

    private static string B64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public async ValueTask DisposeAsync()
    {
        await Rig.DisposeAsync();
        // The rig clears every FAKECLI_ name it knows; the ones this world set for the probes are cleared by the same door, never put back to what a rig left (a claude version that the next class would read as its own).
        FakeCliSteering.Reset();
    }
}

/// <summary>The turns FakeCli plays as codex: a stream of events, the report in the output file, the rollout file the CLI leaves in its home, and the edits it makes.</summary>
internal static class CodexScript
{
    internal const string ThreadId = "01a217ad-59bd-7543-907c-6b343bd876d3";

    internal const string OtherThread = "01a217af-0f53-7310-a2e4-641468fecbb1";

    /// <summary>The stream of one turn: the thread, the items, the end.</summary>
    internal static string Stream(string thread, params string[] items) =>
        string.Join('\n', [$"{{\"type\":\"thread.started\",\"thread_id\":\"{thread}\"}}", "{\"type\":\"turn.started\"}", .. items, "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":10,\"cached_input_tokens\":0,\"output_tokens\":5}}"]) + "\n";

    internal static string Message(string text = "done") => $"{{\"type\":\"item.completed\",\"item\":{{\"id\":\"item_0\",\"type\":\"agent_message\",\"text\":\"{text}\"}}}}";

    internal static string OfType(string type, string rest = "") => $"{{\"type\":\"item.completed\",\"item\":{{\"id\":\"item_9\",\"type\":\"{type}\"{rest}}}}}";

    /// <summary>The flat report of a codex coder (null members for the arm it does not use) that answers every dod id.</summary>
    internal static string Report(string notes = "null") =>
        "{\"status\":\"ok\",\"reason\":null,\"dod\":[" + string.Join(',', CoderRig.Dod.Select(id => $"{{\"id\":\"{id}\",\"met\":true,\"evidence\":\"{id} is shown by a test\"}}")) + "],\"notes\":" + notes + "}";

    /// <summary>The rollout file of a thread, as the CLI makes it under <c>sessions</c> of its home: one call of <c>exec</c>, and more lines when a test says.</summary>
    internal static Dictionary<string, string> Rollout(string thread, params string[] more) => new()
    {
        [Path.Combine(".codex", "sessions", "2026", "11", "26", $"rollout-2026-11-26T10-00-00-{thread}.jsonl")] = string.Join('\n', ["{\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"name\":\"exec\",\"arguments\":\"{}\"}}", .. more]) + "\n",
    };

    /// <summary>One launch of FakeCli as codex: the edits it makes below the worktree, the stream it prints, the report it writes to the output file and the rollout it leaves.</summary>
    internal static object Turn(IReadOnlyDictionary<string, string>? edits, string report, string thread = ThreadId, string[]? items = null, IReadOnlyDictionary<string, string>? rollout = null, string stderr = "", int exit = 0, IReadOnlyList<string>? tryWrites = null, int holdAfterMs = 0)
    {
        var entry = new Dictionary<string, object?>
        {
            ["stdout"] = Stream(thread, items ?? [Message()]),
            ["outfileText"] = report,
            ["exit"] = exit,
            ["homeFiles"] = rollout ?? Rollout(thread),
        };
        if (edits is not null)
        {
            entry["writeFiles"] = edits;
        }

        if (stderr.Length > 0)
        {
            entry["stderr"] = stderr;
        }

        if (holdAfterMs > 0)
        {
            entry["holdAfterMs"] = holdAfterMs;
        }

        if (tryWrites is not null)
        {
            entry["tryWriteFiles"] = tryWrites;
        }

        return entry;
    }

    internal static IReadOnlyDictionary<string, string> Writes(string text) => new Dictionary<string, string> { [CoderRig.StateFile] = text + "\n" };

}

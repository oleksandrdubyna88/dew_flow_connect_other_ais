using System.Text.Json;
using CoaiMcp.Core.Cadence;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Worktrees;

namespace CoaiMcp.Server;

/// <summary>
/// <c>--cadence --repo &lt;path&gt; --branch &lt;name&gt; [--plan &lt;file&gt;]</c>: a plan's consultation cadence, exactly as
/// <c>status</c> answers it, for the sidebar (<c>todo/PLAN_consult_on_a_cadence.md</c>, epic 4 story 4.2).
/// </summary>
/// <remarks>
/// <para><b>Why a mode.</b> The sidebar never calls <c>status</c> — it reads the session files — and the cadence
/// cannot be worked out from those: the record holds neither the plan's epics nor the grouping, and the answer
/// also needs the plan at the branch's commit, the repository's identity and the consultation evidence. So it is
/// asked of the one computation, in C#, through the sanctioned one-shot shape (<c>.agents/PROJECT.md</c>).</para>
/// <para><b>Exit codes.</b> 0 with the answer on stdout — the JSON <c>status</c> carries as <c>cadence</c>, or
/// <c>null</c> when there is nothing to draw (the cadence is off, or the session holds no plan and none was named);
/// 65 for a request that is malformed or names a branch with no session; 74 for anything that could not be read.
/// Never 64: that is "this binary has never heard of the mode", which is how the panel spots an old server.</para>
/// <para>A unit of its own rather than more of <c>Program.cs</c>, for the reason <c>PairsDecideMode</c> gives.</para>
/// </remarks>
internal static class CadenceReadMode
{
    private const string Usage = "--cadence needs --repo <path> --branch <name> [--plan <repo-relative plan file>]";

    private const string Nothing = "null";

    internal static async Task<int> RunAsync(string[] args)
    {
        var configuration = SettingsFile.Layer(
            SettingsFile.DataDirFrom(Environment.GetEnvironmentVariable).Path,
            Environment.GetEnvironmentVariable,
            // STDERR: stdout carries the answer the panel parses.
            Program.Note);
        var (code, answer, why) = await AnswerAsync(PanelSettings.FromEnvironment(configuration), args, CancellationToken.None);
        if (answer.Length > 0)
        {
            await Console.Out.WriteLineAsync(answer);
        }

        if (why.Length > 0)
        {
            Program.Note(why);
        }

        return code;
    }

    /// <summary>The exit code, what goes to stdout and what goes to stderr — the whole mode, with no console in it.</summary>
    internal static async Task<(int Code, string Out, string Err)> AnswerAsync(PanelSettings settings, string[] args, CancellationToken ct)
    {
        var flags = Program.Flags(args);
        flags.TryGetValue("--repo", out var repo);
        flags.TryGetValue("--branch", out var branch);
        flags.TryGetValue("--plan", out var plan);
        if (string.IsNullOrWhiteSpace(repo) || string.IsNullOrWhiteSpace(branch))
        {
            return (65, string.Empty, Usage); // EX_DATAERR
        }

        try
        {
            return await ReadAsync(settings, repo, branch, plan ?? string.Empty, ct);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return (74, string.Empty, $"the cadence could not be read: {e.Message}"); // EX_IOERR
        }
    }

    private static async Task<(int Code, string Out, string Err)> ReadAsync(
        PanelSettings settings, string repo, string branch, string plan, CancellationToken ct)
    {
        var session = new SessionStore(settings.DataDir, settings.Rounds.Catalog).Load(repo, branch);
        if (session is null)
        {
            return (65, string.Empty, $"no session for {repo} on {branch} — nothing has been through the gate there");
        }

        if (settings.CadenceMode == CadenceMode.Off)
        {
            return (0, Nothing, string.Empty);
        }

        var launcher = new ProcessLauncher();
        var sha = await new WorktreeManager(launcher, settings.WorktreeRoot).ShaOrNoneAsync(repo, branch);
        var answer = await CadenceDesk.ForReading(settings, launcher).AnswerAsync(session, plan, sha, ct);

        return (0, answer is null ? Nothing : JsonSerializer.Serialize(answer, ServerJsonContext.Default.CadenceAnswer), string.Empty);
    }
}

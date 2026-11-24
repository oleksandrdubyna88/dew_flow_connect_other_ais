using System.Globalization;
using System.Text.Json;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The <c>probe</c> role's work: a scripted brief that exercises the detached job end to end with no model and no repository access. The first word of the brief says what to do —
/// <c>succeed</c> (also an empty brief), <c>sleep N</c> (N seconds, then succeed), <c>fail WHY</c>, <c>child N</c> (starts a <c>sleep N</c> child through the recording launcher and waits for it).
/// </summary>
/// <remarks>
/// <para>It exists for two readers. A person or a post-deploy check can start it on a machine to prove that a runner outlives its starter, that the lease and heartbeat work and that a
/// cancel stops the whole tree — at no cost. And the tests of this story drive the REAL binary with it, where a double inside the product would be a second product.</para>
/// <para>It honours its token everywhere it waits, which is what makes <c>role_cancel</c> and the job's own timeout work for it exactly as they will for a real role. A brief it cannot read fails
/// naming the legal forms — never a silent success.</para>
/// </remarks>
internal sealed class ProbeWork : IRoleWork
{
    internal const string Forms = "succeed | sleep N | fail WHY | child N";

    /// <summary>The longest wait a brief may ask for, in seconds — a probe is never a way to hold a runner for a day.</summary>
    internal const int LongestWaitSeconds = 3600;

    public async Task<RoleWorkOutcome> RunAsync(IRoleRun run, CancellationToken ct)
    {
        var words = run.Request.Brief.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var verb = words.Length == 0 ? "succeed" : words[0].ToLowerInvariant();
        run.Phase("probe", 1);

        return verb switch
        {
            "succeed" => Succeeded(run),
            "fail" => new RoleWorkOutcome.Failed("probe-failed", words.Length > 1 ? string.Join(' ', words[1..]) : "the probe was told to fail"),
            "sleep" when Seconds(words) is { } seconds => await SleepAsync(run, seconds, ct),
            "child" when Seconds(words) is { } seconds => await ChildAsync(run, seconds, ct),
            _ => new RoleWorkOutcome.Failed("probe-brief", $"the probe brief '{run.Request.Brief}' is not one of: {Forms} (N is whole seconds, 1 to {LongestWaitSeconds})"),
        };
    }

    private static int? Seconds(string[] words) =>
        words.Length == 2 && int.TryParse(words[1], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds is >= 1 and <= LongestWaitSeconds ? seconds : null;

    private static async Task<RoleWorkOutcome> SleepAsync(IRoleRun run, int seconds, CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
        }
        catch (OperationCanceledException)
        {
            return new RoleWorkOutcome.Stopped();
        }

        return Succeeded(run);
    }

    private static async Task<RoleWorkOutcome> ChildAsync(IRoleRun run, int seconds, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
        {
            return new RoleWorkOutcome.Failed("probe-child", "the child form needs a POSIX host: it starts `sleep`");
        }

        var launcher = new ProcessLauncher(run.Children);
        var request = new ProcessRequest("sleep", [seconds.ToString(CultureInfo.InvariantCulture)], run.Folder)
        {
            TrackAs = "probe-child",
            Timeout = TimeSpan.FromSeconds(seconds + 30),
        };
        var result = await launcher.RunAsync(request, ct);

        return result switch
        {
            { Cancelled: true } => new RoleWorkOutcome.Stopped(),
            { ExitCode: 0 } => Succeeded(run),
            _ => new RoleWorkOutcome.Failed("probe-child", $"the child exited {result.ExitCode}"),
        };
    }

    private static RoleWorkOutcome Succeeded(IRoleRun run)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("probe", "ok");
            json.WriteString("brief", run.Request.Brief);
            json.WriteEndObject();
        }

        return new RoleWorkOutcome.Succeeded(System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
    }
}

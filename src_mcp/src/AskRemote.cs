using System.Diagnostics;
using System.Net;
using System.Text;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp;

/// <summary>
/// <c>coai-mcp --ask-remote</c> — the shim that sends one review to a Team server and waits.
/// </summary>
/// <remarks>
/// <para>The twin of <c>--ask-local</c>, and launched the same way: the adapter builds a command line
/// and this process does the HTTP, so the round's own code never learns that a reviewer was remote.
/// Every sentence and every exit code lives in <see cref="RemoteAsk"/>, which is pure and tested
/// without a server.</para>
/// <para><b>It cancels what it abandons.</b> On its own deadline it sends <c>DELETE</c> before
/// exiting, so a review nobody will collect stops costing the team's subscription. It also writes the
/// job id to <c>--job-file</c> the moment the server accepts: when the executor KILLS this process
/// instead of letting it finish, nothing here runs at all, and that file is the only thing left that
/// can stop the job. <see cref="RemoteRuntime.CancelAbandonedAsync"/> is the other half.</para>
/// </remarks>
internal static class AskRemote
{
    internal static async Task<int> RunAsync(
        IReadOnlyDictionary<string, string> flags, Action<string> note, TextWriter output)
    {
        var server = TeamServerAuth.Normalise(flags.GetValueOrDefault("--server", string.Empty));
        var vendor = flags.GetValueOrDefault("--vendor", string.Empty);
        var model = flags.GetValueOrDefault("--model", string.Empty);
        var role = flags.GetValueOrDefault("--role", string.Empty);
        var promptFile = flags.GetValueOrDefault("--prompt-file", string.Empty);
        var outFile = flags.GetValueOrDefault("--out", string.Empty);
        var tokenFile = flags.GetValueOrDefault("--token-file", string.Empty);
        var jobFile = flags.GetValueOrDefault("--job-file", string.Empty);

        if (server.Length == 0 || vendor.Length == 0 || promptFile.Length == 0 || outFile.Length == 0)
        {
            note("--ask-remote needs --server, --vendor, --prompt-file and --out");

            return RemoteAsk.BadUsage;
        }

        var token = TeamServerAuth.ReadToken(tokenFile);
        if (token.Length == 0)
        {
            // Not a failure of the server or the network: this machine has never signed in, or the
            // token was removed. It has its own exit code so the panel can offer the right button.
            note(RemoteAsk.NotSignedInMessage(server));

            return RemoteAsk.NotSignedIn;
        }

        // TWO clocks, and they are not the same one — the lesson story 2.3 paid for on the server.
        //
        // `--timeout-seconds` is how long THIS PROCESS waits before it cancels and reports; it is
        // deliberately shorter than the executor's, so reaching it produces a sentence instead of a
        // kill. `--vendor-timeout-seconds` is how long the VENDOR may take, which is the caller's own
        // reviewer timeout and is what the server is told. Sending the first as the second asked the
        // server for an eight-second review and got a 400 naming its range, which is how this was
        // found.
        var deadline = int.TryParse(flags.GetValueOrDefault("--timeout-seconds", ""), out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromMinutes(10);
        var vendorBudget = int.TryParse(flags.GetValueOrDefault("--vendor-timeout-seconds", ""), out var budget)
            ? budget
            : (int)Math.Ceiling(deadline.TotalSeconds);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(RemoteAsk.LongPollSeconds + 15) };
        http.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
        http.DefaultRequestHeaders.Add(RemoteAsk.ContractHeader, RemoteAsk.ContractVersion.ToString());

        return await ReviewAsync(
            new Job(server, vendor, model, role, promptFile, outFile, jobFile, deadline, vendorBudget),
            http, note, output);
    }

    private sealed record Job(
        string Server, string Vendor, string Model, string Role,
        string PromptFile, string OutFile, string JobFile, TimeSpan Deadline, int VendorBudgetSeconds);

    private static async Task<int> ReviewAsync(Job job, HttpClient http, Action<string> note, TextWriter output)
    {
        var waited = Stopwatch.StartNew();
        string prompt;
        try
        {
            prompt = await File.ReadAllTextAsync(job.PromptFile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            note($"the prompt file {job.PromptFile} could not be read: {e.Message}");

            return RemoteAsk.BadUsage;
        }

        var submitted = await SubmitAsync(job, prompt, http, note);
        if (submitted.Exit is { } refused)
        {
            return refused;
        }

        // Claimed BEFORE the first poll: from here on, a kill leaves a file that can still cancel it.
        RemoteRuntime.Claim(job.JobFile, job.Server, submitted.Id);

        return await PollAsync(job, submitted.Id, waited, http, note, output);
    }

    private static async Task<(string Id, int? Exit)> SubmitAsync(
        Job job, string prompt, HttpClient http, Action<string> note)
    {
        var body = RemoteAsk.RequestBody(job.Vendor, job.Model, job.Role, prompt, job.VendorBudgetSeconds);

        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(TeamServerAuth.Endpoint(job.Server, "api/reviews"), content);
            var text = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return (string.Empty, Refuse(job.Server, (int)response.StatusCode, text, note));
            }

            var id = RemoteAsk.AcceptedId(text);
            if (id.Length == 0)
            {
                // A 2xx whose body is not a review. Named rather than crashed on: something in front
                // of the server can answer 200 with a login page.
                note(RemoteAsk.UnreadableMessage(job.Server, text));

                return (string.Empty, RemoteAsk.Unreachable);
            }

            return (id, null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            note(RemoteAsk.UnreachableMessage(job.Server, e.Message));

            return (string.Empty, RemoteAsk.Unreachable);
        }
    }

    private static async Task<int> PollAsync(
        Job job, string id, Stopwatch waited, HttpClient http, Action<string> note, TextWriter output)
    {
        var position = 0;
        while (waited.Elapsed < job.Deadline)
        {
            // The long poll never asks for longer than what is LEFT. Asking for the full 25 seconds
            // with 6 to go meant a 6-second deadline took 26 seconds to notice — and the whole point
            // of this deadline being the shorter one is that reaching it produces a sentence instead
            // of the executor killing the process. Found by running it against a real server.
            var remaining = job.Deadline - waited.Elapsed;
            var poll = await PollOnceAsync(job, id, WaitSecondsFor(remaining), http, note);
            if (poll.Exit is { } stopped)
            {
                RemoteRuntime.Forget(job.JobFile);

                return stopped;
            }

            if (poll.Answer is { } state)
            {
                position = state.Position;
                if (state.State is RemoteState.Done or RemoteState.Failed)
                {
                    RemoteRuntime.Forget(job.JobFile);

                    return await FinishAsync(job, state, note, output);
                }
            }

            await Task.Delay(RemoteAsk.PollGap);
        }

        // The polite path: cancel before giving up, so an answer nobody will collect stops being
        // paid for. The server's own queue deadline is the backstop for when this process is killed
        // instead of reaching here.
        await CancelAsync(job, id, http);
        RemoteRuntime.Forget(job.JobFile);
        note(RemoteAsk.TooSlowMessage(job.Server, waited.Elapsed, position));

        return RemoteAsk.Unreachable;
    }

    /// <summary>How long to ask the server to hold this poll: the smaller of its cap and what is left.</summary>
    private static int WaitSecondsFor(TimeSpan remaining) =>
        Math.Clamp((int)remaining.TotalSeconds, 1, RemoteAsk.LongPollSeconds);

    private static async Task<(RemotePoll? Answer, int? Exit)> PollOnceAsync(
        Job job, string id, int waitSeconds, HttpClient http, Action<string> note)
    {
        try
        {
            var url = TeamServerAuth.Endpoint(job.Server, $"api/reviews/{id}?wait={waitSeconds}");
            using var response = await http.GetAsync(url);
            var text = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return (null, Refuse(job.Server, (int)response.StatusCode, text, note));
            }

            var poll = RemoteAsk.ReadPoll(text);
            if (poll is null)
            {
                note(RemoteAsk.UnreadableMessage(job.Server, text));

                return (null, RemoteAsk.Unreachable);
            }

            return (poll, null);
        }
        catch (TaskCanceledException)
        {
            // The long poll's own timeout, which is ordinary: it means nothing changed. The loop's
            // deadline is what decides when to stop, not this.
            return (null, null);
        }
        catch (HttpRequestException e)
        {
            note(RemoteAsk.UnreachableMessage(job.Server, e.Message));

            return (null, RemoteAsk.Unreachable);
        }
    }

    private static async Task<int> FinishAsync(Job job, RemotePoll state, Action<string> note, TextWriter output)
    {
        if (state.State == RemoteState.Failed)
        {
            note(RemoteAsk.FailedMessage(job.Vendor, state.Failure, state.Reason));

            return RemoteAsk.VendorFailed;
        }

        try
        {
            await File.WriteAllTextAsync(job.OutFile, state.Answer);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            note($"the answer could not be written to {job.OutFile}: {e.Message}");

            return RemoteAsk.Unreachable;
        }

        // The usage line, on stdout, exactly where RemoteRuntime.ReadUsage looks for it.
        await output.WriteLineAsync(RemoteAsk.UsageLine(state.TokensIn, state.TokensOut));

        return RemoteAsk.Ok;
    }

    /// <summary>Best effort. A cancel that fails costs one queued job the server will expire anyway.</summary>
    private static async Task CancelAsync(Job job, string id, HttpClient http)
    {
        try
        {
            using var response = await http.DeleteAsync(TeamServerAuth.Endpoint(job.Server, $"api/reviews/{id}"));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // Nothing to add: the sentence about giving up is printed either way, and the server's
            // queue deadline ends the job.
        }
    }

    /// <summary>The exit for a status the server gave, with the sentence that belongs to it.</summary>
    private static int Refuse(string server, int status, string body, Action<string> note)
    {
        var said = ErrorText(body);
        note(status switch
        {
            (int)HttpStatusCode.Unauthorized => RemoteAsk.RejectedMessage(server),
            (int)HttpStatusCode.Forbidden => RemoteAsk.ForbiddenMessage(server),
            (int)HttpStatusCode.UpgradeRequired => RemoteAsk.TooOldMessage(server, said),
            _ => RemoteAsk.UnexpectedMessage(server, status, said),
        });

        return RemoteAsk.ExitForStatus(status) ?? RemoteAsk.Unreachable;
    }

    /// <summary>The server's own sentence, when it sent one.</summary>
    private static string ErrorText(string body)
    {
        try
        {
            var error = System.Text.Json.JsonSerializer.Deserialize(
                body, RemoteErrorContext.Default.ShimError);

            return error?.Error ?? body;
        }
        catch (System.Text.Json.JsonException)
        {
            return body;
        }
    }
}

internal sealed record ShimError(string? Error);

[System.Text.Json.Serialization.JsonSourceGenerationOptions(
    PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
[System.Text.Json.Serialization.JsonSerializable(typeof(ShimError))]
internal sealed partial class RemoteErrorContext : System.Text.Json.Serialization.JsonSerializerContext;

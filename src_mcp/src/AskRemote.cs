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

        var token = TeamServerAuth.ReadToken(tokenFile);
        if (Refusal(server, vendor, promptFile, outFile, token) is { } refused)
        {
            note(refused.Message);

            return refused.Exit;
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
        // Clamped here as well as in the adapter, because this shim is also runnable by hand and a
        // budget outside the server's 30..1800 is refused before the review starts.
        var vendorBudget = RemoteRuntime.VendorBudgetSeconds(
            int.TryParse(flags.GetValueOrDefault("--vendor-timeout-seconds", ""), out var budget)
                ? TimeSpan.FromSeconds(budget)
                : deadline);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(RemoteAsk.LongPollSeconds + 15) };
        http.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
        http.DefaultRequestHeaders.Add(RemoteAsk.ContractHeader, RemoteAsk.ContractVersion.ToString());

        return await ReviewAsync(
            new Job(server, vendor, model, role, promptFile, outFile, jobFile, tokenFile, deadline, vendorBudget),
            http, note, output);
    }

    /// <summary>Why this shim will not even try — or null when it will.</summary>
    /// <remarks>
    /// Extracted so <c>RunAsync</c> stays inside the repository's complexity rule, and because the two
    /// refusals are genuinely different things: one is a command line nobody could have meant, the
    /// other is an ordinary state with its own exit code so the panel can offer the right button.
    /// </remarks>
    internal static (int Exit, string Message)? Refusal(
        string server, string vendor, string promptFile, string outFile, string token) =>
        (server.Length == 0 || vendor.Length == 0 || promptFile.Length == 0 || outFile.Length == 0, token.Length == 0)
        switch
        {
            (true, _) => (RemoteAsk.BadUsage, "--ask-remote needs --server, --vendor, --prompt-file and --out"),
            // Not a failure of the server or the network: this machine has never signed in, or the
            // token was removed.
            (false, true) => (RemoteAsk.NotSignedIn, RemoteAsk.NotSignedInMessage(server)),
            _ => null,
        };

    internal sealed record Job(
        string Server, string Vendor, string Model, string Role,
        string PromptFile, string OutFile, string JobFile, string TokenFile,
        TimeSpan Deadline, int VendorBudgetSeconds);

    private static async Task<int> ReviewAsync(Job job, HttpClient http, Action<string> note, TextWriter output)
    {
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

        // Started AFTER the prompt is read, so a large file on a slow disk is not later reported as
        // time the server spent thinking.
        var waited = Stopwatch.StartNew();

        var submitted = await SubmitAsync(job, prompt, http, note);
        if (submitted.Exit is { } refused)
        {
            return refused;
        }

        // Claimed BEFORE the first poll: from here on, a kill leaves a file that can still cancel it.
        var unclaimed = RemoteRuntime.Claim(job.JobFile, job.Server, submitted.Id, job.TokenFile);
        if (unclaimed.Length > 0)
        {
            // Not fatal — the server's queue deadline still ends the job — but said out loud, because
            // the consequence is a bill nobody can later explain.
            note($"this review could not be recorded as cancellable ({unclaimed}); if it is abandoned "
                + "it will run to completion on the Team server");
        }

        return await PollAsync(job, submitted.Id, waited, http, note, output);
    }

    private static async Task<(string Id, int? Exit)> SubmitAsync(
        Job job, string prompt, HttpClient http, Action<string> note)
    {
        var body = RemoteAsk.RequestBody(job.Vendor, job.Model, job.Role, prompt, job.VendorBudgetSeconds);

        try
        {
            // The submit gets the review's own deadline, so a stalled server cannot spend more of it
            // than the person allowed for the whole review.
            using var budget = new CancellationTokenSource(job.Deadline);
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(
                TeamServerAuth.Endpoint(job.Server, "api/reviews"), content, budget.Token);
            var text = await response.Content.ReadAsStringAsync(budget.Token);

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
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
        {
            note(RemoteAsk.UnreachableMessage(job.Server, e.Message));

            return (string.Empty, RemoteAsk.Unreachable);
        }
    }

    private static async Task<int> PollAsync(
        Job job, string id, Stopwatch waited, HttpClient http, Action<string> note, TextWriter output)
    {
        var seen = PollTracker.Start;
        while (waited.Elapsed < job.Deadline)
        {
            // The long poll never asks for longer than what is LEFT. Asking for the full 25 seconds
            // with 6 to go meant a 6-second deadline took 26 seconds to notice — and the whole point
            // of this deadline being the shorter one is that reaching it produces a sentence instead
            // of the executor killing the process. Found by running it against a real server.
            var remaining = job.Deadline - waited.Elapsed;
            var attempt = await PollOnceAsync(job, id, WaitSecondsFor(remaining), remaining, http, note);
            seen = seen.After(attempt, job, note);

            var step = Decide(attempt, seen.Failures, job.Server);
            if (step.Verdict != PollVerdict.KeepWaiting)
            {
                return await EndAsync(job, id, step, http, note, output);
            }

            if (!await WaitAsync(job, waited))
            {
                break;
            }
        }

        // The polite path: cancel before giving up, so an answer nobody will collect stops being
        // paid for. The server's own queue deadline is the backstop for when this process is killed
        // instead of reaching here.
        await CancelAsync(job, id, http);
        RemoteRuntime.Forget(job.JobFile);
        note(RemoteAsk.TooSlowMessage(job.Server, waited.Elapsed, seen.Position));

        return RemoteAsk.TooSlow;
    }

    /// <summary>How many failed polls in a row mean the server is gone rather than the network blinked.</summary>
    private const int MaxConsecutiveFailures = 3;

    internal enum PollVerdict
    {
        KeepWaiting,
        Finish,
        Stop,
    }

    /// <param name="Note">What to tell the person before stopping, when there is something to say.</param>
    internal readonly record struct PollStep(PollVerdict Verdict, int Exit, RemotePoll? Answer, string Note)
    {
        public static readonly PollStep KeepWaiting = new(PollVerdict.KeepWaiting, 0, null, "");

        public static PollStep Finish(RemotePoll answer) => new(PollVerdict.Finish, RemoteAsk.Ok, answer, "");

        public static PollStep Stop(int exit, string note = "") => new(PollVerdict.Stop, exit, null, note);
    }

    /// <summary>
    /// What one poll means for the loop.
    /// </summary>
    /// <remarks>
    /// Pure, and separated from the loop so every branch is a unit test rather than something only a
    /// real server can produce. The loop above then does one thing: ask, and act on the answer.
    /// </remarks>
    internal static PollStep Decide(PollAttempt attempt, int failures, string server) => attempt switch
    {
        // A refusal the server MEANT — a bad token, a contract too old. Retrying only repeats it.
        { Exit: { } refused } => PollStep.Stop(refused),
        // A blip is tolerated; a run of them is an outage. Aborting on the first one abandoned
        // reviews that were running perfectly.
        { Transient: true } when failures >= MaxConsecutiveFailures =>
            PollStep.Stop(RemoteAsk.Unreachable, RemoteAsk.UnreachableMessage(server, $"{failures} polls in a row failed")),
        { Answer: null } => PollStep.KeepWaiting,
        { Answer: { State: RemoteState.Done or RemoteState.Failed } done } => PollStep.Finish(done),
        // Not folded into "still queued": a status this client cannot read is a reason to stop and
        // say so, not to wait out the deadline and then blame the server for being slow.
        { Answer: { State: RemoteState.Unknown } unknown } =>
            PollStep.Stop(RemoteAsk.TooOld, RemoteAsk.UnknownStateMessage(server, unknown.RawStatus)),
        _ => PollStep.KeepWaiting,
    };

    /// <summary>Leave the loop: say why, stop the job unless it stopped itself, and exit.</summary>
    private static async Task<int> EndAsync(
        Job job, string id, PollStep step, HttpClient http, Action<string> note, TextWriter output)
    {
        if (step.Verdict == PollVerdict.Finish)
        {
            RemoteRuntime.Forget(job.JobFile);

            return await FinishAsync(job, step.Answer!, note, output);
        }

        Say(step.Note, note);
        await CancelAsync(job, id, http);
        RemoteRuntime.Forget(job.JobFile);

        return step.Exit;
    }

    private static void Say(string message, Action<string> note)
    {
        if (message.Length > 0)
        {
            note(message);
        }
    }

    /// <summary>The gap between polls — skipped when it would overrun the deadline.</summary>
    /// <returns>False when there is no time left for another attempt.</returns>
    /// <remarks>
    /// The delay used to run unconditionally, so the loop could notice its own deadline a second
    /// late — small, but the deadline exists precisely so that the shim beats the executor's kill.
    /// </remarks>
    private static async Task<bool> WaitAsync(Job job, Stopwatch waited)
    {
        if (job.Deadline - waited.Elapsed <= RemoteAsk.PollGap)
        {
            return false;
        }

        await Task.Delay(RemoteAsk.PollGap);

        return true;
    }

    /// <summary>
    /// What the loop has learned so far: how many polls failed in a row, where the review sits, and
    /// what the person has already been told.
    /// </summary>
    internal readonly record struct PollTracker(int Failures, int Position, string Said)
    {
        public static readonly PollTracker Start = new(0, 0, "");

        public PollTracker After(PollAttempt attempt, Job job, Action<string> note) =>
            attempt.Answer is { } state
                ? new PollTracker(0, state.Position, Progress(job, state, Said, note))
                : this with { Failures = attempt.Transient ? Failures + 1 : Failures };
    }

    /// <summary>
    /// Say what changed, once per change.
    /// </summary>
    /// <remarks>
    /// A queued review can wait minutes behind other people's work, and the shim used to say nothing
    /// at all until it finished or gave up — so the queue position was first mentioned in the sentence
    /// announcing that the review had been cancelled. Reported on CHANGE rather than per poll, because
    /// a line every second is the same silence with more scrolling.
    /// </remarks>
    private static string Progress(Job job, RemotePoll state, string said, Action<string> note)
    {
        var now = state.State switch
        {
            RemoteState.Queued => $"queued on the Team server at {job.Server}, {state.Position} ahead of it",
            RemoteState.Running => $"running on the Team server at {job.Server}",
            _ => string.Empty,
        };

        Say(now.Length > 0 && now != said ? $"{job.Vendor}: {now}" : string.Empty, note);

        return now;
    }

    /// <summary>How long to ask the server to hold this poll: the smaller of its cap and what is left.</summary>
    internal static int WaitSecondsFor(TimeSpan remaining) =>
        Math.Clamp((int)remaining.TotalSeconds, 1, RemoteAsk.LongPollSeconds);

    /// <param name="Transient">
    /// The poll failed for a reason that may not repeat — a dropped connection, a server that did not
    /// answer this one long poll. The caller counts these; one is a blip, several in a row is an
    /// outage. It is NOT an exit, which is what a single dropped connection used to be.
    /// </param>
    internal readonly record struct PollAttempt(RemotePoll? Answer, int? Exit, bool Transient);

    private static async Task<PollAttempt> PollOnceAsync(
        Job job, string id, int waitSeconds, TimeSpan remaining, HttpClient http, Action<string> note)
    {
        // Bounded by what is LEFT of the review's own deadline, not only by the client's fixed
        // timeout. Without this a stalled server held each request for the full HttpClient timeout,
        // so a 10-second review could sit for 40 — long enough for the executor to kill it first, and
        // the person then sees nothing at all. Raised by two reviewers on the code round.
        using var budget = new CancellationTokenSource(remaining + RemoteAsk.PollGap);
        try
        {
            var url = TeamServerAuth.Endpoint(job.Server, $"api/reviews/{id}?wait={waitSeconds}");
            using var response = await http.GetAsync(url, budget.Token);
            var text = await response.Content.ReadAsStringAsync(budget.Token);

            if (!response.IsSuccessStatusCode)
            {
                return new PollAttempt(null, Refuse(job.Server, (int)response.StatusCode, text, note), false);
            }

            var poll = RemoteAsk.ReadPoll(text);
            if (poll is null)
            {
                note(RemoteAsk.UnreadableMessage(job.Server, text));

                return new PollAttempt(null, RemoteAsk.Unreachable, false);
            }

            return new PollAttempt(poll, null, false);
        }
        catch (OperationCanceledException)
        {
            // Either this poll's own budget or the client timeout. Both mean the server did not answer
            // a long poll it should have answered within its 25 seconds — which is a symptom, not the
            // "nothing changed" this used to be read as. Transient: the loop's deadline still decides
            // when to stop, but a server that never answers is now noticed rather than waited out.
            return new PollAttempt(null, null, true);
        }
        catch (HttpRequestException)
        {
            return new PollAttempt(null, null, true);
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
            // Its OWN short budget, not the client's 40 seconds: this runs after the review has
            // already failed or timed out, and the person is waiting on the sentence that follows it.
            using var budget = new CancellationTokenSource(CancelBudget);
            using var response = await http.DeleteAsync(
                TeamServerAuth.Endpoint(job.Server, $"api/reviews/{id}"), budget.Token);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
        {
            // Nothing to add: the sentence about giving up is printed either way, and the server's
            // queue deadline ends the job.
        }
    }

    /// <summary>How long the courtesy cancellation may take before the person's message is printed.</summary>
    private static readonly TimeSpan CancelBudget = TimeSpan.FromSeconds(10);

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

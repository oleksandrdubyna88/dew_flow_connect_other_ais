using System.Net.Http.Json;
using CoaiMcp.Core.Collecting;
using CoaiMcp.Store;

namespace CoaiMcp.Collecting;

/// <summary>What one upload did.</summary>
public sealed record UploadSummary(
    int Offered = 0, int Accepted = 0, int Duplicate = 0, int Refused = 0, string Trouble = "");

/// <summary>
/// Sending the pairs a person decided to keep.
/// </summary>
/// <remarks>
/// <para><b>This is the only thing in six stories that sends anything.</b> Everything before it reads
/// a local database and a local git history.</para>
/// <para><b>A pair is marked sent only on an ACKNOWLEDGEMENT</b>, never when the batch leaves. A pair
/// recorded before the server answered is a pair the client skips for ever, and a kill between the
/// request and the reply must leave it to be retried.</para>
/// <para><b>The wire types are <c>CoaiMcp.Core.Collecting</c>'s</b>, the same declarations the server
/// compiles against. They used to be a copy per binary — which two reviewers pointed out means a
/// renamed field leaves both halves compiling and both suites green while the field goes nowhere.</para>
/// </remarks>
public sealed class UploadRun(HttpClient http, TextWriter? progress = null)
{
    /// <summary>How many pairs one request carries. The server refuses more.</summary>
    public const int PerBatch = 200;

    /// <summary>Sends what is sendable, and writes down what came back.</summary>
    /// <remarks>
    /// <b>As many batches as it takes.</b> It sent ONE, so a queue of two thousand kept pairs
    /// answered "200 accepted" with exit code 0 and eighteen hundred pairs still waiting, with
    /// nothing in the output saying so. The loop stops on the caller's limit, on an empty queue, or
    /// on the first batch that goes wrong — because a batch that failed will fail the same way
    /// again. (Code round, codex.)
    /// </remarks>
    public async Task<UploadSummary> RunAsync(
        RoundsDb db, Uri server, string key, int limit, CancellationToken ct = default)
    {
        var total = new UploadSummary();
        while (total.Offered < limit && total.Trouble.Length == 0)
        {
            var sendable = db.Sendable(Math.Min(limit - total.Offered, PerBatch));
            if (sendable.Count == 0)
            {
                break;
            }

            Say($"sending {sendable.Count} pair(s) to {server}");
            total = Add(total, await OnceAsync(db, sendable, server, key, ct));
        }

        Say(total.Offered == 0
            ? "nothing to send: no kept pair is unsent"
            : $"{total.Accepted} accepted, {total.Duplicate} already held, {total.Refused} refused");

        return total;
    }

    /// <summary>One request, and what it came to.</summary>
    private async Task<UploadSummary> OnceAsync(
        RoundsDb db, IReadOnlyList<StoredPair> sendable, Uri server, string key, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(server, "/ingest"))
            {
                Content = JsonContent.Create(
                    new UploadRequest([.. sendable.Select(Wire)]),
                    Server.ServerJsonContext.Default.UploadRequest),
            };
            request.Headers.Authorization = new("Bearer", key);

            using var reply = await http.SendAsync(request, ct);
            if (!reply.IsSuccessStatusCode)
            {
                // NOTHING is marked. A refusal of the whole request says nothing about any single
                // pair, and marking them would lose every one of them silently.
                return new UploadSummary(
                    sendable.Count, Trouble: $"the server answered {(int)reply.StatusCode}");
            }

            var answer = await reply.Content.ReadFromJsonAsync(
                Server.ServerJsonContext.Default.UploadAnswer, ct);

            return Record(db, sendable, answer?.Items ?? []);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return new UploadSummary(sendable.Count, Trouble: e.Message);
        }
    }

    /// <summary>
    /// Writes down what the server said, matched to what was sent BY ID.
    /// </summary>
    /// <remarks>
    /// <para><b>It was matched by POSITION</b>, on the reasoning that the answer preserves the order
    /// of the request. It does today. Four reviewers pointed out what that costs the day it does not:
    /// a server that parallelises ingestion, or reorders anything at all while keeping the count,
    /// hands every acknowledgement to the wrong local pair — and the damage is silent, because both
    /// halves think they succeeded. The id is a pure function of the payload
    /// (<see cref="PairId.Of(UploadedPair)"/>), so this client can derive exactly what the server
    /// will, and does.</para>
    /// <para><b>Nothing is written unless the WHOLE answer lines up.</b> A word this does not know,
    /// an id it never sent, a missing id, a duplicate — any of them and the batch is left alone to be
    /// retried. An unrecognised word used to fall through to "refused", which would permanently
    /// strand a good pair and blame a normaliser that was working.</para>
    /// </remarks>
    private UploadSummary Record(
        RoundsDb db, IReadOnlyList<StoredPair> sent, IReadOnlyList<UploadResult> answers)
    {
        var byId = sent.ToDictionary(pair => PairId.Of(Wire(pair)), pair => pair, StringComparer.Ordinal);
        var trouble = Mismatch(byId, answers);
        if (trouble.Length > 0)
        {
            return new UploadSummary(sent.Count, Trouble: trouble);
        }

        var outcomes = answers
            .Select(answer => new SendOutcome(
                byId[answer.EntryId].FindingId,
                answer.Took == Took.Refused ? answer.Why : string.Empty,
                answer.Took == Took.Refused))
            .ToList();

        foreach (var answer in answers.Where(one => one.Took == Took.Refused))
        {
            // The refusal names a defect in OUR normaliser and is worth keeping. `--requeue-refused`
            // is what clears it once the normaliser is repaired.
            Say($"  refused {byId[answer.EntryId].SymbolName}: {answer.Why}");
        }

        // ONE transaction for the batch: a kill halfway through two hundred single updates left half
        // the batch marked and half not, which is a local state no retry can reason about.
        db.RecordSend(outcomes);

        return new UploadSummary(
            sent.Count,
            answers.Count(one => one.Took == Took.Accepted),
            answers.Count(one => one.Took == Took.Duplicate),
            answers.Count(one => one.Took == Took.Refused));
    }

    /// <summary>Why this answer cannot be trusted, or empty when it can.</summary>
    private static string Mismatch(
        IReadOnlyDictionary<string, StoredPair> byId, IReadOnlyList<UploadResult> answers)
    {
        if (answers.Count != byId.Count)
        {
            return $"the server answered about {answers.Count} of {byId.Count} pairs, so this "
                   + "client cannot tell which; nothing was marked";
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var answer in answers)
        {
            if (!Took.Known(answer.Took))
            {
                return $"the server said '{answer.Took}', which is not a word this client knows; "
                       + "nothing was marked";
            }

            if (!byId.ContainsKey(answer.EntryId) || !seen.Add(answer.EntryId))
            {
                return $"the server answered about '{answer.EntryId}', which this client did not "
                       + "send exactly once; nothing was marked";
            }
        }

        return string.Empty;
    }

    private static UploadSummary Add(UploadSummary total, UploadSummary batch) =>
        new(total.Offered + batch.Offered,
            total.Accepted + batch.Accepted,
            total.Duplicate + batch.Duplicate,
            total.Refused + batch.Refused,
            batch.Trouble);

    /// <summary>The three fields that cross, and nothing else.</summary>
    private static UploadedPair Wire(StoredPair pair) =>
        new(pair.Language, pair.SkeletonBefore, pair.SkeletonAfter);

    private void Say(string line) => progress?.WriteLine(line);
}

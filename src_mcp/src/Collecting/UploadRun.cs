using System.Net.Http.Json;
using CoaiMcp.Core.Collecting;
using CoaiMcp.Store;

namespace CoaiMcp.Collecting;

/// <summary>One pair, as it crosses the wire — three fields and no more.</summary>
/// <remarks>
/// <para><b>A type of its own, and an architecture test says so.</b> <see cref="StoredPair"/> carries
/// the finding id, the symbol, the severity, the category and the title, and every one of them must
/// never leave this machine. A story-5 code round flagged reusing it here as the obvious way to leak
/// all five at once; explicit mapping is human discipline, and the test is what makes widening this a
/// red build rather than a quiet afternoon.</para>
/// <para><b>No id.</b> The server derives one from these three fields, so nothing extra crosses and
/// there is no second copy to disagree with the first.</para>
/// </remarks>
public sealed record UploadedPair(string Language, string SkeletonBefore, string SkeletonAfter);

/// <summary>A batch, as it is sent.</summary>
public sealed record UploadRequest(IReadOnlyList<UploadedPair> Items);

/// <summary>What the server said about one item.</summary>
public sealed record UploadResult(string EntryId = "", string Took = "", string Why = "");

/// <summary>What the server said about the batch.</summary>
public sealed record UploadAnswer(IReadOnlyList<UploadResult>? Items = null);

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
/// </remarks>
public sealed class UploadRun(HttpClient http, TextWriter? progress = null)
{
    /// <summary>How many pairs one request carries. The server refuses more.</summary>
    public const int PerBatch = 200;

    /// <summary>Sends what is sendable, and writes down what came back.</summary>
    public async Task<UploadSummary> RunAsync(
        RoundsDb db, Uri server, string key, int limit, CancellationToken ct = default)
    {
        var sendable = db.Sendable(Math.Min(limit, PerBatch));
        if (sendable.Count == 0)
        {
            Say("nothing to send: no kept pair is unsent");

            return new UploadSummary();
        }

        Say($"sending {sendable.Count} pair(s) to {server}");

        UploadAnswer? answer;
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

            answer = await reply.Content.ReadFromJsonAsync(
                Server.ServerJsonContext.Default.UploadAnswer, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return new UploadSummary(sendable.Count, Trouble: e.Message);
        }

        return Record(db, sendable, answer?.Items ?? []);
    }

    /// <summary>
    /// Writes down what the server said, item by item.
    /// </summary>
    /// <remarks>
    /// Paired by POSITION, which the server's answer preserves. An answer of a different length is a
    /// contract the two halves no longer agree on, and the safe reading is to mark nothing: these
    /// halves have shipped out of step before.
    /// </remarks>
    private UploadSummary Record(
        RoundsDb db, IReadOnlyList<StoredPair> sent, IReadOnlyList<UploadResult> answers)
    {
        if (answers.Count != sent.Count)
        {
            return new UploadSummary(
                sent.Count,
                Trouble: $"the server answered about {answers.Count} of {sent.Count} pairs, so this "
                         + "client cannot tell which; nothing was marked");
        }

        var accepted = 0;
        var duplicate = 0;
        var refused = 0;
        foreach (var (pair, answer) in sent.Zip(answers))
        {
            switch (answer.Took)
            {
                case "accepted":
                    db.Sent(pair.FindingId);
                    accepted++;
                    break;

                case "duplicate":
                    // A success: the corpus already holds it and there is no reason to send it again.
                    db.Sent(pair.FindingId);
                    duplicate++;
                    break;

                default:
                    // The refusal is about the pair itself, so retrying produces the same answer. The
                    // reason names a defect in OUR normaliser and is worth keeping.
                    db.Refused(pair.FindingId, answer.Why);
                    Say($"  refused {pair.SymbolName}: {answer.Why}");
                    refused++;
                    break;
            }
        }

        Say($"{accepted} accepted, {duplicate} already held, {refused} refused");

        return new UploadSummary(sent.Count, accepted, duplicate, refused);
    }

    /// <summary>The three fields that cross, and nothing else.</summary>
    private static UploadedPair Wire(StoredPair pair) =>
        new(pair.Language, pair.SkeletonBefore, pair.SkeletonAfter);

    private void Say(string line) => progress?.WriteLine(line);
}

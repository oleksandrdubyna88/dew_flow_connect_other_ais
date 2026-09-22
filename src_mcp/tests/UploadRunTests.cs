using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CoaiMcp.Collecting;
using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The send, alone, against a server that answers whatever the test says.
/// </summary>
/// <remarks>
/// <para><b>Why a stubbed handler when <c>BothHalvesTests</c> runs the real server.</b> The real
/// server answers the way a healthy one does, which is the case least worth testing here: decision 3
/// of <c>PLAN_a_comment_crosses_the_machine_boundary.md</c> is a TABLE of the ways it answers
/// otherwise — a server without the commented route, a key refused, a limit, a server unwell, a
/// timeout, a 200 that does not state the contract — and each has its own answer, of which only the
/// first may say the server is old. Collapsing them was the round's finding: a 503 reported as "too
/// old" sends a person to redeploy for nothing and hides the retry that would have worked.</para>
/// <para><b>And nothing is marked on any of them.</b> A pair is marked sent only on an
/// acknowledgement; every row of that table must leave it offered again, which is asserted as the
/// pair still being <c>Sendable</c> and its <c>SentUtc</c> still empty.</para>
/// </remarks>
public sealed class UploadRunTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "coai-upload-" + Guid.NewGuid().ToString("N")[..8]);

    private static readonly Uri Server = new("https://bugs.example");

    private static readonly SessionState Session =
        new("s1", "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.CodeReview };

    /// <summary>Words no line of output may ever carry. Distinctive, so a leak cannot hide.</summary>
    private const string Secret = "SECRET-WORDS-7f3a";

    private RoundsDb Db() => RoundsDb.Open(_dir, Serilog.Core.Logger.None)!;

    /// <summary>One collected pair, kept, carrying the given words; its finding id.</summary>
    private long Kept(string comment)
    {
        using var db = Db();
        var found = new Finding(
            Severity.Major, Category.Reliability, "src/Totals.cs", 5, "a race", "it races",
            "hold the lock", ["codex"]);
        db.RecordRound(
            Session,
            new RoundRecord("CodeReview", 1, "revise", 1, "all answered", new DateTime(2026, 9, 16)),
            [found],
            new RoundContext("SCOPE", "aaaa111", "claude-code"));
        db.RecordDecisions("s1", "CodeReview", 1, [Decisions.Accept([found], 0)]);

        using var read = new SqliteConnection(
            $"Data Source={Path.Combine(_dir, RoundsDb.FileName)};Pooling=False");
        read.Open();
        using var one = read.CreateCommand();
        one.CommandText = "SELECT id FROM findings";
        var id = (long)one.ExecuteScalar()!;

        db.RecordCollect(
            id, "", "collected", "", "bbbb222", "run-1",
            new CollectedPair("GetOrAdd", "CSharp", "method_1() { }", "method_1() { lock { } }"));
        db.RecordDecide([new CommentedDecision(id, Keep.Kept, comment)]).Refusal.Should().BeEmpty();

        return id;
    }

    /// <summary>The run, through a handler answering as told; what it said, and what it came to.</summary>
    private async Task<(UploadSummary Summary, string Said)> Send(Answering server)
    {
        using var http = new HttpClient(server);
        var said = new StringWriter();
        using var db = Db();
        var summary = await new UploadRun(http, said).RunAsync(
            db, Server, "a-key", 10, TestContext.Current.CancellationToken);

        return (summary, said.ToString());
    }

    /// <summary>The pair was NOT marked: still offered, never stamped.</summary>
    private void NothingWasMarked()
    {
        using var db = Db();
        db.Sendable(10).Should().ContainSingle("a pair is marked only on an acknowledgement");
        db.Pairs(10)[0].SentUtc.Should().BeEmpty();
    }

    // ------------------------------------------------------------------------------------------
    // The route.
    // ------------------------------------------------------------------------------------------

    /// <summary>A batch carrying words takes the route that keeps them; one without takes the old one.</summary>
    [Theory]
    [InlineData("this one bit us", UploadRun.CommentedRoute)]
    [InlineData("", UploadRun.PlainRoute)]
    public async Task TheRouteIsChosenByWhetherTheBatchCarriesWords(string comment, string route)
    {
        Kept(comment);
        var server = Answering.With(Took.Accepted);

        (await Send(server)).Summary.Accepted.Should().Be(1);

        server.Paths.Should().Equal(route);
    }

    /// <summary>A pair without words sends no `comment` field at all — the old bytes, not a null.</summary>
    [Fact]
    public async Task APairWithoutWordsSendsNoCommentField()
    {
        Kept(string.Empty);
        var server = Answering.With(Took.Accepted);

        await Send(server);

        server.Bodies.Should().ContainSingle().Which.Should().NotContain(
            "comment", "an empty comment is omitted, so every deployed server receives what it always did");
    }

    // ------------------------------------------------------------------------------------------
    // Decision 3's failure table — one answer each, and only a 404 on the new route says "old".
    // ------------------------------------------------------------------------------------------

    /// <summary>A server without the commented route is OLD, says what to do, and nothing is marked.</summary>
    [Fact]
    public async Task A404OnTheCommentedRouteSaysTheServerIsOlder()
    {
        Kept("words");

        var (summary, _) = await Send(Answering.Status(HttpStatusCode.NotFound));

        summary.Trouble.Should().Be(UploadRun.OlderThanComments(1, 1));
        NothingWasMarked();
    }

    /// <summary>Every other status keeps its own sentence, and none of them says the server is old.</summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task AnyOtherStatusIsNotAnOldServer(HttpStatusCode status)
    {
        Kept("words");

        var (summary, _) = await Send(Answering.Status(status));

        summary.Trouble.Should().Be($"the server answered {(int)status}")
            .And.NotContain("older", "a key, a limit or an unwell server is not a server too old");
        NothingWasMarked();
    }

    /// <summary>A 404 on the PLAIN route is only a 404: an old server has that route.</summary>
    [Fact]
    public async Task A404OnThePlainRouteIsNotAnOldServerEither()
    {
        Kept(string.Empty);

        (await Send(Answering.Status(HttpStatusCode.NotFound))).Summary.Trouble
            .Should().Be("the server answered 404");
    }

    /// <summary>A timeout learned nothing, says so, and marks nothing.</summary>
    [Fact]
    public async Task ATimeoutIsNotAnOldServer()
    {
        Kept("words");

        var (summary, _) = await Send(Answering.Throwing(new TaskCanceledException("the request timed out")));

        summary.Trouble.Should().NotBeEmpty().And.NotContain("older");
        NothingWasMarked();
    }

    /// <summary>A 200 on the commented route that does not state contract 2 is not trusted.</summary>
    /// <remarks>A proxy answering for a route it does not really have, or half a deployment.</remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ACommentedBatchAnsweredWithoutTheContractMarksNothing(int contract)
    {
        Kept("words");

        var (summary, _) = await Send(Answering.With(Took.Accepted, contract: contract));

        summary.Trouble.Should().Be(UploadRun.UnstatedContract);
        NothingWasMarked();
    }

    /// <summary>A comment-free batch needs no contract: an old server's answer is still taken.</summary>
    [Fact]
    public async Task ACommentFreeBatchNeedsNoContract()
    {
        Kept(string.Empty);

        (await Send(Answering.With(Took.Accepted, contract: 0))).Summary.Accepted.Should().Be(1);
    }

    // ------------------------------------------------------------------------------------------
    // Words that did not land.
    // ------------------------------------------------------------------------------------------

    /// <summary>The server took the pair and not its words: written down, and said.</summary>
    [Fact]
    public async Task WordsTheServerDidNotStoreAreWrittenDownAndSaid()
    {
        var id = Kept("mine");
        const string lost = "this pair already carries a comment, and the first one stays, so yours was not stored";

        var (summary, said) = await Send(Answering.With(Took.Duplicate, lost));

        summary.Duplicate.Should().Be(1);
        said.Should().Contain("GetOrAdd").And.Contain("was not stored");
        using var db = Db();
        var pair = db.Pairs(10)[0];
        pair.FindingId.Should().Be(id);
        pair.SentUtc.Should().NotBeEmpty("the pair itself was acknowledged");
        pair.CommentLost.Should().Be(lost);
    }

    /// <summary>Words changed while their batch was in the air are said not to have gone.</summary>
    [Fact]
    public async Task WordsChangedDuringTheSendAreSaidNotToHaveGone()
    {
        var id = Kept("before the edit");
        var server = Answering.With(Took.Accepted, before: () =>
        {
            // A person typing in the box while the POST is out: the pair is not sent yet, so the
            // store allows it.
            using var db = Db();
            db.RecordDecide([new CommentedDecision(id, Keep.Kept, "after the edit")]).Refusal.Should().BeEmpty();
        });

        await Send(server);

        using var after = Db();
        var pair = after.Pairs(10)[0];
        pair.Comment.Should().Be("after the edit");
        pair.CommentLost.Should().Be(RoundsDb.EditedWhileSending);
    }

    // ------------------------------------------------------------------------------------------
    // A run that ends at either boundary of the request leaves nothing marked it did not earn.
    // ------------------------------------------------------------------------------------------

    /// <summary>Stopped before the request left: nothing crossed, nothing is marked.</summary>
    /// <remarks>
    /// A stop arrives as a cancellation, which the batch converts into trouble rather than letting
    /// escape — the same door a timeout comes through, because to this client the two are one
    /// fact: no answer. What matters is the pair, and it is still offered.
    /// </remarks>
    [Fact]
    public async Task ARunStoppedBeforeTheRequestMarksNothing()
    {
        Kept("words");
        using var stopped = new CancellationTokenSource();
        await stopped.CancelAsync();
        var server = Answering.With(Took.Accepted);
        using var http = new HttpClient(server);
        using var db = Db();

        var summary = await new UploadRun(http).RunAsync(db, Server, "a-key", 10, stopped.Token);

        summary.Trouble.Should().NotBeEmpty("the run did not complete");
        server.Paths.Should().BeEmpty("nothing reached the server");
        NothingWasMarked();
    }

    /// <summary>The request reached the server and the answer never came back: nothing is marked.</summary>
    /// <remarks>
    /// The server may well have committed — that is the case the server's own retry rule exists for
    /// (<c>BothHalvesTests.ARetriedSendWhoseAnswerWasLostDoesNotCallItsOwnCommentLost</c>). What this
    /// side owes is to leave the pair offered, because it learned nothing.
    /// </remarks>
    [Fact]
    public async Task ARunThatLosesTheAnswerMarksNothing()
    {
        Kept("words");
        var server = Answering.Throwing(new HttpRequestException("the connection was reset"));

        var (summary, _) = await Send(server);

        server.Paths.Should().ContainSingle("the request did go out");
        summary.Trouble.Should().Contain("reset");
        NothingWasMarked();
    }

    // ------------------------------------------------------------------------------------------
    // The comment is logged nowhere.
    // ------------------------------------------------------------------------------------------

    /// <summary>Whatever happens to the send, the words never appear in what it says or reports.</summary>
    /// <remarks>
    /// The promise is that a comment is written to no log. What this client prints is its progress and
    /// its summary — the two things a person, a CI log or a support ticket would carry — so both are
    /// read, on the four ways a send ends. (Plan round of 4.2, codex.)
    /// </remarks>
    [Theory]
    [InlineData("accepted")]
    [InlineData("refused")]
    [InlineData("unwell")]
    [InlineData("timeout")]
    public async Task TheWordsAreNeverSaidAloud(string ending)
    {
        Kept(Secret);
        var server = ending switch
        {
            "accepted" => Answering.With(Took.Accepted),
            "refused" => Answering.With(Took.Refused, "the skeleton carries an identifier"),
            "unwell" => Answering.Status(HttpStatusCode.ServiceUnavailable),
            _ => Answering.Throwing(new TaskCanceledException("the request timed out")),
        };

        var (summary, said) = await Send(server);

        said.Should().NotContain(Secret);
        JsonSerializer.Serialize(summary, ServerJsonContext.Default.UploadSummary).Should().NotContain(Secret);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>A server that answers as a test tells it to, and remembers what it was sent.</summary>
    private sealed class Answering(Func<string, HttpResponseMessage> answer, Action? before = null) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        public List<string> Bodies { get; } = [];

        /// <summary>A 200 naming every pair it was sent, with one word and one reason for all of them.</summary>
        public static Answering With(string took, string why = "", int contract = Contract.Comments, Action? before = null) =>
            new(body => Json(new UploadAnswer(
                [.. Asked(body).Select(pair => new UploadResult(PairId.Of(pair), took, why))], contract)), before);

        public static Answering Status(HttpStatusCode status) => new(_ => new HttpResponseMessage(status));

        public static Answering Throwing(Exception e) => new(_ => throw e);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Paths.Add(request.RequestUri!.AbsolutePath);
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Bodies.Add(body);
            before?.Invoke();

            return answer(body);
        }

        private static IReadOnlyList<UploadedPair> Asked(string body) =>
            JsonSerializer.Deserialize(body, ServerJsonContext.Default.UploadRequest)!.Items ?? [];

        private static HttpResponseMessage Json(UploadAnswer answer) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(answer, ServerJsonContext.Default.UploadAnswer) };
    }
}

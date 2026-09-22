using CoaiBugs;
using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Normalising;
using CoaiMcp.Normalizer;
using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// The contributor's own words: stored verbatim, refused rather than scrubbed, and read only on
/// the route that exists to read them.
/// </summary>
/// <remarks>
/// <para>A comment is PUBLIC by the operator's decision of 2026-09-18, so nothing here sanitises
/// one. What these assert is the opposite: that the server takes what a person wrote, refuses what
/// would break a reader, and never quietly changes either.</para>
/// <para><b>The route is the mechanism.</b> A server older than this drops an unknown field in
/// silence, so a comment travels on a path that server does not have. The pair of tests that matter
/// most here are the two that prove the routes DIFFER — one reads a comment, the other ignores one
/// it is handed — because a mechanism that is only differently named is no mechanism.</para>
/// </remarks>
public sealed class TheCommentTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "coai-bugs-" + Guid.NewGuid().ToString("N")[..8]);

    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _keywords;

    public TheCommentTests()
    {
        Directory.CreateDirectory(_dir);
        var normalizer = new TreeSitterNormalizer();
        _keywords = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["CSharp"] = normalizer.KeywordsOf(SourceLanguage.CSharp),
        };
    }

    private static readonly KeyId Key = new("key-1");

    private static readonly DateTimeOffset Sixteenth = new(2026, 9, 16, 10, 0, 0, TimeSpan.Zero);

    private readonly FrozenClock _clock = new(Sixteenth);

    private Corpus Open()
    {
        var corpus = Corpus.Open(Path.Combine(_dir, "coai-bugs.db"));
        corpus.Issue(Key, "a-hash", string.Empty, Audit.By(AdminId.Cli, _clock));

        return corpus;
    }

    private static UploadedPair Pair(string comment = "", string before = "method_1(var_1) { }") =>
        new("CSharp", before, "method_1(var_1) { lock (var_2) { } }", comment);

    /// <summary>The batch as the COMMENTED route takes it.</summary>
    private IReadOnlyList<UploadResult> Commented(Corpus corpus, params UploadedPair[] items) =>
        corpus.Accept(Key, UtcMonth.Of(Sixteenth),
                scope => Ingest.Take(scope, items, _keywords, commented: true))
            .Should().BeOfType<Accepted<UploadAnswer>.Stored>().Subject.Answer.Items!;

    /// <summary>The batch as the PLAIN route takes it.</summary>
    private IReadOnlyList<UploadResult> Plain(Corpus corpus, params UploadedPair[] items) =>
        corpus.Accept(Key, UtcMonth.Of(Sixteenth), scope => Ingest.Take(scope, items, _keywords))
            .Should().BeOfType<Accepted<UploadAnswer>.Stored>().Subject.Answer.Items!;

    private string StoredComment(Corpus corpus) =>
        corpus.Waiting(50).Should().ContainSingle().Subject.Comment;

    // ------------------------------------------------------------------------------------------
    // The two routes really differ. This is the whole design in two tests.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void TheCommentedRouteStoresWhatAPersonWrote()
    {
        using var corpus = Open();

        Commented(corpus, Pair("this one bit us in production")).Should().ContainSingle()
            .Which.Took.Should().Be("accepted");

        StoredComment(corpus).Should().Be("this one bit us in production");
    }

    /// <summary>
    /// And the plain route REFUSES a comment it is handed, rather than dropping it.
    /// </summary>
    /// <remarks>
    /// <para>Without this the two routes would be one route with two names, and the 404 an old
    /// server gives would be the only thing keeping a comment out — the promise a person is owed,
    /// made by accident instead of on purpose.</para>
    /// <para><b>Refused, not dropped, and three code-round reviewers were right about that.</b> The
    /// whole reason this route exists is that an old server drops an unknown field in silence and
    /// answers <c>accepted</c>; a new server doing the same thing on its old path commits that exact
    /// failure, where it is harder to notice. Nothing is written, so the client may post the same
    /// batch to the right path with nothing lost.</para>
    /// </remarks>
    [Fact]
    public void ThePlainRouteRefusesAPairThatCarriesAComment()
    {
        using var corpus = Open();

        var one = Plain(corpus, Pair("this must not be lost")).Should().ContainSingle().Subject;

        one.Took.Should().Be("refused");
        one.Why.Should().Contain("/ingest/commented", "a person must be told where it does belong");
        one.Why.Should().NotContain("this must not be lost", "and the refusal never quotes it back");
        corpus.Waiting(50).Should().BeEmpty("nothing is written, so the batch can simply be resent");
    }

    /// <summary>And a pair with no comment goes down the old route exactly as it always did.</summary>
    [Fact]
    public void ThePlainRouteIsUnchangedForAPairWithoutAComment()
    {
        using var corpus = Open();

        Plain(corpus, Pair()).Should().ContainSingle().Which.Took.Should().Be("accepted");
        StoredComment(corpus).Should().BeEmpty();
    }

    [Fact]
    public void APairWithNoCommentIsStoredWithAnEmptyOne()
    {
        using var corpus = Open();

        Commented(corpus, Pair()).Should().ContainSingle().Which.Took.Should().Be("accepted");

        StoredComment(corpus).Should().BeEmpty(
            "an empty comment and a row written before comments existed are the same thing");
    }

    // ------------------------------------------------------------------------------------------
    // Refused, never scrubbed — and the refusal says what it refused.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void ACommentOverTheLimitRefusesThePairAndNamesTheLength()
    {
        using var corpus = Open();

        var one = Commented(corpus, Pair(new string('x', CommentRule.MostChars + 4)))
            .Should().ContainSingle().Subject;

        one.Took.Should().Be("refused");
        one.Why.Should().Contain("1004").And.Contain("1000");
        corpus.Waiting(50).Should().BeEmpty("a refused pair is not half-stored");
    }

    /// <summary>
    /// The four kinds that are refused, each named by its code point rather than printed.
    /// </summary>
    /// <remarks>
    /// The character is built here from its NUMBER rather than written into the attribute: a lone
    /// surrogate is not a valid string for a runner that serialises theory arguments, and a test
    /// that cannot be enumerated is a test that silently does not run.
    /// </remarks>
    [Theory]
    [InlineData(0x0000, "U+0000")] // NUL truncates sqlite3's own output of a TEXT column
    [InlineData(0x000d, "U+000D")] // a bare CR is a client defect: the client normalises to LF
    [InlineData(0x001b, "U+001B")] // ESC hands a terminal listing to whoever wrote the comment
    [InlineData(0x202e, "U+202E")] // the Trojan Source shape, invisible to a reviewer
    [InlineData(0xd83d, "U+D83D")] // half an emoji: not a scalar value, not encodable as UTF-8
    public void ACommentHoldingWhatBreaksAReaderIsRefusedByCodePoint(int code, string named)
    {
        using var corpus = Open();

        var one = Commented(corpus, Pair($"before{(char)code}after"))
            .Should().ContainSingle().Subject;

        one.Took.Should().Be("refused");
        one.Why.Should().Contain(named);
        one.Why.Should().NotContain("before", "a refusal never quotes the comment back into a log");
    }

    /// <summary>
    /// EVERY bidirectional control, not one of them.
    /// </summary>
    /// <remarks>
    /// <para>The whitelist is three literals and two ranges, and a test that sampled only U+202E
    /// would stay green with either range emptied. That is not hypothetical: these characters were
    /// first written as THEMSELVES in the rule, where each one renders as an empty pair of quotes —
    /// so a formatter, an editor or a copy through a tool that strips them would have silently
    /// removed a range, and nothing here would have noticed.</para>
    /// <para>Each is named by its code point rather than printed, for the same reason.</para>
    /// </remarks>
    [Theory]
    [InlineData(0x061c)] // ARABIC LETTER MARK
    [InlineData(0x200e)] // LEFT-TO-RIGHT MARK
    [InlineData(0x200f)] // RIGHT-TO-LEFT MARK
    [InlineData(0x202a)] // LEFT-TO-RIGHT EMBEDDING — the start of the override block
    [InlineData(0x202b)]
    [InlineData(0x202c)]
    [InlineData(0x202d)]
    [InlineData(0x202e)] // RIGHT-TO-LEFT OVERRIDE — the Trojan Source character
    [InlineData(0x2066)] // the isolates
    [InlineData(0x2067)]
    [InlineData(0x2068)]
    [InlineData(0x2069)]
    public void EveryBidirectionalControlIsRefused(int code)
    {
        using var corpus = Open();

        var one = Commented(corpus, Pair($"looks{(char)code}harmless"))
            .Should().ContainSingle().Subject;

        one.Took.Should().Be("refused");
        one.Why.Should().Contain($"U+{code:X4}");
    }

    /// <summary>And the characters on either side of those ranges are perfectly ordinary text.</summary>
    /// <remarks>
    /// The other half of the range assertion: a whitelist widened by a typo — <c>&lt;= '⁯'</c>
    /// for <c>&lt;= '⁩'</c> — would refuse punctuation nobody meant to refuse, and the theory
    /// above cannot see it.
    /// </remarks>
    [Theory]
    [InlineData(0x0629)] // ARABIC TEH MARBUTA, just past the letter mark
    [InlineData(0x200d)] // ZERO WIDTH JOINER — what holds a family emoji together
    [InlineData(0x2029)] // PARAGRAPH SEPARATOR, deliberately allowed
    [InlineData(0x202f)] // NARROW NO-BREAK SPACE, one past the override block
    [InlineData(0x2065)] // one before the isolates
    [InlineData(0x206a)] // one past them
    public void TheCharactersAroundThoseRangesAreOrdinaryText(int code)
    {
        using var corpus = Open();

        Commented(corpus, Pair($"looks{(char)code}harmless"))
            .Should().ContainSingle().Which.Took.Should().Be("accepted");
    }

    [Theory]
    [InlineData("two lines\nof it")]
    [InlineData("a tab\there")]
    [InlineData("emoji 🚀 and 中文")]
    [InlineData("  padded  ")]
    public void WhatAPersonActuallyWritesIsTaken(string said)
    {
        using var corpus = Open();

        Commented(corpus, Pair(said)).Should().ContainSingle().Which.Took.Should().Be("accepted");
        StoredComment(corpus).Should().Be(said, "the server stores what it is given, verbatim");
    }

    /// <summary>
    /// The alphabet guards the SKELETONS and does not reach the comment.
    /// </summary>
    /// <remarks>
    /// The operator's decision written as something that fails if somebody widens the whitelist to
    /// the new field: a comment is public by choice, and scanning it would be this product deciding
    /// it knows better than the person who typed it.
    /// </remarks>
    [Fact]
    public void TheAlphabetDoesNotRunOverAComment()
    {
        using var corpus = Open();

        Commented(corpus, Pair("ChargeAcmeCustomer is where it happens"))
            .Should().ContainSingle().Which.Took.Should().Be("accepted");

        Commented(corpus, Pair(before: "ChargeAcmeCustomer(var_1) { }"))
            .Should().ContainSingle().Which.Took.Should().Be("refused",
                "the same word in a skeleton is somebody's private code, and still refused");
    }

    /// <summary>
    /// A leak is reported before a long comment, because it is the one a person must see first.
    /// </summary>
    [Fact]
    public void APairThatBothLeaksAndOverRunsReportsTheLeak()
    {
        using var corpus = Open();

        var one = Commented(corpus,
                new UploadedPair("CSharp", "ChargeAcmeCustomer(var_1) { }", "method_1() { }",
                    new string('x', CommentRule.MostChars + 1)))
            .Should().ContainSingle().Subject;

        one.Why.Should().NotContain("1001").And.NotBeEmpty();
    }

    // ------------------------------------------------------------------------------------------
    // The identity, and what the second contributor is told.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Two people, one skeleton: one row, the first comment, and the second is TOLD.
    /// </summary>
    /// <remarks>
    /// `duplicate` is a success, so without the sentence the second person is told their comment
    /// landed when it did not. Storing both would need a comments table, which is a story of its own.
    /// </remarks>
    [Fact]
    public void ASecondCommentOnOnePairIsNotStoredAndSaysSo()
    {
        using var corpus = Open();

        Commented(corpus, Pair("mine was first")).Should().ContainSingle()
            .Which.Took.Should().Be("accepted");

        var second = Commented(corpus, Pair("and mine was not")).Should().ContainSingle().Subject;

        second.Took.Should().Be("duplicate", "it is still a success; the client may stop sending it");
        second.Why.Should().Contain("already carries a comment").And.Contain("not stored");
        second.Why.Should().NotContain("promoted", "that is a different thing to be told");
        StoredComment(corpus).Should().Be("mine was first");
    }

    /// <summary>
    /// A pair that was ALREADY waiting without a comment takes the first one it is offered.
    /// </summary>
    /// <remarks>
    /// <para>The case that is not an edge case at all: on the day this ships, every pair in
    /// quarantine is one that arrived before comments existed. Without this, a contributor who
    /// re-reads something they sent last week, writes a sentence about it and sends it again is
    /// answered <c>duplicate</c> for ever and their words are stored nowhere — which a code round
    /// called permanently blocking, and it was.</para>
    /// <para>It is still a <c>duplicate</c>: the pair itself is not new and the client may stop
    /// sending it. What changes is that the words landed, so nothing tells the person they did not.
    /// </para>
    /// </remarks>
    [Fact]
    public void APairAlreadyWaitingWithoutACommentTakesTheFirstOneOffered()
    {
        using var corpus = Open();
        Commented(corpus, Pair()).Should().ContainSingle().Which.Took.Should().Be("accepted");

        var second = Commented(corpus, Pair("we hit this again today"))
            .Should().ContainSingle().Subject;

        second.Took.Should().Be("duplicate", "the PAIR is not new, whatever was said about it");
        second.Why.Should().BeEmpty("nothing was lost, so there is nothing to report");
        StoredComment(corpus).Should().Be("we hit this again today");
    }

    /// <summary>But a pair a person already promoted is out of the queue, and is told so.</summary>
    /// <remarks>
    /// There is no waiting row to attach words to, and the decision the pair belonged to has been
    /// made. The contributor is told rather than left to assume their comment landed.
    /// </remarks>
    [Fact]
    public void APromotedPairTakesNoCommentAndSaysSo()
    {
        using var corpus = Open();
        var id = Commented(corpus, Pair())[0].EntryId;
        corpus.Promote(id, UtcInstant.Of(Sixteenth.AddHours(1))).Should().BeTrue();

        var again = Commented(corpus, Pair("too late, but here it is")).Should().ContainSingle().Subject;

        again.Took.Should().Be("duplicate");
        again.Why.Should().Contain("promoted").And.Contain("not stored");
        again.Why.Should().NotContain(
            "already carries a comment",
            "it carries none, and telling somebody that is a false account of where their words went");
    }

    /// <summary>A duplicate with NO comment says nothing extra — there is nothing to report.</summary>
    [Fact]
    public void ADuplicateWithoutACommentIsSilentAsItAlwaysWas()
    {
        using var corpus = Open();

        Commented(corpus, Pair());
        Commented(corpus, Pair()).Should().ContainSingle().Subject.Why.Should().BeEmpty();
    }

    // ------------------------------------------------------------------------------------------
    // The store says what became of the WORDS, separately from what became of the pair.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Each of the four outcomes, read straight off the store rather than through a sentence.
    /// </summary>
    /// <remarks>
    /// <para>These exist because a code round found a contract that lied in a way no caller could
    /// see: the answer was a <c>bool CommentLanded</c> computed as "the row was written OR the
    /// comment was attached", so a new pair with NO comment reported that a comment had landed. The
    /// only caller masked it by checking the comment's length itself, so nothing anywhere went red —
    /// and the next caller to trust the value would have told somebody their words were stored when
    /// there were none. (Code round, codex.)</para>
    /// <para>Asserted on <see cref="Corpus.Keep"/> directly, because that is where the contract is.
    /// A test that could only see the refusal sentences would have the same blind spot the defect
    /// hid in.</para>
    /// </remarks>
    [Fact]
    public void TheStoreSaysWhatBecameOfTheWords()
    {
        using var corpus = Open();
        var month = UtcMonth.Of(Sixteenth);

        corpus.Keep("CSharp", "method_1() { }", "method_1() { lock { } }", Key, month)
            .Words.Should().Be(Words.None, "a new pair with nothing said about it landed no words");
        corpus.Keep("CSharp", "method_2() { }", "method_2() { lock { } }", Key, month, "mine")
            .Words.Should().Be(Words.Stored, "a new pair carries the words it arrived with");
        corpus.Keep("CSharp", "method_2() { }", "method_2() { lock { } }", Key, month, "mine too")
            .Words.Should().Be(Words.AlreadySpokenFor, "and the first to speak keeps it");
        corpus.Keep("CSharp", "method_1() { }", "method_1() { lock { } }", Key, month, "late")
            .Words.Should().Be(Words.Stored, "a pair that had none takes the first offered");

        var promoted = Corpus.IdOf("CSharp", "method_1() { }", "method_1() { lock { } }");
        corpus.Promote(promoted, UtcInstant.Of(Sixteenth.AddHours(1))).Should().BeTrue();

        corpus.Keep("CSharp", "method_1() { }", "method_1() { lock { } }", Key, month, "later still")
            .Words.Should().Be(Words.TooLate, "there is no waiting row left to attach to");
    }

    // ------------------------------------------------------------------------------------------
    // And it survives the one moment somebody decided it was worth keeping.
    // ------------------------------------------------------------------------------------------

    /// <summary>Promotion carries the comment into the corpus with its pair.</summary>
    /// <remarks>
    /// <para>The failure this would otherwise have is the quietest one available: quarantine shows
    /// the comment, a person reads it, decides on it, promotes — and the corpus keeps the pair
    /// without the only words anybody wrote about it. Nothing errors and nothing is logged; the
    /// sentence is simply not there afterwards.</para>
    /// <para>Read with <see cref="TestSql"/> rather than through <see cref="Corpus"/>, because what
    /// is being asserted is what the <c>INSERT … SELECT</c> actually wrote into the column.</para>
    /// </remarks>
    [Fact]
    public void ThePromotedPairKeepsTheComment()
    {
        var path = Path.Combine(_dir, "coai-bugs.db");
        using (var corpus = Corpus.Open(path))
        {
            corpus.Issue(Key, "a-hash", string.Empty, Audit.By(AdminId.Cli, _clock));
            var id = Commented(corpus, Pair("this one bit us in production"))[0].EntryId;

            corpus.Promote(id, UtcInstant.Of(Sixteenth.AddHours(1))).Should().BeTrue();
        }

        TestSql.Scalar(path, "SELECT comment FROM corpus")
            .Should().Be("this one bit us in production");
    }

    public void Dispose() => Scratch.Delete(_dir);
}

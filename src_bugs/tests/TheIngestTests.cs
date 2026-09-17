using CoaiBugs;
using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Normalising;
using CoaiMcp.Normalizer;
using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// Taking a batch: every item judged on its own, and nothing identifying kept.
/// </summary>
/// <remarks>
/// This is the boundary the whole plan protects. Five stories of care about anonymity are worth
/// exactly what these decisions let through.
/// </remarks>
public sealed class TheIngestTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "coai-bugs-" + Guid.NewGuid().ToString("N")[..8]);

    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _keywords;

    public TheIngestTests()
    {
        Directory.CreateDirectory(_dir);
        var normalizer = new TreeSitterNormalizer();
        _keywords = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["CSharp"] = normalizer.KeywordsOf(SourceLanguage.CSharp),
            ["TypeScript"] = normalizer.KeywordsOf(SourceLanguage.TypeScript),
        };
    }

    private static readonly KeyId Key = new("key-1");

    private static readonly DateTimeOffset Sixteenth = new(2026, 9, 16, 10, 0, 0, TimeSpan.Zero);

    private readonly FrozenClock _clock = new(Sixteenth);

    /// <summary>A corpus with the batch's key issued: `Accept` checks the key is in force before it writes.</summary>
    private Corpus Open()
    {
        var corpus = Corpus.Open(Path.Combine(_dir, "coai-bugs.db"));
        corpus.Issue(Key, "a-hash", string.Empty, Audit.By(AdminId.Cli, _clock));

        return corpus;
    }

    private static readonly UploadedPair Good =
        new("CSharp", "method_1(var_1) { }", "method_1(var_1) { lock (var_2) { } }");

    /// <summary>The batch's results, through the real batch transaction, which is all any test here reads.</summary>
    private IReadOnlyList<UploadResult> Take(Corpus corpus, params UploadedPair[] items) =>
        corpus.Accept(Key, UtcMonth.Of(Sixteenth), scope => Ingest.Take(scope, items, _keywords))
            .Should().BeOfType<Accepted<UploadAnswer>.Stored>().Subject.Answer.Items!;

    [Fact]
    public void AGoodPairIsAccepted()
    {
        using var corpus = Open();

        var answer = Take(corpus, Good);

        var one = answer.Should().ContainSingle().Subject;
        one.Took.Should().Be("accepted");
        one.Why.Should().BeEmpty();
        one.EntryId.Should().HaveLength(64, "the id is a sha256 of what the pair IS");
    }

    /// <summary>The same pair twice is one row, and the second time says so.</summary>
    /// <remarks>
    /// `duplicate` is a SUCCESS: the corpus already holds it and the client may stop sending it.
    /// Reporting it as a refusal would make a client retry for ever.
    /// </remarks>
    [Fact]
    public void TheSamePairTwiceIsOneRow()
    {
        using var corpus = Open();

        Take(corpus, Good)[0].Took.Should().Be("accepted");
        Take(corpus, Good)[0].Took.Should().Be("duplicate");

        corpus.Waiting(50).Should().ContainSingle();
    }

    /// <summary>
    /// One refusal never strands its neighbours.
    /// </summary>
    /// <remarks>
    /// The defect a plan reviewer traced out before this existed: a whole-batch 4xx is a batch the
    /// client retries unchanged, for ever, with every valid pair behind the invalid one. So the
    /// answer is per item and the good ones land.
    /// </remarks>
    [Fact]
    public void OneBadPairDoesNotStrandTheGoodOnes()
    {
        using var corpus = Open();
        var leaky = new UploadedPair(
            "CSharp", "method_1() { ChargeAcmeCustomer(); }", "method_1() { lock (var_1) { } }");
        var other = new UploadedPair("CSharp", "method_1() { }", "method_1() { var_1 = 0; }");

        var answer = Take(corpus, Good, leaky, other);

        answer.Select(one => one.Took).Should().Equal(["accepted", "refused", "accepted"]);
        answer[1].Why.Should().Contain("ChargeAcmeCustomer", "a person must be told WHICH word");
        corpus.Waiting(50).Should().HaveCount(2, "the refusal lands in no table");
    }

    [Fact]
    public void ARefusedPairIsStoredNowhere()
    {
        using var corpus = Open();
        var leaky = new UploadedPair(
            "CSharp", """method_1() { var_1 = "https://acme.example/x"; }""", "method_1() { }");

        Take(corpus, leaky)[0].Took.Should().Be("refused");

        corpus.Waiting(50).Should().BeEmpty();
    }

    /// <summary>Both halves are checked, because a pair is as anonymous as its worse side.</summary>
    [Fact]
    public void TheAfterHalfIsCheckedToo()
    {
        using var corpus = Open();
        var leakyAfter = new UploadedPair(
            "CSharp", "method_1() { }", "method_1() { AcmePaymentGateway.Charge(); }");

        var answer = Take(corpus, leakyAfter);

        answer[0].Took.Should().Be("refused");
        answer[0].Why.Should().Contain("AcmePaymentGateway");
    }

    /// <summary>A pair that shows no change teaches nothing.</summary>
    /// <remarks>
    /// The artefact is the DIFFERENCE between broken and fixed. The collector should never produce
    /// one, so this is a client defect rather than a judgement about somebody's code.
    /// </remarks>
    [Fact]
    public void APairWithIdenticalHalvesIsRefused()
    {
        using var corpus = Open();

        var answer = Take(corpus, new UploadedPair("CSharp", "method_1() { }", "method_1() { }"));

        answer[0].Took.Should().Be("refused");
        answer[0].Why.Should().Contain("no change");
    }

    [Fact]
    public void ALanguageThisCorpusDoesNotHoldIsRefused()
    {
        using var corpus = Open();

        var answer = Take(corpus, new UploadedPair("COBOL", "method_1()", "method_1() { }"));

        answer[0].Took.Should().Be("refused");
        answer[0].Why.Should().Contain("COBOL");
    }

    /// <summary>The id is what the pair IS, so two people who found it once send one row.</summary>
    [Fact]
    public void TheIdIsAFunctionOfThePair()
    {
        Corpus.IdOf("CSharp", "a", "b").Should().Be(Corpus.IdOf("CSharp", "a", "b"));
        Corpus.IdOf("CSharp", "a", "b").Should().NotBe(Corpus.IdOf("CSharp", "a", "c"));
        Corpus.IdOf("CSharp", "a", "b").Should().NotBe(Corpus.IdOf("TypeScript", "a", "b"));
    }

    /// <summary>
    /// A NUL inside a skeleton cannot be made to look like the delimiter between two of them.
    /// </summary>
    /// <remarks>
    /// The id joined the three fields with <c>\0</c>, so these two DIFFERENT pairs hashed the
    /// same: the second came back <c>duplicate</c> and was never stored. A length in front of each
    /// field cannot be forged from the field's own bytes. (Code round, codex.)
    /// </remarks>
    [Fact]
    public void TwoPairsThatDifferOnlyByWhereANulSitsAreTwoIds() =>
        Corpus.IdOf("CSharp", "method_1() { }\0", "method_2() { }")
            .Should().NotBe(Corpus.IdOf("CSharp", "method_1() { }", "\0method_2() { }"));

    /// <summary>The client derives the same id the server will, or matching by id is theatre.</summary>
    [Fact]
    public void TheClientDerivesTheSameId() =>
        PairId.Of(Good).Should().Be(
            Corpus.IdOf("CSharp", "method_1(var_1) { }", "method_1(var_1) { lock (var_2) { } }"));

    /// <summary>
    /// A field the client omitted is null, and a null must not reach the validator.
    /// </summary>
    /// <remarks>
    /// <c>UploadedPair</c>'s <c>= null</c> defaults do NOT save it: the deserializer writes what the
    /// document said, and the document said nothing. This used to dereference inside
    /// <c>Alphabet.Refuse</c> and turn a malformed request into a 500. (Code round, codex.)
    /// </remarks>
    [Fact]
    public void APairWithNothingInItIsRefusedRatherThanThrown()
    {
        using var corpus = Open();

        var answer = Take(corpus, new UploadedPair());

        answer[0].Took.Should().Be("refused");
        answer[0].Why.Should().Contain("is not a language this corpus holds");
    }

    /// <summary>A pair the corpus already promoted does not come back into the queue.</summary>
    /// <remarks>
    /// It inserted into quarantine and asked the corpus AFTERWARDS, so a promoted pair resent was
    /// written back into quarantine and answered <c>duplicate</c> — and then sat in `--waiting` for
    /// ever, because promoting it again is a no-op. (Code round, codex/gemini.)
    /// </remarks>
    [Fact]
    public void APromotedPairDoesNotReturnToQuarantine()
    {
        using var corpus = Open();
        var id = Take(corpus, Good)[0].EntryId;
        corpus.Promote(id, UtcInstant.Of(Sixteenth.AddHours(1))).Should().BeTrue();

        Take(corpus, Good)[0].Took.Should().Be("duplicate");

        corpus.Waiting(50).Should().BeEmpty("a promoted pair is held, not waiting");
        corpus.Held().Should().Be(1);
    }

    public void Dispose() => Scratch.Delete(_dir);
}

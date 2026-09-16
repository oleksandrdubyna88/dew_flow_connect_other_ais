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

    private Corpus Open() => Corpus.Open(Path.Combine(_dir, "coai-bugs.db"));

    private static readonly UploadedPair Good =
        new("CSharp", "method_1(var_1) { }", "method_1(var_1) { lock (var_2) { } }");

    private IngestAnswer Take(Corpus corpus, params UploadedPair[] items) =>
        Ingest.Take(corpus, items, _keywords, "key-1", "2026-09-16T10:00:00Z");

    [Fact]
    public void AGoodPairIsAccepted()
    {
        using var corpus = Open();

        var answer = Take(corpus, Good);

        var one = answer.Items.Should().ContainSingle().Subject;
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

        Take(corpus, Good).Items[0].Took.Should().Be("accepted");
        Take(corpus, Good).Items[0].Took.Should().Be("duplicate");

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

        answer.Items.Select(one => one.Took).Should().Equal(["accepted", "refused", "accepted"]);
        answer.Items[1].Why.Should().Contain("ChargeAcmeCustomer", "a person must be told WHICH word");
        corpus.Waiting(50).Should().HaveCount(2, "the refusal lands in no table");
    }

    [Fact]
    public void ARefusedPairIsStoredNowhere()
    {
        using var corpus = Open();
        var leaky = new UploadedPair(
            "CSharp", """method_1() { var_1 = "https://acme.example/x"; }""", "method_1() { }");

        Take(corpus, leaky).Items[0].Took.Should().Be("refused");

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

        answer.Items[0].Took.Should().Be("refused");
        answer.Items[0].Why.Should().Contain("AcmePaymentGateway");
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

        answer.Items[0].Took.Should().Be("refused");
        answer.Items[0].Why.Should().Contain("no change");
    }

    [Fact]
    public void ALanguageThisCorpusDoesNotHoldIsRefused()
    {
        using var corpus = Open();

        var answer = Take(corpus, new UploadedPair("COBOL", "method_1()", "method_1() { }"));

        answer.Items[0].Took.Should().Be("refused");
        answer.Items[0].Why.Should().Contain("COBOL");
    }

    /// <summary>The id is what the pair IS, so two people who found it once send one row.</summary>
    [Fact]
    public void TheIdIsAFunctionOfThePair()
    {
        Corpus.IdOf("CSharp", "a", "b").Should().Be(Corpus.IdOf("CSharp", "a", "b"));
        Corpus.IdOf("CSharp", "a", "b").Should().NotBe(Corpus.IdOf("CSharp", "a", "c"));
        Corpus.IdOf("CSharp", "a", "b").Should().NotBe(Corpus.IdOf("TypeScript", "a", "b"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

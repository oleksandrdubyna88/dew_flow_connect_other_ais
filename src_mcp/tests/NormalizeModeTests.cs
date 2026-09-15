using CoaiMcp.Core.Normalising;
using CoaiMcp.Normalising;
using CoaiMcp.Normalizer;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The collector's parser, driven the way the collector drives it: a batch in, a batch out.
/// </summary>
/// <remarks>
/// <para>Every outcome the collector has to record is an ANSWER here rather than a failure — an
/// unsupported language, a line inside no function, a method that normalises cleanly. A batch of
/// fifty where two could not be resolved is a successful batch, and the two carry their reason.</para>
/// <para>The skip codes are the plan's own vocabulary, not a second set of words for the same
/// outcomes, so the collector records what it is handed.</para>
/// </remarks>
public sealed class NormalizeModeTests
{
    private static NormalizeResult Run(params NormalizeAsk[] asks) =>
        new NormalizeMode(new TreeSitterNormalizer()).Run(new NormalizeRequest(asks));

    private const string Racy = """
        public sealed class Cache
        {
            public int GetOrAdd(string invoiceKey, int amountDue)
            {
                if (!_invoiceTotals.ContainsKey(invoiceKey))
                {
                    _invoiceTotals.Add(invoiceKey, amountDue);
                }

                return _invoiceTotals[invoiceKey];
            }
        }
        """;

    [Fact]
    public void AMethodComesBackAsASkeletonWithNothingOfOursInIt()
    {
        var answer = Run(new NormalizeAsk("a", "src/Cache.cs", 6, Racy)).Items.Should().ContainSingle().Subject;

        answer.Id.Should().Be("a", "a batch is re-joined by the caller's own handle");
        answer.Language.Should().Be("CSharp");
        answer.Skip.Should().BeEmpty();
        answer.Kind.Should().Be("method_declaration");
        answer.StartLine.Should().Be(3);
        answer.Skeleton.Should().Contain("ContainsKey").And.Contain("Add");
        answer.Skeleton.Should().NotContain("invoiceKey").And.NotContain("GetOrAdd").And.NotContain("_invoiceTotals");
        answer.Leaks.Should().BeEmpty("a non-empty leak list is a defect in our code, not a property of the input");
    }

    [Fact]
    public void AFileInALanguageWeDoNotRead_IsASkipAndNotAFailure()
    {
        var answer = Run(new NormalizeAsk("a", ".github/workflows/ci.yml", 3, "on: push")).Items!.Single();

        answer.Skip.Should().Be("language_unsupported");
        answer.Skeleton.Should().BeEmpty();
    }

    [Fact]
    public void ALineInsideNoFunction_IsASkipAndNotAFailure()
    {
        var answer = Run(new NormalizeAsk("a", "src/Cache.cs", 1, Racy)).Items!.Single();

        answer.Skip.Should().Be("symbol_not_resolved", "line 1 is the class, and a class is not a method");
    }

    /// <summary>A batch answers every ask, in order, however the individual ones turn out.</summary>
    /// <remarks>
    /// The property the collector depends on: it sends fifty and matches the answers back by position
    /// and by id. One unresolvable method must not shorten the list or reorder it.
    /// </remarks>
    [Fact]
    public void ABatchAnswersEveryAsk_InOrder_WhateverHappenedToEachOne()
    {
        var result = Run(
            new NormalizeAsk("one", "src/Cache.cs", 6, Racy),
            new NormalizeAsk("two", "readme.md", 1, "# hello"),
            new NormalizeAsk("three", "src/Cache.cs", 1, Racy));

        result.Items!.Select(i => i.Id).Should().Equal(["one", "two", "three"]);
        result.Items!.Select(i => i.Skip).Should().Equal([string.Empty, "language_unsupported", "symbol_not_resolved"]);
    }

    [Fact]
    public void AnEmptyRequestIsAnEmptyAnswer() =>
        new NormalizeMode(new TreeSitterNormalizer()).Run(new NormalizeRequest()).Items.Should().BeEmpty();

    [Fact]
    public void TheModeHasItsOwnArgument()
    {
        Program.Classify(["--normalize"]).Should().Be(Program.Startup.Normalize);
        Program.Classify(["--normalise"]).Should().Be(Program.Startup.Usage, "a near miss is refused, not guessed");
    }

    /// <summary>Without both file arguments it refuses with 64, rather than writing somewhere odd.</summary>
    [Fact]
    public void TheModeNeedsBothFiles() =>
        Program.NormalizeJson(["--normalize", "--in", "only-one.json"]).Should().Be(64);

    /// <summary>A request file that is not there is the CALLER's mistake, and says so with 66.</summary>
    [Fact]
    public void AMissingRequestFileIs66_NotACrash() =>
        Program.NormalizeJson(
                ["--normalize", "--in", Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.json"),
                 "--out", Path.Combine(Path.GetTempPath(), $"out-{Guid.NewGuid():N}.json")])
            .Should().Be(66);

    /// <summary>End to end through the files, which is the only shape the collector will ever use.</summary>
    /// <remarks>
    /// Driven through <c>Program.NormalizeJson</c> rather than the mode class because the
    /// SERIALISATION is the half that can fail on its own: this binary has reflection-free JSON, so a
    /// shape missing from <c>ServerJsonContext</c> throws at run time while every unit test of the
    /// mode stays green.
    /// </remarks>
    [Fact]
    public void TheModeReadsARequestFileAndWritesAnAnswerFile()
    {
        var dir = Directory.CreateTempSubdirectory("coai-normalize-");
        try
        {
            var input = Path.Combine(dir.FullName, "asks.json");
            var output = Path.Combine(dir.FullName, "answers.json");
            File.WriteAllText(
                input,
                System.Text.Json.JsonSerializer.Serialize(
                    new NormalizeRequest([new NormalizeAsk("a", "src/Cache.cs", 6, Racy)]),
                    Server.ServerJsonContext.Default.NormalizeRequest));

            Program.NormalizeJson(["--normalize", "--in", input, "--out", output]).Should().Be(0);

            var written = System.Text.Json.JsonSerializer.Deserialize(
                File.ReadAllText(output), Server.ServerJsonContext.Default.NormalizeResult);

            written!.Items.Should().ContainSingle().Which.Skeleton.Should().Contain("ContainsKey");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}

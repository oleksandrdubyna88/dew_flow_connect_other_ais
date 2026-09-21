using System.Text.Json;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Every road a refusal can leave by, and every way a reviewer can end — counted, not remembered.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> §8 of the server-notices plan: <i>"'Every refusal' and 'every
/// reviewer failure' are claims a list can make and a codebase can quietly break."</i> Story 2.2 is
/// about to promise that every refusal returned to a calling AI is written down, and that is a
/// promise about a population nothing counts. The extension answered the same question for itself
/// and wrote down what it cost not to: a plan published 109 call sites, a table that summed to 113
/// and a sentence saying 95, and every test was green because no test knew the number.</para>
///
/// <para><b>What the plan round changed, and it is the whole design.</b> The first draft counted CALL
/// SITES of the two refusal helpers. codex showed that a count cannot carry the guarantee: remove one
/// call and add an uninstrumented one in the same file and the number does not move. gemini showed
/// that the count is not even obtainable by a text scan — <c>_log.Error(</c> appears throughout this
/// codebase and a naive search counts it.</para>
///
/// <para>So the guarantee is a BOUNDARY rather than a census of calls: an <c>ErrorAnswer</c> — the
/// only shape a refusal takes on the wire — is constructed in exactly one place, and both services
/// reach it through <see cref="CoaiMcp.Server.Refusal"/>. "Every refusal passes through one
/// instrumented point" is then a fact about the type, which story 2.2 can rely on, rather than a
/// number somebody keeps up to date. The call counts are still recorded, as INFORMATION for the
/// documents; nothing is guarded by them.</para>
///
/// <para><b>The inventory is generated, not typed.</b> <c>shared/refusal-sites.json</c> is written by
/// this suite when <c>COAI_RECORD_REFUSAL_SITES=1</c> and compared READ-ONLY otherwise, so CI on a
/// read-only checkout cannot rewrite it and a stale file cannot be accepted. (The plan round: a test
/// that writes into the source tree is a test that passes by changing the answer.)</para>
/// </remarks>
public sealed class TheRefusalRoadsAreCountedTests
{
    /// <summary>The one place the wire shape of a refusal may be built.</summary>
    private const string TheBoundary = "src_mcp/src/Server/Refusal.cs";

    /// <summary>Where the generated inventory lives, beside the other things both halves share.</summary>
    private static string Inventory =>
        Path.Combine(NoSourceFileCarriesAControlByteTests.RepositoryRoot(), "shared", "refusal-sites.json");

    [Fact]
    public void TheRefusalAnswerIsBuiltInExactlyOnePlace()
    {
        // The guarantee story 2.2 will rest on. Two places were what this found when it was written:
        // `PanelService.Error` and `ConsultationService.Error` each built their own, and a third
        // could have appeared in any file without a test noticing.
        var built = ProductionSources.FilesMentioning("new ErrorAnswer(");

        built.Keys.Should().Equal([TheBoundary],
            "every refusal a calling AI receives is an ErrorAnswer, so the place it is BUILT is the "
            + "one place instrumentation has to reach — found: {0}", string.Join(", ", built.Keys));
        built[TheBoundary].Should().Be(1, "and it is built once even there");
    }

    [Fact]
    public void NoProductionFileReachesARefusalHelperWithoutNamingIt()
    {
        // The two bypasses a spelling-based census cannot see, both named on the plan round: a
        // `using static` makes `Answer(...)` legal with the type nowhere on the line, and an alias
        // (`using R = ...Refusal;`) renames it out of every search. A `global using` of either is the
        // same thing one file further away, which is why this scans EVERY production file rather
        // than the two services.
        var imports = ProductionSources.FilesMentioning("using static")
            .Keys.Where(file => Names(ProductionSources.CodeOf(file)));
        var aliases = ProductionSources.Files()
            .Where(file => ProductionSources.CodeOf(file).Contains("=CoaiMcp.Server.Refusal;", StringComparison.Ordinal));

        imports.Should().BeEmpty("a static import hides a call from every census that reads spellings");
        aliases.Should().BeEmpty("and an alias renames it out of one");
    }

    [Fact]
    public void EveryReviewerEndingIsInTheInventory()
    {
        // Asked of the TYPE SYSTEM, not of source text: a seventh ending is a seventh subtype, and
        // reflection finds it the moment it exists. `ReviewerOutcome` is public in CoaiMcp.Runners
        // and this suite already constructs three of its cases by name.
        var endings = ReviewerEndings();

        endings.Should().BeEquivalentTo(Recorded().ReviewerEndings,
            "a reviewer ending nothing writes down is a round that failed in a way the page cannot "
            + "show — story 2.3 writes these, and this is what says the list is all of them");
        endings.Should().HaveCountGreaterThan(3, "the scan found almost nothing, so it is looking in "
            + "the wrong assembly and the comparison above means nothing");
    }

    [Fact]
    public void TheInventoryIsWhatTheCensusProduces()
    {
        // Read-only by default. With COAI_RECORD_REFUSAL_SITES=1 it writes instead, which is the
        // sanctioned way to regenerate: a test that silently rewrites its own expectation is a test
        // that cannot fail, and one that writes into a read-only CI checkout is a test that cannot
        // run.
        var census = Census();
        if (Environment.GetEnvironmentVariable("COAI_RECORD_REFUSAL_SITES") == "1")
        {
            File.WriteAllText(Inventory, JsonSerializer.Serialize(census, Shape) + "\n");
        }

        var recorded = Recorded();

        recorded.Boundary.Should().Be(census.Boundary);
        recorded.ReviewerEndings.Should().Equal(census.ReviewerEndings);
        recorded.Callers.Should().BeEquivalentTo(census.Callers,
            "the counts are information rather than a guard, but a file that disagrees with the scan "
            + "is a file somebody edited by hand — regenerate it with COAI_RECORD_REFUSAL_SITES=1");
    }

    [Fact]
    public void TheScanFindsItsKnownInstances_SoAnEmptyCensusCannotPass()
    {
        ProductionSources.Files().Should().HaveCountGreaterThan(200,
            "the walk found almost no production files, so every census here is asserting nothing");
        ProductionSources.FilesMentioning("ErrorAnswer").Should().NotBeEmpty(
            "the shape of a refusal is named somewhere, or this suite is scanning the wrong tree");
    }

    /// <summary>Whether a file's code names one of the two types a static import could hide.</summary>
    private static bool Names(string code) =>
        code.Contains("using staticCoaiMcp.Server.Refusal", StringComparison.Ordinal)
        || code.Contains("using staticCoaiMcp.Server.PanelService", StringComparison.Ordinal)
        || code.Contains("using staticCoaiMcp.Server.Consultation.ConsultationService", StringComparison.Ordinal);

    /// <summary>Every terminal state a reviewer run can be in, by name, in a stable order.</summary>
    private static IReadOnlyList<string> ReviewerEndings() =>
        [.. typeof(ReviewerOutcome).Assembly.GetTypes()
            .Where(type => type.IsSealed && typeof(ReviewerOutcome).IsAssignableFrom(type))
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)];

    /// <summary>
    /// What the scan says today: who reaches the boundary, and how many refusals each of them has.
    /// </summary>
    /// <remarks>
    /// The FILES are the guarantee — a third one is a third road — and the counts beside them are
    /// information for the documents, which have quoted a number nothing produced before. They are
    /// the unqualified calls of each service's own <c>Error</c> helper, which is the rule that does
    /// not count the logger.
    /// </remarks>
    private static RefusalSites Census() =>
        new(TheBoundary,
            [.. ProductionSources.FilesMentioning("Refusal.Answer(")
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new RefusalCaller(
                    pair.Key,
                    ProductionSources.UnqualifiedCalls(ProductionSources.CodeOf(pair.Key), "Error")))],
            ReviewerEndings());

    /// <summary>What the committed inventory says.</summary>
    private static RefusalSites Recorded() =>
        JsonSerializer.Deserialize<RefusalSites>(File.ReadAllText(Inventory), Shape)
            ?? throw new InvalidOperationException($"{Inventory} parsed to nothing");

    private static readonly JsonSerializerOptions Shape = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>One file that reaches the refusal boundary, and how many refusals it has.</summary>
    public sealed record RefusalCaller(string File, int Refusals);

    /// <summary>The census, as the inventory carries it.</summary>
    public sealed record RefusalSites(
        string Boundary,
        IReadOnlyList<RefusalCaller> Callers,
        IReadOnlyList<string> ReviewerEndings);
}

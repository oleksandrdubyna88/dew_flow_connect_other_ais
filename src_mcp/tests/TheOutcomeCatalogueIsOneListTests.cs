using System.Text.Json;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The server's outcome catalogue, held against <c>shared/consultation-outcomes.json</c>.
/// </summary>
/// <remarks>
/// <para>Both halves have their own copy of these four words: this one validates against it before
/// writing anything, and the panel turns it into the sentence a person reads and the choices a
/// person is offered. They ship separately — that is a standing fact about this product, not an
/// accident — so a word added here and not there is a refusal the other side cannot explain, and a
/// word added there and not here is a button whose only outcome is a refusal.</para>
/// <para>Against a file NEITHER half owns, which is the shape
/// <see cref="NothingReadsAnotherProgramsSourceTests"/> insists on: a suite that read the other
/// program's source to derive its expectation goes quiet on a reformat rather than red, because an
/// expression that stops matching produces an empty list every assertion passes over. The extension
/// asserts the same file from its own side. (codex Architecture, the code round of issue #309.)</para>
/// </remarks>
public sealed class TheOutcomeCatalogueIsOneListTests
{
    private sealed record Outcome(string Word, bool Verdict, string Label, string Detail, string Said);

    private static readonly Outcome[] Catalogue = [.. Shared().GetProperty("outcomes").EnumerateArray().Select(one => new Outcome(
        one.GetProperty("word").GetString() ?? string.Empty,
        one.GetProperty("verdict").GetBoolean(),
        one.GetProperty("label").GetString() ?? string.Empty,
        one.GetProperty("detail").GetString() ?? string.Empty,
        one.GetProperty("said").GetString() ?? string.Empty))];

    private static readonly string[] Sources = [.. Shared().GetProperty("sources").EnumerateArray()
        .Select(one => one.GetProperty("word").GetString() ?? string.Empty)];

    private static JsonElement Shared()
    {
        // tests/bin/<cfg>/net10.0 → the repository root, then the shared folder both sides read.
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "shared", "consultation-outcomes.json"));

        // Cloned, because the document owning this element is disposed with the using below and a
        // JsonElement outliving its document reads freed memory rather than failing loudly.
        using var parsed = JsonDocument.Parse(File.ReadAllBytes(path));

        return parsed.RootElement.Clone();
    }

    /// <summary>Without this, every assertion below would pass vacuously over an empty list.</summary>
    [Fact]
    public void TheSharedCatalogueActuallyLoaded()
    {
        Catalogue.Should().HaveCountGreaterThan(3, "four words, and a file that did not load has none");
    }

    /// <summary>Every word the shared file names is one this server will accept — and no others.</summary>
    [Fact]
    public void TheKnownWordsAreExactlyTheSharedOnes()
    {
        var known = Catalogue.Select(one => one.Word).ToArray();

        foreach (var word in known)
        {
            ConsultationOutcomes.Known(word).Should().BeTrue($"'{word}' is in the shared catalogue");
        }

        // And the other direction, which is the half a one-way check misses: a word this server
        // learned and nobody wrote down is a word the panel will never offer and never explain.
        ConsultationOutcomes.Verdicts.Should().BeSubsetOf(known);
        known.Should().Contain(ConsultationOutcomes.Lapsed);
    }

    /// <summary>
    /// And which of them is somebody's VERDICT, which is the distinction the feature turns on.
    /// </summary>
    /// <remarks>
    /// A caller may write a verdict; only the server writes <c>lapsed</c>. Getting this wrong in one
    /// half is not a cosmetic drift — it is either a button that is always refused or a clock's fact
    /// presented to a person as somebody's opinion.
    /// </remarks>
    [Fact]
    public void WhichWordsAreSomebodysVerdictIsTheSameOnBothSides()
    {
        foreach (var one in Catalogue)
        {
            ConsultationOutcomes.Verdicts.Contains(one.Word).Should().Be(
                one.Verdict,
                $"the shared catalogue calls '{one.Word}' {(one.Verdict ? "a verdict" : "the server's own")}");
        }
    }

    /// <summary>And the three doors an outcome can come through are the same three on both sides.</summary>
    /// <remarks>
    /// One of them is the server's own: <c>lapsed</c> is written by nobody, so an export that saw
    /// only `caller` and `person` would have to guess what the clock's entry meant.
    /// </remarks>
    [Fact]
    public void TheDoorsAnOutcomeComesThroughAreTheSharedOnes()
    {
        Sources.Should().HaveCount(3, "a file that did not load has none");
        ConsultationSources.All.Should().BeEquivalentTo(Sources);
    }

    /// <summary>The sentence the server hands back when a word is refused names every word that works.</summary>
    [Fact]
    public void TheRefusalNamesEveryVerdictTheSharedCatalogueHas()
    {
        var record = new ConsultationRecord(
            "c1", "caller-a", "claude", "s1", "D:/repo", "main", "sha", "codex", "gpt-6", "codex",
            ConsultationMemories.VendorRemembers, 5, "2026-09-17T09:00:00.0000000Z");

        var refusal = ConsultationClosing.Refusal(record, "caller-a", "probably_fine", byPerson: false);

        refusal.Should().NotBeNull();
        foreach (var verdict in Catalogue.Where(one => one.Verdict))
        {
            refusal.Should().Contain(verdict.Word, "a refusal that does not name a word nobody can use is a dead end");
        }
    }
}

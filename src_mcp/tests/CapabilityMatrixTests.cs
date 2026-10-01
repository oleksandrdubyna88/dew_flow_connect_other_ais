using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The capability table is the file in <c>shared/</c>, and admission answers the shared vectors —
/// the C# half of <c>shared/capability-matrix-vectors.json</c>; <c>capabilityAdmission.test.ts</c> is
/// the other (PLAN_question_consultant.md, S1 acceptance 1).
/// </summary>
/// <remarks>
/// <para>Read from disk the way <see cref="BuiltinRoleCatalogTests"/> reads its seed: from the test
/// binary up to the repository root, so the comparison is against the checked-in file and not the
/// copy the build embedded a moment ago.</para>
/// <para>A3's "blocked in the UI AND refused by the server" is one rule only while both sides answer
/// one set of vectors; two self-consistent loaders cannot notice that they disagree.</para>
/// </remarks>
public sealed class CapabilityMatrixTests
{
    private sealed record Vector(string Runtime, string Capability, bool Admitted, string Standing, string Flag, string Why);

    private sealed record FileRow(string Runtime, string Capability, string Standing, string Cli, string Version, string Date, string ResultRef, IReadOnlyList<string> Cells, string Note);

    private static readonly Vector[] Vectors = LoadVectors();

    private static readonly IReadOnlyList<FileRow> Seed = LoadSeed();

    private static string Shared(string file) => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "shared", file));

    private static Vector[] LoadVectors()
    {
        using var file = File.OpenRead(Shared("capability-matrix-vectors.json"));
        using var parsed = JsonDocument.Parse(file);

        return [.. parsed.RootElement.GetProperty("vectors").EnumerateArray().Select(v => new Vector(
            v.GetProperty("runtime").GetString() ?? "",
            v.GetProperty("capability").GetString() ?? "",
            v.GetProperty("admitted").GetBoolean(),
            v.GetProperty("standing").GetString() ?? "",
            v.GetProperty("flag").GetString() ?? "",
            v.GetProperty("why").GetString() ?? ""))];
    }

    private static IReadOnlyList<FileRow> LoadSeed()
    {
        using var file = File.OpenRead(Shared("runtime-capabilities.json"));
        using var parsed = JsonDocument.Parse(file);

        return [.. parsed.RootElement.GetProperty("rows").EnumerateArray().Select(r =>
        {
            var measured = r.GetProperty("measuredWith");
            return new FileRow(
                r.GetProperty("runtime").GetString() ?? "",
                r.GetProperty("capability").GetString() ?? "",
                r.GetProperty("standing").GetString() ?? "",
                measured.GetProperty("cli").GetString() ?? "",
                measured.GetProperty("version").GetString() ?? "",
                measured.GetProperty("date").GetString() ?? "",
                measured.GetProperty("resultRef").GetString() ?? "",
                [.. measured.GetProperty("cells").EnumerateArray().Select(c => c.GetString() ?? "")],
                measured.GetProperty("note").GetString() ?? "");
        })];
    }

    public static TheoryData<int> Cases()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Vectors.Length; i++)
        {
            data.Add(i);
        }

        return data;
    }

    [Fact]
    public void TheVectorsActuallyLoaded_AndCoverEveryPairOfTheTable()
    {
        // Without this every assertion below would pass vacuously over an empty list.
        Vectors.Should().HaveCountGreaterThan(15, "fifteen pairs plus the refusals by name");
        foreach (var row in Seed)
        {
            Vectors.Should().Contain(v => v.Runtime == row.Runtime && v.Capability == row.Capability,
                $"the table's {row.Runtime} × {row.Capability} row must have a vector deciding it");
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryVector_IsAdmittedOrRefused_AsMeasured(int index)
    {
        var vector = Vectors[index];

        var admission = CapabilityMatrix.Admit(vector.Runtime, vector.Capability);

        switch (admission)
        {
            case Admission.Admitted admitted:
                vector.Admitted.Should().BeTrue($"{vector.Runtime} × {vector.Capability}: {vector.Why}");
                admitted.Standing.Spelled().Should().Be(vector.Standing, vector.Why);
                admitted.Flag.Spelled().Should().Be(vector.Flag, vector.Why);
                break;
            case Admission.Refused refused:
                vector.Admitted.Should().BeFalse($"{vector.Runtime} × {vector.Capability}: {vector.Why}");
                refused.Reason.Should().Contain(vector.Runtime).And.Contain(vector.Capability,
                    "a refusal names the pair it refused");
                break;
        }
    }

    [Fact]
    public void TheEmbeddedTable_IsTheFileInShared_RowForRow()
    {
        var loaded = RuntimeCapabilities.Builtin.Rows;

        loaded.Select(r => (r.Runtime, r.Capability.Spelled())).Should().Equal(
            Seed.Select(r => (r.Runtime, r.Capability)),
            "the loader keeps the file's rows in the file's order");
        foreach (var (row, expected) in loaded.Zip(Seed))
        {
            var where = $"{row.Runtime} × {row.Capability.Spelled()}";
            row.Standing.Spelled().Should().Be(expected.Standing, where);
            row.MeasuredWith.Cli.Should().Be(expected.Cli, where);
            row.MeasuredWith.Version.Should().Be(expected.Version, where);
            row.MeasuredWith.Date.Should().Be(expected.Date, where);
            row.MeasuredWith.ResultRef.Should().Be(expected.ResultRef, where);
            row.MeasuredWith.Cells.Should().Equal(expected.Cells, where);
            row.MeasuredWith.Note.Should().Be(expected.Note, where);
        }
    }

    [Fact]
    public void EveryRuntimeTimesCapability_HasExactlyOneRow_AndNoRowIsOutsideTheMatrix()
    {
        var pairs = RuntimeCapabilities.Builtin.Rows.Select(r => (r.Runtime, r.Capability)).ToList();

        pairs.Should().OnlyHaveUniqueItems("two rows for one pair would be two answers");
        foreach (var runtime in RuntimeCapabilities.Runtimes)
        {
            foreach (var capability in Enum.GetValues<Capability>())
            {
                pairs.Should().Contain((runtime, capability), "every pair is decided by data, never by a default arm");
            }
        }

        pairs.Should().HaveCount(RuntimeCapabilities.Runtimes.Count * Enum.GetValues<Capability>().Length);
    }

    [Fact]
    public void EveryRow_CitesTheRecord_AndACellOrSaysWhyThereIsNone()
    {
        foreach (var row in RuntimeCapabilities.Builtin.Rows)
        {
            var where = $"{row.Runtime} × {row.Capability.Spelled()}";
            row.MeasuredWith.ResultRef.Should().Contain("RESULTS_question_consultant_capabilities.md", where);
            row.MeasuredWith.Date.Should().Be("2026-10-01", $"{where}: the full run's date");
            (row.MeasuredWith.Cells.Count > 0 || row.MeasuredWith.Note.Length > 0).Should().BeTrue(
                $"{where}: a row with no cell says why — by construction, or not measured");
            foreach (var cell in row.MeasuredWith.Cells)
            {
                cell.Should().StartWith("01a0f8c7-18e4-", $"{where}: every cell is from the full run 01a0f8c7");
            }
        }
    }

    /// <summary>D13's two refusals kept apart: the flags are what the data says, never what a runtime name suggests.</summary>
    [Fact]
    public void TheFlags_AreD13s_CodexUnconfinedEverywhere_AgyDefaultDeny_NothingElseFlagged()
    {
        foreach (var capability in Enum.GetValues<Capability>())
        {
            CapabilityMatrix.Admit("codex", capability).Should().BeOfType<Admission.Admitted>()
                .Which.Flag.Should().Be(AdmissionFlag.Unconfined, $"codex × {capability}: F2");
        }

        CapabilityMatrix.Admit("antigravity", Capability.Disk).Should().BeOfType<Admission.Admitted>()
            .Which.Flag.Should().Be(AdmissionFlag.DefaultDeny, "F7");
        CapabilityMatrix.Admit("antigravity", Capability.None).Should().BeOfType<Admission.Admitted>()
            .Which.Flag.Should().Be(AdmissionFlag.DefaultDeny, "F7: held by the headless default alone");
        foreach (var runtime in (string[])["claude", "local", "api"])
        {
            CapabilityMatrix.Admit(runtime, Capability.None).Should().BeOfType<Admission.Admitted>()
                .Which.Flag.Should().Be(AdmissionFlag.None, $"{runtime} × none is confined");
        }
    }

    [Fact]
    public void AnUnmeasuredPair_IsRefused_NamingTheRecordThatWouldAdmitIt()
    {
        var refused = CapabilityMatrix.Admit("api", Capability.Web).Should().BeOfType<Admission.Refused>().Which;

        refused.Reason.Should().Contain("not been measured").And.Contain("RESULTS_question_consultant_capabilities.md",
            "the cure for an unmeasured pair is a measurement, and the sentence says where it goes");
    }

    [Fact]
    public void AnUnsupportedPair_IsRefused_WithTheRowsOwnReason()
    {
        var refused = CapabilityMatrix.Admit("antigravity", Capability.Web).Should().BeOfType<Admission.Refused>().Which;

        refused.Reason.Should().Contain("cannot fetch a page headless", "the row's note is the reason a person reads");
    }

    [Fact]
    public void AnUnknownRuntimeOrCapability_IsRefused_NamingWhatIsKnown()
    {
        var runtime = CapabilityMatrix.Admit("gemini", "none").Should().BeOfType<Admission.Refused>().Which;
        runtime.Reason.Should().Contain("gemini").And.Contain("claude, codex, antigravity, local, api",
            "an unknown name fails naming the legal values");

        var capability = CapabilityMatrix.Admit("claude", "shell").Should().BeOfType<Admission.Refused>().Which;
        capability.Reason.Should().Contain("shell").And.Contain("none, disk, web");
    }

    [Fact]
    public void TheWordsOfTheFile_RoundTripThroughTheTypes()
    {
        foreach (var standing in Enum.GetValues<CapabilityStanding>())
        {
            CapabilityStandings.Parse(standing.Spelled()).Should().Be(standing);
        }

        foreach (var capability in Enum.GetValues<Capability>())
        {
            Capabilities.TryParse(capability.Spelled(), out var parsed).Should().BeTrue();
            parsed.Should().Be(capability);
        }

        Capabilities.TryParse("Disk", out var cased).Should().BeTrue("the file's words are matched without case");
        cased.Should().Be(Capability.Disk);
        Capabilities.TryParse("shell", out _).Should().BeFalse();
        Capabilities.AllSpelled.Should().Be("none, disk, web");
    }

    /// <summary>A broken table is a broken BUILD, refused whole and loudly — the role seed's rule.</summary>
    /// <remarks>
    /// Written against a hand-built table rather than by corrupting the embedded file, because the
    /// validation is the unit under test and the resource is not reachable without rebuilding the
    /// assembly. Every fixture starts from the WHOLE matrix — fifteen well-formed rows — and breaks one
    /// thing, so the refusal observed is for that thing and not for the fourteen pairs a smaller
    /// fixture would also be missing.
    /// </remarks>
    public sealed class ABrokenTable
    {
        private static RuntimeCapabilityRowSeed Row(string runtime, string capability, string standing = "confined", IReadOnlyList<string>? cells = null, string note = "n") =>
            new(runtime, capability, standing, new MeasuredWithSeed("cli", "1", "2026-10-01", "ref", cells ?? ["01a0f8c7-18e4-0000-0000-000000000000"], note));

        /// <summary>Every pair of the matrix, well formed — the fixture the broken ones are cut from.</summary>
        private static List<RuntimeCapabilityRowSeed> Whole() =>
            [.. RuntimeCapabilities.Runtimes.SelectMany(runtime => Capabilities.All.Select(capability => Row(runtime, capability.Spelled())))];

        private static Action Loading(IEnumerable<RuntimeCapabilityRowSeed> rows) =>
            () => RuntimeCapabilities.FromSeed(new RuntimeCapabilitySeed(["x"], [.. RuntimeCapabilities.Runtimes], ["none", "disk", "web"], [.. rows]));

        /// <summary>The whole matrix with ONE row replaced.</summary>
        private static IEnumerable<RuntimeCapabilityRowSeed> With(RuntimeCapabilityRowSeed replacement) =>
            Whole().Select(row => row.Runtime == replacement.Runtime && row.Capability == replacement.Capability ? replacement : row);

        [Fact]
        public void WithAPairMissing_IsRefused_NamingIt() =>
            Loading(Whole().Where(row => !(row.Runtime == "claude" && row.Capability == "disk")))
                .Should().Throw<InvalidOperationException>().WithMessage("*claude*disk*no row*",
                    "a pair with no row would be decided by nothing");

        [Fact]
        public void WithAPairTwice_IsRefused_NamingIt() =>
            Loading([.. Whole(), Row("claude", "none")]).Should().Throw<InvalidOperationException>()
                .WithMessage("*claude*none*twice*");

        [Fact]
        public void WithAnUnknownStanding_IsRefused_NamingIt() =>
            Loading(With(Row("claude", "none", standing: "sort of"))).Should().Throw<InvalidOperationException>()
                .WithMessage("*sort of*");

        [Fact]
        public void WithARowOutsideTheMatrix_IsRefused_NamingIt() =>
            Loading([.. Whole(), Row("gemini", "none")]).Should().Throw<InvalidOperationException>()
                .WithMessage("*gemini*");

        [Fact]
        public void WithACapabilityOutsideTheMatrix_IsRefused_NamingIt() =>
            Loading([.. Whole(), Row("claude", "shell")]).Should().Throw<InvalidOperationException>()
                .WithMessage("*shell*none, disk, web*");

        [Fact]
        public void WithNeitherACellNorANote_IsRefused_NamingTheRow() =>
            Loading(With(Row("claude", "none", cells: [], note: ""))).Should().Throw<InvalidOperationException>()
                .WithMessage("*claude*none*cell*");

        [Fact]
        public void ThatIsWellFormed_Loads_EveryPairOnce()
        {
            var table = RuntimeCapabilities.FromSeed(new RuntimeCapabilitySeed(["x"], [.. RuntimeCapabilities.Runtimes], ["none", "disk", "web"], Whole()));

            table.Rows.Should().HaveCount(15).And.OnlyContain(row => row.Standing == CapabilityStanding.Confined);
            table.Of("codex", Capability.Web).Should().NotBeNull();
            table.Of("gemini", Capability.Web).Should().BeNull("a runtime outside the matrix has no row to answer with");
        }
    }
}

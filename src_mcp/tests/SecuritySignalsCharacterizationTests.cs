using CoaiMcp.Core.Context;
using CoaiMcp.Core.Security;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Pins what <see cref="SecuritySignals.Classify(IReadOnlyList{FileDiff})"/> makes of one file on every arm —
/// withheld (binary, credential), oversized, prose, ordinary — so splitting the per-file classification to
/// complexity 4 cannot change which text reaches a reviewer or which signals route it.
/// </summary>
/// <remarks>
/// Written against the code BEFORE the split and observed green there
/// (todo/PLAN_security_lane_methods_within_complexity_4.md). Each render is the whole classified file: its
/// path, the text kept, every signal in order, and both flags.
/// </remarks>
public sealed class SecuritySignalsCharacterizationTests
{
    private static readonly string Oversized = "+database.Query(value);" + new string('a', SecuritySignals.MaxFileCharacters);
    private static readonly string AtTheLimit = ("+database.Query(value);" + new string('a', SecuritySignals.MaxFileCharacters))[..SecuritySignals.MaxFileCharacters];

    internal static readonly (string Name, FileDiff File)[] Inputs =
    [
        ("ordinary code", new("src/Query.cs", "+database.Query(value);")),
        ("a signal in the path alone", new("src/OrdersController.cs", "+return 42;")),
        ("no signal at all", new("src/Plain.cs", "+return 42;")),
        ("binary", new("src/Query.bin", "+database.Query(value);", IsBinary: true, BinaryBytes: 10)),
        ("binary and oversized", new("src/Query.bin", Oversized, IsBinary: true)),
        ("credential file", new("config/server.pem", "+database.Query(value); PRIVATE KEY")),
        ("credential file and oversized", new("config/server.pem", Oversized)),
        ("oversized code", new("src/Query.cs", Oversized)),
        ("code at the character limit", new("src/Query.cs", AtTheLimit)),
        ("prose", new("docs/sql.md", "+database.Query(value); [Authorize]")),
        ("prose and oversized", new("docs/sql.md", Oversized)),
        ("supporting code", new("tests/QueryTests.cs", "+database.Query(value);")),
        ("a removed line routes", new("src/Gate.cs", "-[Authorize]\n+// removed")),
        ("redacted before matching", new("src/Settings.cs", "+var password = \"hunter2hunter2\";")),
    ];

    private static readonly Dictionary<string, string> Expected = new()
    {
        ["a removed line routes"] = "src/Gate.cs | text=-[Authorize]\n+// removed | signals=[authz] | incomplete=False | supporting=False | binary=False",
        ["a signal in the path alone"] = "src/OrdersController.cs | text=+return 42; | signals=[authz,entry-point] | incomplete=False | supporting=False | binary=False",
        ["binary and oversized"] = "src/Query.bin | text= | signals=[] | incomplete=False | supporting=False | binary=True",
        ["binary"] = "src/Query.bin | text= | signals=[] | incomplete=False | supporting=False | binary=True",
        ["code at the character limit"] = "src/Query.cs | text=<262144 chars> | signals=[sql] | incomplete=False | supporting=False | binary=False",
        ["credential file and oversized"] = "config/server.pem | text= | signals=[] | incomplete=False | supporting=False | binary=False",
        ["credential file"] = "config/server.pem | text= | signals=[] | incomplete=False | supporting=False | binary=False",
        ["no signal at all"] = "src/Plain.cs | text=+return 42; | signals=[] | incomplete=False | supporting=False | binary=False",
        ["ordinary code"] = "src/Query.cs | text=+database.Query(value); | signals=[sql] | incomplete=False | supporting=False | binary=False",
        ["oversized code"] = "src/Query.cs | text= | signals=[] | incomplete=True | supporting=False | binary=False",
        ["prose and oversized"] = "docs/sql.md | text= | signals=[] | incomplete=True | supporting=True | binary=False",
        ["prose"] = "docs/sql.md | text=+database.Query(value); [Authorize] | signals=[] | incomplete=False | supporting=True | binary=False",
        ["redacted before matching"] = "src/Settings.cs | text=+var password = \"[redacted]\"; | signals=[secrets] | incomplete=False | supporting=False | binary=False",
        ["supporting code"] = "tests/QueryTests.cs | text=+database.Query(value); | signals=[sql] | incomplete=False | supporting=True | binary=False",
    };

    public static TheoryData<string> Names => [.. Inputs.Select(i => i.Name)];

    [Theory]
    [MemberData(nameof(Names))]
    public void Classifying_one_file_answers_exactly_what_it_answered_before_the_split(string name)
    {
        var actual = Render(SecuritySignals.Classify([Inputs.Single(i => i.Name == name).File]).Single());
        actual.Should().Be(Expected[name]);
    }

    [Fact]
    public void Every_input_has_an_expectation_and_every_expectation_an_input() =>
        Expected.Keys.Should().BeEquivalentTo(Inputs.Select(i => i.Name));

    [Fact]
    public void Files_are_capped_before_they_are_sorted()
    {
        FileDiff[] files = [.. Enumerable.Range(0, SecuritySignals.MaxFiles).Select(i => new FileDiff($"b{i:D4}.cs", "+return 1;")),
            new("a-first-by-name.cs", "+return 1;")];
        var classified = SecuritySignals.Classify(files);
        classified.Should().HaveCount(SecuritySignals.MaxFiles);
        classified.Select(f => f.Diff.Path).Should().NotContain("a-first-by-name.cs").And.BeInAscendingOrder(StringComparer.Ordinal);
    }

    internal static string Render(SecurityFile file) =>
        $"{file.Diff.Path} | text={(file.Diff.Text.Length > 200 ? $"<{file.Diff.Text.Length} chars>" : file.Diff.Text)} "
        + $"| signals=[{string.Join(",", file.Signals)}] | incomplete={file.DetectionIncomplete} | supporting={file.SupportingMaterial} "
        + $"| binary={file.Diff.IsBinary}";
}

using System.Text.Json;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The C# half of the shared URL vectors — the extension's suite asserts the same file.
/// </summary>
/// <remarks>
/// <para>The extension WRITES the Team-server token file and this binary's shim READS it, and each
/// derives the path independently: TypeScript's <c>URL</c> on one side, .NET's <c>Uri</c> on the
/// other. Isolated unit tests on each side cannot catch a divergence, because each side is
/// self-consistent — the failure only appears as a person signing in successfully and being told
/// they are not signed in one second later.</para>
/// <para>So the vectors live outside both implementations, in
/// <c>shared/team-server-url-vectors.json</c>, and both suites assert them. Raised independently by
/// two reviewers on the plan round of epic 3.</para>
/// </remarks>
public sealed class TeamServerUrlVectorTests
{
    private sealed record Vector(string Why, string[] Inputs, string Canonical, string Fingerprint);

    private static readonly Vector[] Vectors = Load();

    private static Vector[] Load()
    {
        // tests/bin/<cfg>/net10.0 → the repository root, then the shared folder both sides read.
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "shared", "team-server-url-vectors.json"));

        using var file = File.OpenRead(path);
        using var parsed = JsonDocument.Parse(file);

        return [.. parsed.RootElement.GetProperty("vectors").EnumerateArray().Select(v => new Vector(
            v.GetProperty("why").GetString() ?? "",
            [.. v.GetProperty("inputs").EnumerateArray().Select(i => i.GetString() ?? "")],
            v.GetProperty("canonical").GetString() ?? "",
            v.GetProperty("fingerprint").GetString() ?? ""))];
    }

    public static TheoryData<string, string, string> Cases()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var vector in Vectors)
        {
            foreach (var input in vector.Inputs)
            {
                data.Add(input, vector.Canonical, vector.Fingerprint);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EverySpellingCanonicalisesToTheSharedAnswer(string input, string canonical, string _) =>
        TeamServerAuth.Normalise(input).Should().Be(canonical);

    [Theory]
    [MemberData(nameof(Cases))]
    public void EverySpellingFingerprintsToTheSharedAnswer(string input, string _, string fingerprint) =>
        TeamServerAuth.Fingerprint(input).Should().Be(fingerprint);

    [Fact]
    public void TheTokenFileIsNamedByThatFingerprint()
    {
        // The whole point of the fixture: this is the path the extension must write to.
        var path = TeamServerAuth.TokenPath("/data", "https://coai.example.com/");

        Path.GetFileName(path).Should().Be("0ac377feb52b33a9.token");
        Path.GetFileName(Path.GetDirectoryName(path)!).Should().Be("servers");
    }

    [Fact]
    public void TheFixtureItselfIsNotEmpty() =>
        // A fixture that failed to load would make every test above pass vacuously.
        Vectors.Should().HaveCountGreaterThan(5).And.OnlyContain(v => v.Inputs.Length > 0);
}

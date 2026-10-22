using System.Text.Json;
using CoaiMcp.Runners.Kimi;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The shared Kimi identity cases of <c>shared/kimi-identity-vectors.json</c> (G22 of the code gate on E1): a process described by its <c>exe</c> link — or <c>argv[0]</c> when the exe is hidden — and its
/// <c>comm</c>, the name <see cref="LinuxProcessListing"/> reads for it, and whether <see cref="ForeignKimiWatch.IsKimi"/> calls it a Kimi. The probes' <c>isKimi</c> (<c>scripts/lib/proc.mjs</c>) is asserted
/// against the SAME file in <c>scripts/lib/proc.test.mjs</c>, so changing the product's identities forces the probe's, and a change to one side alone is red on that side alone.
/// </summary>
public sealed class KimiIdentityVectorTests
{
    private const int Shim = 100;

    private const int Candidate = 5000;

    private static IReadOnlyList<IdentityVector> Vectors { get; } = Load();

    public static TheoryData<string> Ids { get; } = [.. Vectors.Select(vector => vector.Id)];

    [Fact]
    public void TheVectorFile_IsNotEmpty_AndItsIdsAreUnique()
    {
        Vectors.Count.Should().BeGreaterThanOrEqualTo(12, "a theory over nothing proves nothing");
        Vectors.Select(vector => vector.Id).Distinct().Should().HaveCount(Vectors.Count);
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void TheListingReadsTheVectorsName_AndIsKimiAnswersAsTheFileSays(string id)
    {
        var vector = Vectors.Single(candidate => candidate.Id == id);
        var machine = new FakeProcReads().Running(1, 0, "/sbin/init", "init").Running(Shim, 1, "/usr/local/bin/coai-mcp", "coai-mcp");
        _ = vector.Exe is null ? machine.Hidden(Candidate, 1, vector.Argv, vector.Comm) : machine.Running(Candidate, 1, vector.Exe, vector.Comm);

        var listing = new LinuxProcessListing(machine, Shim).Snapshot();

        var entry = listing.Should().BeOfType<ProcessListing.Listed>($"{id} made the listing unreadable").Subject.Processes.Single(process => process.Pid == Candidate);
        entry.Name.Should().Be(vector.Name, $"the name the listing reads for {id}");
        ForeignKimiWatch.IsKimi(entry).Should().Be(vector.IsKimi, $"{id}: the product's IsKimi disagrees with shared/kimi-identity-vectors.json (name {entry.Name}, comm {entry.Comm})");
    }

    private static List<IdentityVector> Load()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "shared", "kimi-identity-vectors.json"));
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));

        return [.. document.RootElement.GetProperty("vectors").EnumerateArray().Select(vector => new IdentityVector(
            vector.GetProperty("id").GetString()!,
            vector.GetProperty("exe").GetString(),
            [.. vector.GetProperty("argv").EnumerateArray().Select(argument => argument.GetString()!)],
            vector.GetProperty("comm").GetString()!,
            vector.GetProperty("name").GetString()!,
            vector.GetProperty("isKimi").GetBoolean()))];
    }

    private sealed record IdentityVector(string Id, string? Exe, string[] Argv, string Comm, string Name, bool IsKimi);
}

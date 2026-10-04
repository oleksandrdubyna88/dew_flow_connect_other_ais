using System.Text.Json;
using CoaiMcp.Core.Security;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What a security prompt's override text counts as — the C# half of <c>shared/security-prompt-text-vectors.json</c>;
/// <c>securityPromptFiles.test.ts</c> is the other (research/PLAN_the_security_tab_reads_at_a_glance.md, epic 2).
/// </summary>
/// <remarks>
/// The server decides from this whether a pairing can run, and the Security lane tab draws each card from the
/// extension's copy of the same rule. Two self-consistent copies cannot notice that they disagree — a card drawn
/// green for a pair the server drops — so both answer one set of vectors, read from the checked-in file.
/// </remarks>
public sealed class SecurityPromptTextVectorsTests
{
    private sealed record Vector(string Name, string? Text, string State);

    private static readonly JsonDocument File = JsonDocument.Parse(System.IO.File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "shared", "security-prompt-text-vectors.json"))));

    public static TheoryData<string> Names => [.. Vectors().Select(v => v.Name)];

    private static IEnumerable<Vector> Vectors() => File.RootElement.GetProperty("vectors").EnumerateArray().Select(v =>
        new Vector(v.GetProperty("name").GetString()!, TextOf(v), v.GetProperty("state").GetString()!));

    private static string? TextOf(JsonElement v) => v.TryGetProperty("repeat", out var repeat)
        ? string.Concat(Enumerable.Repeat(repeat.GetString()!, v.GetProperty("count").GetInt32()))
        : v.GetProperty("text").GetString();

    [Theory]
    [MemberData(nameof(Names))]
    public void The_server_classifies_each_shared_vector_as_the_file_says(string name)
    {
        var vector = Vectors().Single(v => v.Name == name);
        SecurityPromptText.Classify(vector.Text).ToString().ToLowerInvariant().Should().Be(vector.State, name);
    }

    [Fact]
    public void The_size_limit_is_the_one_the_shared_file_declares() =>
        SecurityContext.MaxPromptBytes.Should().Be(File.RootElement.GetProperty("maxBytes").GetInt32());
}

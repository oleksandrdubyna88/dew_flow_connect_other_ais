using System.Text.Json;
using Xunit;
using CoaiMcp.Core.Findings;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// The schema is handed to a VENDOR, and that vendor has rules. OpenAI's structured outputs (what
/// `codex exec --output-schema` feeds) require, for every object in the schema:
/// <c>additionalProperties: false</c>, and <c>required</c> listing EVERY key in
/// <c>properties</c> — an optional field is expressed as a nullable type, not as an absent
/// requirement.
/// </summary>
/// <remarks>
/// Written after the real run of 2026-08-31, where Codex answered
/// <c>400 invalid_json_schema: 'required' … Missing 'file'</c> on every single reviewer. The suite
/// had asserted the schema's CONTENT (that it names every severity and field) and nothing about
/// whether a vendor would accept it.
/// </remarks>
public sealed class FindingSchemaTests
{
    [Fact]
    public void EveryObject_ForbidsAdditionalProperties()
    {
        using var doc = JsonDocument.Parse(FindingSchema.Json);

        foreach (var (path, obj) in Objects(doc.RootElement, "$"))
        {
            obj.TryGetProperty("additionalProperties", out var additional).Should().BeTrue($"{path} must say so");
            additional.GetBoolean().Should().BeFalse($"{path} must forbid extras");
        }
    }

    [Fact]
    public void EveryObject_RequiresEveryPropertyItDeclares()
    {
        using var doc = JsonDocument.Parse(FindingSchema.Json);

        foreach (var (path, obj) in Objects(doc.RootElement, "$"))
        {
            var declared = obj.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();
            var required = obj.TryGetProperty("required", out var r)
                ? r.EnumerateArray().Select(x => x.GetString()).ToList()
                : [];

            required.Should().BeEquivalentTo(declared,
                $"{path}: OpenAI requires every declared property to be required — optionality is a nullable TYPE");
        }
    }

    [Fact]
    public void TheOptionalFields_AreNullableTypes_NotAbsentRequirements()
    {
        using var doc = JsonDocument.Parse(FindingSchema.Json);
        var properties = doc.RootElement
            .GetProperty("properties").GetProperty("findings")
            .GetProperty("items").GetProperty("properties");

        foreach (var name in (string[])["file", "line"])
        {
            var types = properties.GetProperty(name).GetProperty("type");
            types.ValueKind.Should().Be(JsonValueKind.Array, $"{name} is optional, so its TYPE carries null");
            types.EnumerateArray().Select(t => t.GetString()).Should().Contain("null");
        }
    }

    [Fact]
    public void ANullFileAndLine_ParseBackAsARepoLevelFinding()
    {
        // The other half of nullable-instead-of-absent: what the vendor may now legally send.
        var outcome = ReviewParser.Parse(
            """
            {"findings": [{"severity": "major", "category": "architecture", "file": null, "line": null,
              "title": "the plan has no rollback", "why": "w", "fix": "f"}]}
            """,
            "codex");

        var finding = outcome.Should().BeOfType<ParseOutcome.Success>().Subject.Review.Findings.Single();
        finding.File.Should().BeEmpty();
        finding.Line.Should().Be(0);
    }

    /// <summary>Every schema node that declares `properties` — the ones the vendor's rules apply to.</summary>
    private static IEnumerable<(string Path, JsonElement Object)> Objects(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (element.TryGetProperty("properties", out _))
        {
            yield return (path, element);
        }

        foreach (var property in element.EnumerateObject())
        {
            foreach (var found in Objects(property.Value, $"{path}.{property.Name}"))
            {
                yield return found;
            }
        }
    }
}

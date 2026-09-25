using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Findings;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The feature reviewer answers in a shape of its own — and no other stage's shape moved a byte.
/// </summary>
/// <remarks>
/// <para>A feature reviewer can ask for code (<c>sourceRequests</c>, plan §4.9). Every other stage's
/// reviewer is handed <see cref="FindingSchema.Json"/>, and offering them a field nobody would serve is
/// how a code reviewer starts asking for files instead of reviewing the diff it was given. So the
/// feature schema is DERIVED from the finding schema by adding one property, and the finding schema is
/// pinned by hash: a change to it is a change to every vendor's contract and must be deliberate.</para>
/// </remarks>
public sealed class TheFeatureSchemaTests
{
    /// <summary>The finding schema as it shipped before S1.3 — its SHA-256 over the UTF-8 bytes.</summary>
    /// <remarks>
    /// Computed from <c>git show HEAD:src_mcp/core/Findings/FindingSchema.cs</c> BEFORE this story
    /// touched the file (1367 characters). <c>*.cs</c> is checked out LF everywhere
    /// (<c>.gitattributes</c>), so the literal is the same bytes on every platform.
    /// </remarks>
    private const string ShippedJsonSha256 = "be80578396dc74f811bba59c809cc7aae82127a3b2ae6cddfa9bdae09013f459";

    [Fact]
    public void TheFindingSchema_IsByteIdenticalToWhatShipped()
    {
        var bytes = Encoding.UTF8.GetBytes(FindingSchema.Json);

        Convert.ToHexStringLower(SHA256.HashData(bytes)).Should().Be(
            ShippedJsonSha256,
            "every plan, code and document reviewer is handed this schema; S1.3 must not have moved it");
        FindingSchema.Json.Should().NotContain("sourceRequests", "a code reviewer is never offered a field nobody serves");
    }

    [Fact]
    public void TheFeatureSchema_IsTheFindingSchemaPlusSourceRequests_AndNothingElse()
    {
        var feature = JsonNode.Parse(FindingSchema.FeatureJson)!.AsObject();
        var finding = JsonNode.Parse(FindingSchema.Json)!.AsObject();

        feature["properties"]!.AsObject().Remove("sourceRequests").Should().BeTrue("the feature schema declares it");
        var required = feature["required"]!.AsArray();
        required.Select(node => node!.GetValue<string>()).Should().Contain("sourceRequests");
        required.Remove(required.First(node => node!.GetValue<string>() == "sourceRequests"));

        JsonNode.DeepEquals(feature, finding).Should().BeTrue(
            "the feature schema is DERIVED — a second copy of the findings part would drift from the first");
    }

    /// <summary>
    /// The derivation reads the base schema's STRUCTURE, not its formatting.
    /// </summary>
    /// <remarks>
    /// Epic 1's code round (local, gemini): the first version found two literal anchors in the base text,
    /// so reindenting or minifying the finding schema — a change that means nothing to any vendor — threw
    /// at type initialisation and took the whole server down with it.
    /// </remarks>
    [Fact]
    public void TheFeatureSchema_DerivesFromAReformattedBase_TheSameWay()
    {
        var minified = JsonNode.Parse(FindingSchema.Json)!.ToJsonString();

        var derived = JsonNode.Parse(FindingSchema.WithSourceRequests(minified))!.AsObject();

        derived["required"]!.AsArray().Select(node => node!.GetValue<string>())
            .Should().Equal("findings", "notes", "sourceRequests");
        JsonNode.DeepEquals(derived, JsonNode.Parse(FindingSchema.FeatureJson)).Should().BeTrue(
            "the same base, however it is laid out, must derive the same feature schema");
    }

    [Fact]
    public void ASourceRequest_IsTheShapeThePlanNames()
    {
        using var doc = JsonDocument.Parse(FindingSchema.FeatureJson);
        var requests = doc.RootElement.GetProperty("properties").GetProperty("sourceRequests");

        Types(requests).Should().BeEquivalentTo(["array", "null"], "absent requests are a null, never a missing key");
        var item = requests.GetProperty("items");
        item.GetProperty("required").EnumerateArray().Select(x => x.GetString())
            .Should().Equal("file", "symbol", "startLine", "endLine", "why");

        var properties = item.GetProperty("properties");
        Types(properties.GetProperty("file")).Should().Equal("string");
        Types(properties.GetProperty("symbol")).Should().BeEquivalentTo(["string", "null"]);
        Types(properties.GetProperty("startLine")).Should().BeEquivalentTo(["integer", "null"]);
        Types(properties.GetProperty("endLine")).Should().BeEquivalentTo(["integer", "null"]);
        Types(properties.GetProperty("why")).Should().Equal("string");
    }

    private static IReadOnlyList<string?> Types(JsonElement property)
    {
        var type = property.GetProperty("type");

        return type.ValueKind == JsonValueKind.Array ? [.. type.EnumerateArray().Select(t => t.GetString())] : [type.GetString()];
    }
}

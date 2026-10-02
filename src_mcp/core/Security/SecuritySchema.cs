using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Findings;

namespace CoaiMcp.Core.Security;

/// <summary>The ordinary schema stays unchanged; only security runs request reproduction.</summary>
public static class SecuritySchema
{
    private const string Properties = "properties";
    private const string Required = "required";
    private const string StringType = "string";
    public static readonly string Json = Create();

    private static string Create()
    {
        var schema = JsonNode.Parse(FindingSchema.Json)!;
        schema[Properties]!.AsObject().Remove("notes");
        schema[Properties]!["status"] = new JsonObject
        { ["type"] = StringType, ["enum"] = new JsonArray("SECURE", "FINDINGS") };
        schema[Required] = new JsonArray([.. schema[Properties]!.AsObject()
            .Select(p => (JsonNode)JsonValue.Create(p.Key))]);
        var item = schema[Properties]!["findings"]!["items"]!;
        item[Properties]!["severity"] = new JsonObject
        { ["type"] = StringType, ["enum"] = new JsonArray("blocking", "major", "minor") };
        foreach (var name in new[] { "trigger", "mechanism", "consequence" })
        {
            item[Properties]![name] = new JsonObject
            { ["type"] = StringType, ["minLength"] = 1, ["maxLength"] = Reproduction.MaxCharacters };
            item[Required]!.AsArray().Add((JsonNode)JsonValue.Create(name));
        }
        var properties = new JsonObject();
        foreach (var name in new[] { "preconditions", "steps", "expected", "actual" })
            properties[name] = new JsonObject { ["type"] = StringType };
        item[Properties]!["reproduction"] = new JsonObject
        {
            ["type"] = new JsonArray("object", "null"),
            ["additionalProperties"] = false,
            [Required] = new JsonArray("preconditions", "steps", "expected", "actual"),
            [Properties] = properties,
        };
        item[Required]!.AsArray().Add((JsonNode)JsonValue.Create("reproduction"));
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) schema.WriteTo(writer);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}

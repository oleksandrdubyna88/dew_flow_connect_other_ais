using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Findings;

namespace CoaiMcp.Core.Security;

/// <summary>The ordinary schema stays unchanged; only security runs request reproduction.</summary>
public static class SecuritySchema
{
    public static readonly string Json = Create();

    private static string Create()
    {
        var schema = JsonNode.Parse(FindingSchema.Json)!;
        schema["properties"]!.AsObject().Remove("notes");
        schema["properties"]!["status"] = new JsonObject
        { ["type"] = "string", ["enum"] = new JsonArray("SECURE", "FINDINGS") };
        schema["required"] = new JsonArray([.. schema["properties"]!.AsObject()
            .Select(p => (JsonNode)JsonValue.Create(p.Key))]);
        var item = schema["properties"]!["findings"]!["items"]!;
        item["properties"]!["severity"] = new JsonObject
        { ["type"] = "string", ["enum"] = new JsonArray("blocking", "major", "minor") };
        foreach (var name in new[] { "trigger", "mechanism", "consequence" })
        {
            item["properties"]![name] = new JsonObject
            { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = Reproduction.MaxCharacters };
            item["required"]!.AsArray().Add((JsonNode)JsonValue.Create(name));
        }
        var properties = new JsonObject();
        foreach (var name in new[] { "preconditions", "steps", "expected", "actual" })
            properties[name] = new JsonObject { ["type"] = "string" };
        item["properties"]!["reproduction"] = new JsonObject
        {
            ["type"] = new JsonArray("object", "null"),
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("preconditions", "steps", "expected", "actual"),
            ["properties"] = properties,
        };
        item["required"]!.AsArray().Add((JsonNode)JsonValue.Create("reproduction"));
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) schema.WriteTo(writer);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}

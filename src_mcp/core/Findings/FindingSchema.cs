using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CoaiMcp.Core.Findings;

/// <summary>
/// The one copy of the finding schema. Codex takes it via <c>--output-schema</c>; Gemini gets it
/// pasted into the prompt. A test holds it and the C# shape together, because two copies of a
/// contract drift and nothing notices.
/// </summary>
/// <remarks>
/// <para><b>It must satisfy OpenAI's structured-output rules, not merely be valid JSON Schema.</b>
/// Every object needs <c>additionalProperties: false</c> AND a <c>required</c> array naming every
/// key it declares — optionality is expressed as a nullable TYPE, never as an absent requirement.
/// Learned from a 400 on every reviewer in the real run of 2026-08-31
/// (<c>invalid_json_schema … Missing 'file'</c>); `FindingSchemaTests` now holds both rules.</para>
/// </remarks>
public static class FindingSchema
{
    public const string Json = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["findings", "notes"],
          "properties": {
            "notes": { "type": ["string", "null"], "description": "Prose about the whole document when the prompt asks for it; null otherwise. Never a finding: it gates nothing." },
            "findings": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["severity", "category", "file", "line", "title", "why", "fix"],
                "properties": {
                  "severity": { "type": "string", "enum": ["blocking", "major", "minor", "nit"] },
                  "category": { "type": "string", "enum": ["architecture", "security", "reliability", "performance", "ux", "convention", "clarity", "completeness", "consistency", "feasibility"] },
                  "file": { "type": ["string", "null"], "description": "Repo-relative path; null for a repo-level or plan-level finding" },
                  "line": { "type": ["integer", "null"], "description": "1-based; null when the finding names no line" },
                  "title": { "type": "string", "description": "One sentence: the defect itself" },
                  "why": { "type": "string", "description": "What breaks, concretely - inputs/state to wrong outcome" },
                  "fix": { "type": "string", "description": "The smallest change that removes it" }
                }
              }
            }
          }
        }
        """;

    /// <summary>
    /// The feature reviewer's schema: <see cref="Json"/> plus <c>sourceRequests</c> — DERIVED, never a
    /// second copy.
    /// </summary>
    /// <remarks>
    /// <para>A feature reviewer has an outline and no code, and asks for code by name (plan §4.9). Only
    /// that stage is offered the field: <see cref="Json"/> stays byte-identical, pinned by hash in
    /// <c>TheFeatureSchemaTests</c>, so no plan, code or document reviewer is ever handed a request it
    /// would make and nobody would serve.</para>
    /// <para>Derived by adding one property and one <c>required</c> name to the finding schema's
    /// PARSED structure, so the findings part cannot drift — there is only one copy of it — and a
    /// reformatted base derives the same schema (the first version searched the text for two literal
    /// anchors and threw at type initialisation on a mere reindent; epic 1's code round). The same OpenAI strict
    /// rules hold — <c>additionalProperties: false</c> on every object, every declared key required,
    /// optionality as a nullable TYPE — and <c>FindingSchemaTests</c> runs them over both shapes.</para>
    /// </remarks>
    public static readonly string FeatureJson = WithSourceRequests(Json);

    /// <summary>The <c>sourceRequests</c> property's own schema.</summary>
    private const string SourceRequestsProperty = """
        {
          "type": ["array", "null"],
          "description": "Code you need to see before you can judge, at most a few per answer; null when you need nothing. Only files in the repository.",
          "items": {
            "type": "object",
            "additionalProperties": false,
            "required": ["file", "symbol", "startLine", "endLine", "why"],
            "properties": {
              "file": { "type": "string", "description": "Repo-relative path, as the outline names it" },
              "symbol": { "type": ["string", "null"], "description": "A declaration's name from the outline; null to ask by lines or for the whole file" },
              "startLine": { "type": ["integer", "null"], "description": "1-based; null unless asking for a span" },
              "endLine": { "type": ["integer", "null"], "description": "1-based and inclusive; null unless asking for a span" },
              "why": { "type": "string", "description": "What you will check in it" }
            }
          }
        }
        """;

    /// <summary>The finding schema <paramref name="json"/> with <c>sourceRequests</c> declared and required.</summary>
    /// <remarks>
    /// Reads the base's STRUCTURE through <see cref="JsonNode"/>, which Native AOT carries without a
    /// serializer context, and writes it indented through a <see cref="Utf8JsonWriter"/> — no
    /// <c>JsonSerializerOptions</c>, so no reflection. A base without a root <c>required</c> array or
    /// <c>properties</c> object is a build defect and throws, which every schema test reaches first.
    /// </remarks>
    internal static string WithSourceRequests(string json)
    {
        var schema = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("the finding schema is not a JSON object");
        var required = schema["required"] as JsonArray
            ?? throw new InvalidOperationException("the finding schema has no root `required` array to derive the feature schema from");
        var properties = schema["properties"] as JsonObject
            ?? throw new InvalidOperationException("the finding schema has no root `properties` object to derive the feature schema from");

        required.Add((JsonNode)JsonValue.Create("sourceRequests"));
        properties.Add("sourceRequests", JsonNode.Parse(SourceRequestsProperty));

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            schema.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}

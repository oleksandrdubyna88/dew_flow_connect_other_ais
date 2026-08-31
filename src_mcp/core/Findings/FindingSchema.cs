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
          "required": ["findings"],
          "properties": {
            "findings": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["severity", "category", "file", "line", "title", "why", "fix"],
                "properties": {
                  "severity": { "type": "string", "enum": ["blocking", "major", "minor", "nit"] },
                  "category": { "type": "string", "enum": ["architecture", "security", "reliability", "performance", "ux", "convention"] },
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
}

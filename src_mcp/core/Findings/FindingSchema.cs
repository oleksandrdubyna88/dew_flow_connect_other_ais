namespace CoaiMcp.Core.Findings;

/// <summary>
/// The one copy of the finding schema. Codex takes it via <c>--output-schema</c>; Gemini gets it
/// pasted into the prompt. A test holds it and the C# shape together, because two copies of a
/// contract drift and nothing notices.
/// </summary>
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
                "required": ["severity", "category", "title", "why", "fix"],
                "properties": {
                  "severity": { "type": "string", "enum": ["blocking", "major", "minor", "nit"] },
                  "category": { "type": "string", "enum": ["architecture", "security", "reliability", "performance", "ux", "convention"] },
                  "file": { "type": "string", "description": "Repo-relative path; omit for a repo-level or plan-level finding" },
                  "line": { "type": "integer", "description": "1-based; omit when the finding names no line" },
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

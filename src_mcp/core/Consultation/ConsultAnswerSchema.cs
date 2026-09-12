namespace CoaiMcp.Core.Consultation;

/// <summary>
/// The shape a consultant answers in when the route it takes insists on a schema.
/// </summary>
/// <remarks>
/// <para>The three vendor CLIs need none — measured 2026-09-12: each answers prose with no schema
/// flag at all, which is what a consultation wants. The LOCAL engine is the exception and refuses a
/// request without one, deliberately: an unconstrained request there is answered with an invented
/// shape after a full generation has been paid for, which the reviewer path learned the expensive
/// way.</para>
/// <para>So this is the smallest schema that means "just answer": one required string.
/// <c>LocalAsk.Bounded</c> passes a schema that is not the finding schema through unchanged, so
/// nothing has to be taught about this one.</para>
/// </remarks>
public static class ConsultAnswerSchema
{
    public const string Name = "consult-answer-schema.json";

    public const string Json = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["answer"],
          "properties": {
            "answer": {
              "type": "string",
              "description": "Your advice to the AI that is stuck: the hypothesis, how to check it, and the smallest next step."
            }
          }
        }
        """;

    /// <summary>The file, written where the launch can name it. Idempotent, and never rewritten in place.</summary>
    public static string EnsureFile(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Name);
        if (!File.Exists(path) || File.ReadAllText(path) != Json)
        {
            var temp = path + ".tmp";
            File.WriteAllText(temp, Json);
            File.Move(temp, path, overwrite: true);
        }

        return path;
    }
}

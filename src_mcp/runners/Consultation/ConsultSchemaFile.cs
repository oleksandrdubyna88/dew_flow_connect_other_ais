using CoaiMcp.Core.Consultation;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// Puts <see cref="ConsultAnswerSchema"/> on disk, once, where a launch can name it.
/// </summary>
/// <remarks>
/// <para>Called when the service is built and never from an adapter's <c>Build</c>: a pure builder is
/// what makes every flag a unit test, and a request that provisions a file cannot be constructed to
/// be looked at. The path then travels on <see cref="ConsultantLaunch"/> like any other input.</para>
/// <para>The temp name is UNIQUE per writer. A fixed one let two concurrent launches both see the
/// schema as absent, both write <c>…json.tmp</c>, and the second <c>Move</c> fail — so one
/// consultation never launched, for a file whose content is a constant. (codex and gemini, story 2's
/// code round.)</para>
/// </remarks>
public static class ConsultSchemaFile
{
    /// <summary>The schema's path, written if it is missing or stale. Never throws for a caller that cannot write.</summary>
    public static string Ensure(string directory)
    {
        var path = Path.Combine(directory, ConsultAnswerSchema.Name);
        try
        {
            Directory.CreateDirectory(directory);
            if (File.Exists(path) && File.ReadAllText(path) == ConsultAnswerSchema.Json)
            {
                return path;
            }

            var temp = $"{path}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temp, ConsultAnswerSchema.Json);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The path is still returned: the launch that needs it will fail with the shim's own
            // sentence about a missing schema, which names the file — better than a failure here,
            // where no consultation exists yet to attach it to.
        }

        return path;
    }
}

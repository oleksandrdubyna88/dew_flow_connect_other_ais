using CoaiMcp.Core.Consultation;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// Puts <see cref="ConsultAnswerSchema"/> on disk, once, where a launch can name it.
/// </summary>
/// <remarks>
/// <para>Called when the service is built and never from an adapter's <c>Build</c>: a builder that
/// provisions a file cannot be constructed to be looked at. The path then travels on
/// <see cref="ConsultantLaunch"/> like any other input.</para>
/// <para>The temp name is UNIQUE per writer. A fixed one let two concurrent launches both see the
/// schema as absent, both write <c>…json.tmp</c>, and the second <c>Move</c> fail — so one
/// consultation never launched, for a file whose content is a constant.</para>
/// </remarks>
public static class ConsultSchemaFile
{
    /// <summary>What provisioning the schema produced: its path, and the reason it is not there.</summary>
    /// <param name="Problem">Empty when the file is on disk; a sentence naming the path and the cause otherwise.</param>
    public sealed record Provisioned(string Path, string Problem)
    {
        public bool Ready => Problem.Length == 0;
    }

    /// <summary>
    /// The schema's path, written if it is missing or stale — and what went wrong if it is not there.
    /// </summary>
    /// <remarks>
    /// It does not throw: this runs while the service is being built, for a file only ONE of the four
    /// routes needs, and a consultation on codex must not be prevented by a directory the local
    /// engine would have used. But it does not stay SILENT either — the first version swallowed the
    /// failure and returned a path to a file that was not there, so a read-only data directory
    /// surfaced minutes later as a child process complaining about a missing schema, nowhere near the
    /// permission that caused it. The caller logs this, and the local route refuses by name.
    /// (codex and gemini, story 2's second code round.)
    /// </remarks>
    public static Provisioned Ensure(string directory)
    {
        var path = Path.Combine(directory, ConsultAnswerSchema.Name);
        try
        {
            Directory.CreateDirectory(directory);
            if (File.Exists(path) && File.ReadAllText(path) == ConsultAnswerSchema.Json)
            {
                return new Provisioned(path, string.Empty);
            }

            var temp = $"{path}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temp, ConsultAnswerSchema.Json);
            File.Move(temp, path, overwrite: true);

            return new Provisioned(path, string.Empty);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Provisioned(
                path,
                $"the consultant's answer schema could not be written to {path}: {e.Message}. "
                + "A local-engine consultation needs it and will be refused; every other route is unaffected.");
        }
    }
}

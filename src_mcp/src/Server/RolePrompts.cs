using System.Reflection;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>
/// Role prompts: a shipped default EMBEDDED in the binary, an override layer on disk, a restore —
/// the prompt-catalog pattern, so editing a prompt is not a rebuild and restoring one is not an
/// archaeology dig.
/// </summary>
/// <remarks>
/// <para><b>Embedded, not copied beside the executable.</b> The release asset carries exactly one
/// file, and the first real run (2026-08-31) died on every `review_plan` because the prompts were
/// content files that the release never packaged — invisible in tests, where a project reference
/// copies them into the output. A default that can go missing is not a default.</para>
/// <para>Overrides live under the data dir and win while they exist; restore is deleting the
/// override. The server only READS here — editing arrives with the extension.</para>
/// </remarks>
public sealed class RolePrompts(string dataDir)
{
    private string OverrideDir => Path.Combine(dataDir, "prompts");

    /// <summary>The text of one prompt by its id — override first, then the shipped default.</summary>
    /// <remarks>
    /// Keyed by PROMPT id rather than by role since 2026-09-12. A role used to have exactly one
    /// prompt whose file name this class knew by a switch statement; a role a person defines has
    /// prompts this build has never heard of, and the file it wants is named by the prompt, which
    /// is what <see cref="ForChoice"/> always did underneath.
    /// </remarks>
    public string For(string promptId) => Text(promptId);

    /// <summary>
    /// One prompt from the catalog, override-first — the same layering as the role default,
    /// because a narrow lens somebody edited must survive the next release too.
    /// </summary>
    public string ForChoice(PromptChoice choice) => Text(choice.Id);

    /// <summary>
    /// Whether this prompt has any text at all — an override on disk, or a shipped default.
    /// </summary>
    /// <remarks>
    /// Only a prompt a PERSON added can answer false: a shipped one's text is embedded in the
    /// binary, and a build missing one is a broken build that <see cref="Embedded"/> refuses loudly.
    /// It is a question rather than a nullable read because the caller's answer is a sentence — the
    /// round says which role it could not ask and why — not a fallback.
    /// </remarks>
    public bool Has(PromptChoice choice) =>
        choice.BuiltIn || File.Exists(Path.Combine(OverrideDir, FileOf(choice.Id)));

    /// <summary>
    /// One prompt's text: the override file if there is one, else what the binary ships.
    /// </summary>
    /// <remarks>
    /// The id becomes a FILE NAME, and since roles became data it is a person's text rather than a
    /// compiled constant. Composition refuses an id that is not <c>^[a-z0-9][a-z0-9-]*$</c> and
    /// refuses the basenames Windows reserves for devices, so nothing shaped like a path should
    /// arrive — this is the second lock, in the place that actually opens the file. Raised on the
    /// code round of the story that removed the enum, alongside the same guard in the adapters.
    /// </remarks>
    private string Text(string promptId)
    {
        var file = FileOf(promptId);

        return File.Exists(Path.Combine(OverrideDir, file)) ? File.ReadAllText(Path.Combine(OverrideDir, file)) : Embedded(file);
    }

    /// <summary>
    /// The one place a prompt id becomes a file name — read, write and restore alike.
    /// </summary>
    /// <remarks>
    /// It guarded the READ only, which was the seam codex found on story B1's second code round: an
    /// editor calling <c>Override("../../settings", text)</c> would have written outside the prompts
    /// directory while <c>For</c> sanitised the same id. Composition refuses an id that is not
    /// <c>^[a-z0-9][a-z0-9-]*$</c>, so nothing shaped like a path should arrive — but the prompt
    /// store is about to gain a caller in the extension, and a boundary only half of a type honours
    /// is not a boundary.
    /// </remarks>
    private static string FileOf(string promptId) => $"{FileName.Safe(promptId ?? string.Empty)}.md";

    /// <summary>The text compiled into this binary. Static: it depends on nothing on disk.</summary>
    public static string ShippedDefaultFor(string promptId) => Embedded(FileOf(promptId));

    private static string Embedded(string file)
    {
        var name = $"CoaiMcp.prompts.{file}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(
                $"the prompt '{name}' is not embedded in this build — check the EmbeddedResource item in CoaiMcp.csproj");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void Override(string promptId, string text)
    {
        Directory.CreateDirectory(OverrideDir);
        File.WriteAllText(Path.Combine(OverrideDir, FileOf(promptId)), text);
    }

    public void RestoreDefault(string promptId)
    {
        var overridePath = Path.Combine(OverrideDir, FileOf(promptId));
        if (File.Exists(overridePath))
        {
            File.Delete(overridePath);
        }
    }
}

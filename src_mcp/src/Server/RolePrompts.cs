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
        var file = $"{FileName.Safe(promptId)}.md";
        var overridePath = Path.Combine(OverrideDir, file);

        return File.Exists(overridePath) ? File.ReadAllText(overridePath) : Embedded(file);
    }

    /// <summary>The text compiled into this binary. Static: it depends on nothing on disk.</summary>
    public static string ShippedDefaultFor(string promptId) => Embedded($"{promptId}.md");

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
        File.WriteAllText(Path.Combine(OverrideDir, $"{promptId}.md"), text);
    }

    public void RestoreDefault(string promptId)
    {
        var overridePath = Path.Combine(OverrideDir, $"{promptId}.md");
        if (File.Exists(overridePath))
        {
            File.Delete(overridePath);
        }
    }
}

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
    /// <para>Only a prompt a PERSON added can answer false: a shipped one's text is embedded in the
    /// binary, and a build missing one is a broken build that <see cref="Embedded"/> refuses loudly.
    /// It is a question rather than a nullable read because the caller's answer is a sentence — the
    /// round says which role it could not ask and why — not a fallback.</para>
    /// <para><b>Text, not a file.</b> This asked <c>File.Exists</c>, which made an empty file a
    /// prompt: somebody who creates the file before writing it — the ordinary order of doing that —
    /// got a reviewer launched with nothing to review by, instead of the sentence saying their role
    /// could not run. The predicate has to answer the same question the read does, or the round
    /// promises text it will not have. (codex, three times on story B2's code round.)</para>
    /// </remarks>
    public bool Has(PromptChoice choice) => choice.BuiltIn || !string.IsNullOrWhiteSpace(Override(choice.Id));

    /// <summary>The override file's text, or empty when there is no override.</summary>
    /// <remarks>
    /// Empty covers both "no file" and "a file that says nothing", which is what the callers mean by
    /// it: the layer below is the shipped default, and nothing is nothing either way.
    /// </remarks>
    private string Override(string promptId)
    {
        var path = Path.Combine(OverrideDir, FileOf(promptId));

        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    /// <summary>Where a person writes the text for a prompt this build does not ship.</summary>
    /// <remarks>
    /// The round names it when a role has no text, because the DIRECTORY leaves somebody guessing
    /// both the file name and the extension — and the id is not always the file name, since it is
    /// sanitised on the way. (gemini, story B2's code round.)
    /// </remarks>
    public string FileToWrite(string promptId) => Path.Combine(OverrideDir, FileOf(promptId));

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
    /// <remarks>
    /// An override that says NOTHING is not an override. It used to win on existing alone, so a
    /// half-written edit to a shipped prompt handed the reviewer an empty file where the binary had
    /// the real text — and for a shipped prompt the way to go back is deleting the file, which this
    /// now treats an empty one as. (Found beside the same defect in <see cref="Has"/>.)
    /// </remarks>
    private string Text(string promptId) =>
        Override(promptId) is var text && !string.IsNullOrWhiteSpace(text) ? text : Embedded(FileOf(promptId));

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

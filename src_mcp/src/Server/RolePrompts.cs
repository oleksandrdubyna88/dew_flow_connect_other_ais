using System.Reflection;
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

    public string For(ReviewRole role)
    {
        var overridePath = Path.Combine(OverrideDir, FileFor(role));
        return File.Exists(overridePath) ? File.ReadAllText(overridePath) : ShippedDefaultFor(role);
    }

    /// <summary>The text compiled into this binary. Static: it depends on nothing on disk.</summary>
    public static string ShippedDefaultFor(ReviewRole role)
    {
        var name = $"CoaiMcp.prompts.{FileFor(role)}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(
                $"the prompt '{name}' is not embedded in this build — check the EmbeddedResource item in CoaiMcp.csproj");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void Override(ReviewRole role, string text)
    {
        Directory.CreateDirectory(OverrideDir);
        File.WriteAllText(Path.Combine(OverrideDir, FileFor(role)), text);
    }

    public void RestoreDefault(ReviewRole role)
    {
        var overridePath = Path.Combine(OverrideDir, FileFor(role));
        if (File.Exists(overridePath))
        {
            File.Delete(overridePath);
        }
    }

    internal static string FileFor(ReviewRole role) => role switch
    {
        ReviewRole.PlanCritique => "plan-critique.md",
        ReviewRole.Architecture => "architecture.md",
        ReviewRole.SecurityReliability => "security-reliability.md",
        _ => "uxdx-performance.md",
    };
}

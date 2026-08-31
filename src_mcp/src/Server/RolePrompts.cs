using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>
/// Role prompts: a shipped file default, an override layer, a restore — the prompt-catalog
/// pattern, so editing a prompt is not a rebuild and restoring one is not an archaeology dig.
/// </summary>
/// <remarks>
/// Shipped defaults live beside the binary (<c>prompts/*.md</c>, copied at build); overrides live
/// under the data dir and win while they exist; restore is deleting the override. The server only
/// READS here — editing arrives with the extension (epic 05).
/// </remarks>
public sealed class RolePrompts(string dataDir)
{
    private string OverrideDir => Path.Combine(dataDir, "prompts");

    public string For(ReviewRole role)
    {
        var overridePath = Path.Combine(OverrideDir, FileFor(role));
        return File.Exists(overridePath) ? File.ReadAllText(overridePath) : ShippedDefault(role);
    }

    public string ShippedDefault(ReviewRole role) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "prompts", FileFor(role)));

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

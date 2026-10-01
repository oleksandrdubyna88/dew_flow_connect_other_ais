namespace CoaiMcp.Core.QuestionConsult;

/// <summary>
/// What a question-consultant prompt NEEDS of its runtime: nothing, this machine's disk, or the web
/// (PLAN_question_consultant.md, A3). Every prompt declares exactly one.
/// </summary>
public enum Capability
{
    /// <summary>The question alone — a scratch cwd, no file tool, no web tool.</summary>
    None,

    /// <summary>Read-only access to the roots the operator listed, and nothing outside them.</summary>
    Disk,

    /// <summary>The web, and nothing of this machine — a web row is given the sanitised question and no more (A2).</summary>
    Web,
}

/// <summary>The capability words as the shared files spell them, and the parse that never guesses.</summary>
public static class Capabilities
{
    /// <summary>The three, in the order the file lists them — the one list a refusal spells.</summary>
    public static IReadOnlyList<Capability> All { get; } = [Capability.None, Capability.Disk, Capability.Web];

    /// <summary>The list as a refusal spells it: <c>none, disk, web</c>.</summary>
    public static string AllSpelled => string.Join(", ", All.Select(c => c.Spelled()));

    /// <summary>The file's word for a capability — lower case, the same on both halves.</summary>
    public static string Spelled(this Capability capability) => capability switch
    {
        Capability.None => "none",
        Capability.Disk => "disk",
        Capability.Web => "web",
        _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, "a capability this build does not spell — map it here"),
    };

    /// <summary>A capability from its word, without case; false for anything else, never a default.</summary>
    public static bool TryParse(string word, out Capability capability)
    {
        foreach (var known in All)
        {
            if (string.Equals(known.Spelled(), word.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                capability = known;
                return true;
            }
        }

        capability = Capability.None;
        return false;
    }
}

/// <summary>
/// What one row is GRANTED: a capability, and for <see cref="Capability.Disk"/> the roots it may read.
/// </summary>
/// <remarks>
/// The roots travel with the capability because they mean nothing apart from it: a <c>none</c> or a
/// <c>web</c> grant carrying a root is a leak path, and <see cref="ConfinementPlanner"/> refuses it.
/// Whether a root is a drive, a profile directory or a system directory is the server's check (D14 c,
/// S2); here a root is an absolute path and nothing more is asked of it.
/// </remarks>
/// <param name="Roots">Absolute directories, in the order the operator listed them; the first is the cwd of a disk launch.</param>
public sealed record CapabilityGrant(Capability Capability, IReadOnlyList<string> Roots)
{
    /// <summary>The question alone.</summary>
    public static CapabilityGrant None { get; } = new(Capability.None, []);

    /// <summary>The web alone.</summary>
    public static CapabilityGrant Web { get; } = new(Capability.Web, []);

    /// <summary>Read-only reads under these roots.</summary>
    public static CapabilityGrant Disk(params string[] roots) => new(Capability.Disk, [.. roots]);
}

using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server;

/// <summary>One vendor that could not be given one role, and why.</summary>
/// <remarks>
/// Three fields rather than the formatted sentence they used to be. The round needs the PAIR to know
/// it has already said this — a role dealt four lenses was refused four times in the same words —
/// and the sentence is a rendering, which belongs at the boundary that renders. It mirrors
/// <see cref="SkippedRole"/> beside it, which has been a record since it shipped. (codex, on the
/// code round of the story that introduced this list.)
/// </remarks>
internal sealed record ExcludedRole(string Provider, string Role, string Reason)
{
    /// <summary>The <c>name: reason</c> line a round summary shows, shaped like every other one.</summary>
    public string Sentence => $"{Provider}: {Reason}";
}

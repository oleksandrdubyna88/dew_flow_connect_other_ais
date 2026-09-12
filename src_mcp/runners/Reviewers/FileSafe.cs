namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// A name that came from OUTSIDE, made safe to put in a path.
/// </summary>
/// <remarks>
/// <para>A provider id and a role id both end up in file names — the answer a vendor writes, the
/// prompt handed to a local engine, the evidence a round keeps. Both used to be closed sets: a
/// vendor id came from a fixed list and a role was a CLR enum, so neither could be a path. The enum
/// is gone (story B1 of the roles plan), and a role is now whatever a person typed into their
/// settings.</para>
/// <para>Composition refuses an id that is not <c>^[A-Za-z][A-Za-z0-9_]*$</c>, so nothing shaped
/// like <c>../../outside</c> should reach here at all. This is the second lock on the same door,
/// and it is here because the two are far apart: the rule lives in the core's composition, the file
/// name is built in a vendor adapter, and a future caller building an invocation by hand passes
/// neither. Raised by codex on B1's plan round.</para>
/// <para>Kept deliberately dull — every character the platform refuses becomes an underscore. It is
/// not an escaping scheme and must not become one: two roles that differ only in a refused
/// character would collide, which composition's own uniqueness rule is what prevents.</para>
/// <para>It was a private helper inside <see cref="ReviewerExecutor"/>, guarding the evidence file
/// alone, and its own remark said what to do next: <i>"`Role` is an enum today, so nothing observed
/// has ever carried a separator. That is a fact about today's callers rather than about this
/// function, and it is the callers that change."</i> The callers changed; the function moved out to
/// meet them rather than being copied to each.</para>
/// </remarks>
internal static class FileSafe
{
    public static string Part(string part) =>
        string.Concat(part.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}

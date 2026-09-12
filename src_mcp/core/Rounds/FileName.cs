namespace CoaiMcp.Core.Rounds;

/// <summary>
/// A name that came from OUTSIDE, made safe to put in a path.
/// </summary>
/// <remarks>
/// <para>A role id and a prompt id both end up in file names — the answer a vendor writes, the
/// prompt handed to a local engine, the remote job file, the kept evidence, the override text under
/// <c>&lt;dataDir&gt;/prompts/</c>. Both used to be closed sets: a vendor id came from a fixed list and a
/// role was a CLR enum, so neither could be a path. The enum is gone, and both are now whatever a
/// person typed into their settings.</para>
/// <para>Composition refuses a role id that is not <c>^[A-Za-z][A-Za-z0-9_]*$</c> and a prompt id
/// that is not <c>^[a-z0-9][a-z0-9-]*$</c>, so nothing shaped like a path should reach a caller of
/// this at all. It is the second lock on the same door, and it is here because the two are far
/// apart: the rule lives in composition, the file name is built in a vendor adapter or in the
/// prompt store, and a caller assembling an invocation by hand passes neither.</para>
/// <para>Replacing the SEPARATORS is what prevents traversal, on every platform: <c>..</c> is only a
/// parent directory when something splits it off as a segment, and both <c>/</c> and <c>\</c> are
/// invalid in a file name on Windows while <c>/</c> is on Unix. So <c>../../escaped</c> becomes
/// <c>.._.._escaped</c> — an odd name, in the right directory.</para>
/// <para>Kept deliberately dull, and it must not become an escaping scheme: two ids differing only
/// in a refused character would collide, which composition's own uniqueness rule is what prevents.
/// It began as a private helper inside <c>ReviewerExecutor</c>, guarding the evidence file alone,
/// and its own remark said what to do next — <i>"`Role` is an enum today, so nothing observed has
/// ever carried a separator. That is a fact about today's callers rather than about this function,
/// and it is the callers that change."</i> The callers changed; the function moved out to meet them
/// rather than being copied to each.</para>
/// </remarks>
public static class FileName
{
    public static string Safe(string part) =>
        string.Concat(part.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}

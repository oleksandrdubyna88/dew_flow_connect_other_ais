using CoaiMcp.Tests.Leftovers;

namespace CoaiMcp.Tests;

/// <summary>
/// The owner claim a run folder carries, as a thin reader over the library's ONE grammar (E5 S3):
/// <c>&lt;owner&gt;:&lt;host&gt;@&lt;os&gt;:&lt;pid&gt;[:&lt;start&gt;[:&lt;ns&gt;]]</c> with <c>linux</c>, <c>macos</c> or <c>windows</c>
/// (<c>.agents/conventions/common/test-leftovers.md</c> §3).
/// </summary>
/// <remarks>
/// The writer is <see cref="Owner.RunRecord"/>, the reader <see cref="Owner.Parse"/> and the judge <see cref="Liveness.State"/>: three-valued, never
/// two-valued. This class only names them for the tests of this suite that ask "what does a claim say" — it holds no grammar of its own, because a second
/// grammar is how the Node and C# spellings drifted before (the old <c>host@linux:pid:start</c> form, which neither new reader accepts).
/// </remarks>
public static class OwnerClaim
{
    /// <summary>The owner a claim text names, or <c>null</c> when it is unreadable (the old form without an owner included).</summary>
    public static Owner? Read(string text) => Owner.Parse(text);

    /// <summary>The liveness of the process a claim names; <c>Unknown</c> keeps everything it cannot prove gone.</summary>
    public static OwnerState Judge(string text, Func<long, Probe> probe) => Liveness.State(Owner.Parse(text), probe);

    /// <summary>The claim text of THIS process, as the run folder writes it (no trailing newline).</summary>
    public static string ForThisProcess() => Owner.RunRecord().Format();
}

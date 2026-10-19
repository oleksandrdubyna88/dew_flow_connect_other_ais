using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CoaiMcp.Runners.Processes;

/// <summary>What asking the kernel to make this process the reaper of its own tree came to — a closed union, so "it could not" always carries the errno to show.</summary>
public abstract record SubreaperResult
{
    private SubreaperResult()
    {
    }

    /// <summary>Every orphan of this process's tree — <c>setsid</c> or not — is now reparented to THIS process, not to init (nor to WSL's <c>Relay</c>, L-K4).</summary>
    public sealed record Became : SubreaperResult;

    /// <summary><c>prctl</c> answered this <c>errno</c>: the shim cannot verify a teardown, so it launches nothing.</summary>
    public sealed record Failed(int Errno) : SubreaperResult;

    /// <summary>Not Linux: there is no subreaper to become, and today's teardown is the one that runs.</summary>
    public sealed record NotLinux : SubreaperResult;
}

/// <summary>
/// <c>prctl(PR_SET_CHILD_SUBREAPER, 1)</c> (DL9 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>): a process that becomes a child subreaper is the parent of every descendant whose own parent
/// exits, however far down the tree it was started and whatever session it opened — which is what lets a teardown LIST an orphan that a process-group kill would never reach (L-K4: <c>rg</c>
/// in its own session survives a pid and a group kill).
/// </summary>
/// <remarks>
/// <para>Called once, as the first statement of the shim (<c>AskAcpMode.RunAsync</c>), never by a test host: a subreaper collects every orphan of every test that runs beside it. The shim
/// starts nothing but the agent today; the reaping rule (<see cref="ProcessReaping"/>) does not rely on that fact.</para>
/// <para>The import is spelled <c>"libc"</c> (the <c>AppendOnlyFile</c> precedent) and is source-generated, so it is Native-AOT safe.</para>
/// </remarks>
public static partial class LinuxSubreaper
{
    private const int PrSetChildSubreaper = 36;

    /// <summary>The refusal when <c>prctl</c> failed — the plan's wording, with the errno.</summary>
    public static string Sentence(int errno) =>
        $"coai could not make this process the reaper of its own tree (prctl(PR_SET_CHILD_SUBREAPER) answered errno {errno}), so it cannot verify Kimi's teardown and does not launch it (DL9 of research/PLAN_kimi_and_vibe_on_linux.md)";

    /// <summary>Idempotent. Off Linux: <see cref="SubreaperResult.NotLinux"/> and nothing is called.</summary>
    public static SubreaperResult Become() => OperatingSystem.IsLinux() ? BecomeOnLinux() : new SubreaperResult.NotLinux();

    [SupportedOSPlatform("linux")]
    private static SubreaperResult BecomeOnLinux() =>
        Prctl(PrSetChildSubreaper, 1, 0, 0, 0) == 0 ? new SubreaperResult.Became() : new SubreaperResult.Failed(Marshal.GetLastPInvokeError());

    [SupportedOSPlatform("linux")]
    [LibraryImport("libc", EntryPoint = "prctl", SetLastError = true)]
    private static partial int Prctl(int option, nuint arg2, nuint arg3, nuint arg4, nuint arg5);
}

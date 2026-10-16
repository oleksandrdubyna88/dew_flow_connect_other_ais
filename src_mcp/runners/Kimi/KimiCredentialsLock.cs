using System.Security.Cryptography;
using System.Text;
using CoaiMcp.Runners.Files;

namespace CoaiMcp.Runners.Kimi;

/// <summary>What taking the shim's tree hold came to — a closed union: held, held by somebody else, or cannot be taken at all.</summary>
public abstract record TreeHolding
{
    private TreeHolding()
    {
    }

    /// <summary>Both files held, inheritable: dispose it once the agent is over.</summary>
    public sealed record Taken(TreeHold Hold) : TreeHolding;

    /// <summary>Another tree holds this sign-in (or this home) still.</summary>
    public sealed record Busy : TreeHolding;

    /// <summary>No key (the folder cannot be named by its handle), or a file that cannot be opened — and why.</summary>
    public sealed record Unusable(Exception Cause) : TreeHolding;
}

/// <summary>The two inheritable files of a running Kimi tree: the sign-in's and the home's. Disposing releases both, once.</summary>
public sealed class TreeHold(FileStream signIn, FileStream home) : IDisposable
{
    private int _given;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _given, 1) == 1)
        {
            return;
        }

        signIn.Dispose();
        home.Dispose();
    }
}

/// <summary>
/// The Kimi sign-in's two locks (D19 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>; E4.2 decisions 1, 9, 10 and 11, brief section 9):
/// one refresher at a time, because Kimi's refresh token ROTATES — two processes refreshing the one sign-in sign the person out.
/// </summary>
/// <remarks>
/// <para><b>Two files per sign-in, one duty each.</b> The GATE (<c>&lt;key&gt;.lock</c>, <see cref="TakeAsync"/>) serialises LAUNCHERS: it is
/// the server's, non-inheritable, held from before the version probe until the launch has been read, and the kernel releases it
/// when the server dies. The TREE HOLD (<c>&lt;key&gt;.tree.lock</c>, <see cref="HoldTree"/>) is the SHIM's: taken inheritable immediately
/// before the agent starts, so <c>kimi</c> inherits it and the sign-in stays held while EITHER the shim or the agent lives — a server
/// killed over a still-refreshing <c>kimi</c> must not let a second refresher in. A second inheritable file per HOME
/// (<c>&lt;root&gt;/coai-tree.lock</c>) lets a disposal ask "is a tree alive on THIS home" without opening the sign-in folder
/// (<see cref="HomeProbe"/>).</para>
/// <para><b>The key is the folder's IDENTITY, never its spelling</b> (decision 9): SHA-256 of the upper-cased final path of the opened
/// folder handle (<c>HandleRead.FinalDirectory</c>). A folder the kernel cannot name has NO key and takes no lock — never a silent
/// fall-back to the spelling, which would be two keys for one sign-in. On a case-SENSITIVE folder the upper-casing merges two
/// distinct folders into one key: two sign-ins that wait on each other, the safe direction.</para>
/// <para><b>The lock folder</b> is <c>&lt;LocalApplicationData&gt;/coai-mcp/kimi-locks/</c> beside the engine leases — NOT under
/// <c>coai-kimi/</c> (the D20 sweep walks every folder there) and not in temp (no sweep takes it; the files are empty and reused).
/// Tests override it per async flow (<see cref="RootFor"/>), never by assigning a static: the suite runs eight tests at once.</para>
/// </remarks>
public static class KimiCredentialsLock
{
    /// <summary>The per-home hold's file name, in the owned root beside <c>home/</c> and <c>os/</c>.</summary>
    public const string HomeHoldName = "coai-tree.lock";

    private static readonly TimeSpan Slice = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long the per-home file is waited for. Nothing else holds it for long, but a disposal's <see cref="HomeProbe"/> opens it
    /// exclusively for the length of one open, and a shim that met that instant used to read it as a bug and exit 65.
    /// </summary>
    private static readonly TimeSpan HomeWait = TimeSpan.FromMilliseconds(500);

    private static readonly AsyncLocal<string?> _root = new();

    private static readonly AsyncLocal<Func<string, string>?> _final = new();

    private static readonly AsyncLocal<Action<string>?> _beforeTheHomeHold = new();

    // DoNotVerify: off Windows .NET answers "" for a folder that does not exist yet (a fresh account has no ~/.local/share), and the path would have been a
    // RELATIVE one. The folder is made by MadeRoot before anything is taken in it.
    private static readonly string DefaultRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "coai-mcp", "kimi-locks");

    /// <summary>Where the locks live; the product's default unless <see cref="RootFor"/> says otherwise on this async flow.</summary>
    public static string Root => _root.Value ?? DefaultRoot;

    /// <summary>
    /// <see cref="Root"/> pointed at <paramref name="folder"/> for this async flow until the result is disposed — the shim's
    /// <c>--locks</c> (it never derives the folder from <c>LocalApplicationData</c> inside the redirected environment) and a test's.
    /// </summary>
    /// <remarks>Public, where the plan wrote <c>internal</c>: the shim lives in another assembly, and so does the fake CLI that plays it.</remarks>
    public static IDisposable RootFor(string folder)
    {
        var before = _root.Value;
        _root.Value = folder;

        return new Restore(before);
    }

    /// <summary>TEST ONLY — null in the product: what names a folder's final path (the product's is <c>HandleRead.FinalDirectory</c>). Per async flow.</summary>
    internal static Func<string, string>? Final
    {
        get => _final.Value;
        set => _final.Value = value;
    }

    /// <summary>TEST ONLY — null in the product: told the per-home file's path between the two takes of <see cref="HoldTree"/>, so a test can make a disposal's probe meet it. Per async flow.</summary>
    internal static Action<string>? BeforeTheHomeHold
    {
        get => _beforeTheHomeHold.Value;
        set => _beforeTheHomeHold.Value = value;
    }

    /// <summary>The key of a sign-in folder, or EMPTY when the kernel cannot name it by its handle (it does not exist, cannot be opened, or is off Windows).</summary>
    public static string KeyFor(string target)
    {
        var final = (Final ?? HandleRead.FinalDirectory)(target);

        return final.Length == 0 ? string.Empty : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(final.ToUpperInvariant())))[..16];
    }

    /// <summary>The gate's file, or EMPTY when there is no key — a caller that gets an empty path refuses; it never hashes the spelling instead.</summary>
    public static string PathFor(string target) => Named(target, ".lock");

    /// <summary>The sign-in's tree-hold file, or EMPTY when there is no key.</summary>
    public static string TreePathFor(string target) => Named(target, ".tree.lock");

    /// <summary>
    /// The server's gate on <paramref name="target"/>'s sign-in, waited for in one-second slices until taken or <paramref name="ct"/>
    /// fires (<see cref="OperationCanceledException"/>, which the executor already reads as the row's end). <see cref="Hold.Unusable"/>
    /// at once for a folder with no key — and nothing is made under <see cref="Root"/> — or a lock folder or file that cannot be used.
    /// </summary>
    public static async Task<Hold> TakeAsync(string target, CancellationToken ct)
    {
        var path = PathFor(target);
        if (path.Length == 0)
        {
            return new Hold.Unusable(new IOException(Unnamed(target)));
        }

        if (MadeRoot() is { } unusable)
        {
            return unusable;
        }

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var hold = await Task.Run(() => HeldFile.TakeWithin(path, Slice), ct);
            if (hold is not Hold.Busy)
            {
                return hold;
            }
        }
    }

    /// <summary>
    /// The shim's tree hold (decision 10): the sign-in's file for <paramref name="target"/> and the per-home file in
    /// <paramref name="root"/>, BOTH inheritable, so the agent started right after inherits them. Synchronous on purpose — the shim
    /// takes it immediately before <c>AcpClient.AskAsync</c>, which is synchronous up to the process start, and must not yield
    /// between the take and the start. <see cref="TreeHolding.Busy"/> after <paramref name="within"/> is the shim's exit 69.
    /// </summary>
    public static TreeHolding HoldTree(string target, string root, TimeSpan within) => TakeTreeThrough(target, root, within, Inheritable);

    /// <summary>The one place a hold is asked to be INHERITABLE — the product's take; a test passes <see cref="HeldFile.TakeWithin"/> without it (no test holds an inheritable handle in the host).</summary>
    private static Hold Inheritable(string path, TimeSpan within) => HeldFile.TakeWithin(path, within, inheritable: true);

    /// <summary>
    /// <see cref="HoldTree"/>'s decisions over a <paramref name="take"/> of one file: no key, no lock folder, the sign-in's file busy or unusable, the
    /// home's file taken after it (and the first given back when it cannot be). The seam exists so these run in the test host with the NON-inheriting take.
    /// </summary>
    internal static TreeHolding TakeTreeThrough(string target, string root, TimeSpan within, Func<string, TimeSpan, Hold> take)
    {
        var path = TreePathFor(target);
        if (path.Length == 0)
        {
            return new TreeHolding.Unusable(new IOException(Unnamed(target)));
        }

        if (MadeRoot() is { } unusable)
        {
            return new TreeHolding.Unusable(unusable.Cause);
        }

        return take(path, within) switch
        {
            Hold.Taken signIn => WithHome(signIn.Stream, root, take),
            Hold.Unusable bad => new TreeHolding.Unusable(bad.Cause),
            _ => new TreeHolding.Busy(),
        };
    }

    /// <summary>What a disposal asks before it unlinks anything: is a tree alive on THIS home. No sign-in folder is opened.</summary>
    public static LockProbe HomeProbe(string root) => HeldFile.Probe(Path.Combine(root, HomeHoldName));

    /// <summary>The per-home file taken after the sign-in's; the first is released when the second cannot be had. Only a disposal's probe ever opens it besides, for a moment, which <see cref="HomeWait"/> waits out; a file still busy after that is answered as unusable.</summary>
    private static TreeHolding WithHome(FileStream signIn, string root, Func<string, TimeSpan, Hold> take)
    {
        BeforeTheHomeHold?.Invoke(Path.Combine(root, HomeHoldName));
        var home = take(Path.Combine(root, HomeHoldName), HomeWait);
        if (home is Hold.Taken taken)
        {
            return new TreeHolding.Taken(new TreeHold(signIn, taken.Stream));
        }

        signIn.Dispose();

        return new TreeHolding.Unusable(home is Hold.Unusable bad ? bad.Cause : new IOException($"{Path.Combine(root, HomeHoldName)} is held by another process, and nothing but a disposal's probe ever takes it"));
    }

    private static string Named(string target, string suffix) =>
        KeyFor(target) is { Length: > 0 } key ? Path.Combine(Root, key + suffix) : string.Empty;

    private static string Unnamed(string target) =>
        $"the Kimi sign-in folder {target} cannot be named by its handle, so coai cannot key its lock — refused (D19)";

    /// <summary>The lock folder made; the unusable answer when it cannot be (a file where it should be, no access).</summary>
    private static Hold.Unusable? MadeRoot()
    {
        try
        {
            Directory.CreateDirectory(Root);

            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new Hold.Unusable(e);
        }
    }

    private sealed class Restore(string? before) : IDisposable
    {
        public void Dispose() => _root.Value = before;
    }
}

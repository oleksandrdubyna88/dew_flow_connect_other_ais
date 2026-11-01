using System.Security.Cryptography;
using CoaiMcp.Runners.Files;

namespace CoaiMcp.Runners.Vibe;

/// <summary>What making the sign-in link came to — closed. The reasons name paths and system words, never a byte of the file.</summary>
public abstract record SignInLink
{
    private SignInLink()
    {
    }

    /// <summary>The link is there and reads back as the target.</summary>
    public sealed record Linked : SignInLink;

    /// <summary>The person's file is not there (<c>ENOENT</c>/<c>ENOTDIR</c>): nothing is linked, Vibe runs signed out.</summary>
    public sealed record NoSignIn(string Reason) : SignInLink;

    /// <summary>The target is fine, the link could not be made: nothing is left at the name, Vibe runs signed out.</summary>
    public sealed record NotMade(string Reason) : SignInLink;

    /// <summary>The target is not a sign-in coai links (or the home holds something it cannot remove): the launch is refused.</summary>
    public sealed record Refused(string Sentence) : SignInLink;

    /// <summary>
    /// What the person is told when the launch goes on without a link — carried as the launch's WARNING (A1 spec §9 item 7: the cure is
    /// keyed on Vibe's own text and cannot see the launch control), so it rides the same reviewer line and card as the cure. Empty otherwise.
    /// </summary>
    public string Note => this switch
    {
        NoSignIn none => $"{none.Reason} — Vibe runs signed out; run `vibe --setup` in a terminal on this side",
        NotMade lost => $"the .env link into coai's Vibe home could not be made: {lost.Reason} — Vibe runs signed out; coai never copies a sign-in",
        _ => string.Empty,
    };
}

/// <summary>
/// The disk steps of making the link, handed in so a host that cannot make a symbolic link and a filesystem that fails can both be stood in
/// for (<c>OneShotSteps</c>' shape). The real ones are <see cref="Real"/>.
/// </summary>
/// <param name="Judge">The person's file, read through ONE descriptor (<see cref="RegularFiles.ReadOwned"/>).</param>
/// <param name="Link">Makes the symbolic link at a path that does not exist: <c>(link, target)</c>.</param>
/// <param name="RawTarget">The target exactly as the link stores it — never resolved; empty when the path is not a link.</param>
/// <param name="Remove">Removes a link BY ITS OWN NAME; a file symlink is unlinked, never followed.</param>
/// <param name="Present">Whether anything — a link, a file, a folder — is at the path.</param>
internal sealed record SignInSteps(
    Func<string, FileReadResult> Judge,
    Action<string, string> Link,
    Func<string, string> RawTarget,
    Action<string> Remove,
    Func<string, bool> Present)
{
    public static SignInSteps Real { get; } = new(
        path => RegularFiles.ReadOwned(path, DotenvNames.MaxBytes, RegularFiles.EffectiveUid()),
        (link, target) => File.CreateSymbolicLink(link, target),
        OwnedPaths.RawLinkTarget,
        File.Delete,
        path => OwnedPaths.IsLinkIfThere(path) || File.Exists(path) || Directory.Exists(path));
}

/// <summary>
/// Makes the ONE link an owned Vibe home may hold: <c>&lt;home&gt;/.env</c> → the person's sign-in file (DL2 of
/// <c>research/PLAN_kimi_and_vibe_on_linux.md</c>). Never a copy, never a read of the target by path, never a <c>chmod</c>, never a log line.
/// </summary>
/// <remarks>
/// In order: the target is judged through one descriptor and its NAMES scanned (<see cref="DotenvNames"/>), the buffer zeroed after the
/// scan — <b>on every turn, including the same home's second</b>; then, unless the right link is already there, the link is made at a path
/// that does not exist and read back RAW. A failed step leaves nothing at the name (a half-made link is removed by its own name) and the
/// launch goes on signed out — and a home holding a <c>.env</c> coai could not remove is never launched on.
/// <para><b>This corrects the A1 spec §3.2 step 2</b>, which answered <c>Linked</c> for the right link on a second turn "with nothing
/// touched". The person's file can change between two turns of one home (a <c>VIBE_*</c> variable appended, <c>chmod 666</c>, a FIFO put in its
/// place) and that turn would run on a sign-in nobody judged; a link to a file is not a verdict on the file. Legitimate key rotation stays
/// admitted: the names are the same, only the value differs.</para>
/// <para><b>A residual, stated:</b> <see cref="LeftOver"/> removes by name what is at the link path after a failed step. If the person's own
/// account (the same uid — the home is <c>0700</c>) puts something there between the <c>Present</c> check and the link, the link fails with
/// <c>EEXIST</c> and that something is removed. The same account can do worse directly; coai does not claim to close it.</para>
/// </remarks>
public static class VibeSignInLink
{
    public static SignInLink Make(VibeSignIn signIn, string home) => Make(signIn, home, signIn.Steps);

    internal static SignInLink Make(VibeSignIn signIn, string home, SignInSteps steps)
    {
        ArgumentOutOfRangeException.ThrowIfZero(signIn.PersonsEnvFile.Length, nameof(signIn));
        var link = Path.Combine(home, VibeSignIn.LinkName);

        return steps.Present(link) ? Existing(signIn, home, link, steps) : Fresh(signIn, home, link, steps);
    }

    /// <summary>
    /// Something is at the name already. The shape check one step earlier refused anything but the right link, so only the right link (the
    /// same home's second turn) is met here — and its TARGET is judged again, the link not remade. Any other thing is NOT coai's to delete,
    /// and is refused with the home's own sentence.
    /// </summary>
    private static SignInLink Existing(VibeSignIn signIn, string home, string link, SignInSteps steps) =>
        steps.RawTarget(link) == signIn.PersonsEnvFile
            ? Judged(signIn, steps, () => new SignInLink.Linked())
            : new SignInLink.Refused($"the coai-owned Vibe home {home} holds {VibeSignIn.LinkName}, which is not the link coai makes to the Vibe sign-in {signIn.PersonsEnvFile} — coai refuses to start Vibe on it; delete {home} and the next launch starts from an empty home");

    private static SignInLink Fresh(VibeSignIn signIn, string home, string link, SignInSteps steps) =>
        Judged(signIn, steps, () => Made(signIn, home, link, steps));

    /// <summary>The person's file judged through one descriptor and its names scanned; <paramref name="admitted"/> says what an admitted file comes to.</summary>
    private static SignInLink Judged(VibeSignIn signIn, SignInSteps steps, Func<SignInLink> admitted) => steps.Judge(signIn.PersonsEnvFile) switch
    {
        FileReadResult.Missing => new SignInLink.NoSignIn($"there is no Vibe sign-in file at {signIn.PersonsEnvFile}"),
        FileReadResult.Unreadable bad => new SignInLink.Refused(VibeSignInSentences.Target(signIn.PersonsEnvFile, bad.Reason)),
        FileReadResult.Content read => Scanned(signIn, read.Bytes, admitted),
        _ => throw new InvalidOperationException("the union is closed"),
    };

    /// <summary>The names scan, then the buffer zeroed whatever the verdict was (I4); only an admitted file is linked.</summary>
    private static SignInLink Scanned(VibeSignIn signIn, byte[] bytes, Func<SignInLink> admitted)
    {
        DotenvVerdict verdict;
        try
        {
            verdict = DotenvNames.Judge(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }

        return verdict switch
        {
            DotenvVerdict.Refused(var line, var kind) => new SignInLink.Refused(VibeSignInSentences.Name(signIn.PersonsEnvFile, line, kind)),
            _ => admitted(),
        };
    }

    /// <summary>Makes the link at a path that does not exist and reads it back RAW; a failure removes what it made and says why.</summary>
    private static SignInLink Made(VibeSignIn signIn, string home, string link, SignInSteps steps)
    {
        try
        {
            steps.Link(link, signIn.PersonsEnvFile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException or NotSupportedException)
        {
            return LeftOver(home, link, steps) is { Length: > 0 } stuck ? new SignInLink.Refused(stuck) : new SignInLink.NotMade(e.Message);
        }

        return ReadBack(signIn, home, link, steps);
    }

    /// <summary>The link must read back as exactly the target coai wrote; anything else is removed and the launch goes on without it.</summary>
    private static SignInLink ReadBack(VibeSignIn signIn, string home, string link, SignInSteps steps)
    {
        if (steps.RawTarget(link) == signIn.PersonsEnvFile)
        {
            return new SignInLink.Linked();
        }

        // The read-back text is NOT quoted: a link target is a path, but not one coai wrote.
        return LeftOver(home, link, steps) is { Length: > 0 } stuck
            ? new SignInLink.Refused(stuck)
            : new SignInLink.NotMade($"the .env link at {link} read back as something else and was removed");
    }

    /// <summary>
    /// Removes what a failed step left at the name, by the name. Empty when nothing is left; else the refusal: a home with an unknown
    /// <c>.env</c> in it is never launched on.
    /// </summary>
    private static string LeftOver(string home, string link, SignInSteps steps)
    {
        var reason = Removed(link, steps);

        return steps.Present(link)
            ? $"the coai-owned Vibe home {home} holds {VibeSignIn.LinkName}, which coai could not remove after a failed link ({reason}) — coai refuses to start Vibe on it; delete {home}"
            : string.Empty;
    }

    private static string Removed(string link, SignInSteps steps)
    {
        try
        {
            steps.Remove(link);

            return string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }
}

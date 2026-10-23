using System.Text;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Worktrees;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The directory junction coai makes from an owned Kimi home to the person's <c>credentials</c> folder (E4.1 of
/// <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>, scenarios S1.1–S1.6 and S1.14): made by the kernel call and not by
/// <c>cmd.exe</c>, read back before anyone trusts it, refused with a sentence before anything is made, and removed
/// leaving the person's folder whole.
/// </summary>
/// <remarks>
/// <para>Every junction here is made INSIDE this test's own <c>testsToDelete-junc-</c> folder, to a sibling folder in the same
/// folder that plays "the person's credentials" — never to the real <c>~/.kimi-code</c>, never to another test's folder
/// (test-leftovers.md): a leftover is swept by a recursive remove, and Windows PowerShell 5.1's walks INTO a junction.</para>
/// <para>The tests' own maker (<see cref="DirectoryLink"/>, <c>mklink /J</c>) stays the INDEPENDENT oracle: it does not delegate to
/// the product's.</para>
/// </remarks>
public sealed class JunctionsTests : IDisposable
{
    private static readonly byte[] Token = Encoding.UTF8.GetBytes("""{"refresh_token":"not-a-real-one"}""");

    private readonly TempDir _dir = TempDir.For("testsToDelete-junc-");

    private string Target => _dir.At("persons-credentials");

    private string Link => _dir.At("link");

    public void Dispose() => _dir.Dispose();

    /// <summary>A file in the person's folder, read-only as the real ones are.</summary>
    private string Person(string name, bool readOnly = true)
    {
        Directory.CreateDirectory(Target);
        var file = Path.Combine(Target, name);
        File.WriteAllBytes(file, Token);
        if (readOnly)
        {
            File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
        }

        return file;
    }

    private void ThePersonsFolderIsWhole(params string[] names)
    {
        foreach (var name in names)
        {
            var file = Path.Combine(Target, name);
            File.ReadAllBytes(file).Should().Equal(Token, $"{name} keeps its bytes");
            File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly).Should().BeTrue($"{name} keeps its read-only attribute");
        }

        Directory.EnumerateFileSystemEntries(Target).Select(Path.GetFileName).Should().BeEquivalentTo(names);
    }

    /// <summary>The root a long absolute path starts with, in THIS host's spelling.</summary>
    private static string HostRoot => OperatingSystem.IsWindows() ? @"C:\" : "/";

    private bool NothingIsAt(string path) =>
        !File.Exists(path) && !Directory.Exists(path) && !OwnedPaths.IsLinkIfThere(path);

    // ---------- S1.1 ----------

    [Fact]
    public void AJunctionIsMade_AndReadsBackAsItsTarget()
    {
        using var standIn = WindowsStandIn.Begin();
        var secret = Person("secret.json");

        var answer = Junctions.Make(HostKind.Windows, Link, Target);

        answer.Should().BeEmpty();
        var attributes = File.GetAttributes(Link);
        attributes.HasFlag(FileAttributes.ReparsePoint).Should().BeTrue("a junction is a reparse point");
        attributes.HasFlag(FileAttributes.Directory).Should().BeTrue();
        Junctions.SameFolder(Junctions.Target(Link), Target).Should().BeTrue($"it reads back as {Junctions.Target(Link)}");
        File.ReadAllBytes(Path.Combine(Link, "secret.json")).Should().Equal(Token, "the link reaches the person's folder");
        DirectoryLink.Remove(Link);
        File.ReadAllBytes(secret).Should().Equal(Token);
        File.GetAttributes(secret).HasFlag(FileAttributes.ReadOnly).Should().BeTrue();
    }

    // ---------- S1.2 ----------

    [Fact]
    public async Task TheProductsJunction_IsWhatMklinkMakes()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the oracle is mklink's junction, which only Windows has");
        Person("secret.json");
        var mine = _dir.At("product-link");
        var theirs = _dir.At("mklink-link");
        var tree = _dir.At("tree");
        Directory.CreateDirectory(tree);
        Junctions.Make(HostKind.Windows, mine, Target).Should().BeEmpty();
        await DirectoryLink.MakeAsync(new ProcessLauncher(), theirs, Target);

        Junctions.Target(mine).Should().Be(Junctions.Target(theirs));
        new DirectoryInfo(mine).LinkTarget.Should().NotBeNull("the .NET reader sees the product's link the way it sees mklink's");
        new DirectoryInfo(mine).LinkTarget.Should().Be(new DirectoryInfo(theirs).LinkTarget);
        Directory.Move(mine, Path.Combine(tree, "product-link"));
        Directory.Move(theirs, Path.Combine(tree, "mklink-link"));

        GitScratch.DeleteEvenIfReadOnly(tree);

        Directory.Exists(tree).Should().BeFalse("both links were removed by their own names");
        ThePersonsFolderIsWhole("secret.json");
    }

    // ---------- S1.3 ----------

    [Theory]
    [InlineData("unc", "is not a local drive path")]
    [InlineData("wsl", "is not a local drive path")]
    [InlineData("relative", "is not a local drive path")]
    [InlineData("missing", "does not exist")]
    [InlineData("target-is-a-file", "is a file, not a folder")]
    [InlineData("target-is-a-junction", "is itself a link, and coai never chains links (D19)")]
    [InlineData("link-is-a-folder", "already exists, and a junction is only ever made at a fresh path")]
    [InlineData("link-is-a-file", "already exists, and a junction is only ever made at a fresh path")]
    [InlineData("link-is-a-link", "already exists, and a junction is only ever made at a fresh path")]
    [InlineData("too-long", "a junction's reparse point can name")]
    [InlineData("too-long-for-the-buffer", "a junction's reparse point can name")]
    public async Task ARefusalBeforeAnythingIsMade(string kind, string sentence)
    {
        using var standIn = WindowsStandIn.Begin();
        Person("secret.json");
        var target = Target;
        var link = Link;
        switch (kind)
        {
            case "unc": target = @"\\server\share\k"; break;
            case "wsl": target = @"\\wsl$\Ubuntu\home\k"; break;
            case "relative": target = "k"; break;
            case "missing": target = _dir.At("not-there"); break;
            case "target-is-a-file":
                target = _dir.At("a-file");
                await File.WriteAllTextAsync(target, "x", TestContext.Current.CancellationToken);
                break;
            case "target-is-a-junction":
                target = _dir.At("a-junction");
                await DirectoryLink.MakeAsync(new ProcessLauncher(), target, Target);
                break;
            case "link-is-a-folder": Directory.CreateDirectory(link); break;
            case "link-is-a-file": await File.WriteAllTextAsync(link, "x", TestContext.Current.CancellationToken); break;
            case "link-is-a-link": await DirectoryLink.MakeAsync(new ProcessLauncher(), link, Target); break;
            case "too-long": target = HostRoot + new string('a', 33_000); break;
            case "too-long-for-the-buffer": target = HostRoot + new string('a', 4_997); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "a row this test does not know");
        }

        var refusal = Junctions.Refusal(HostKind.Windows, link, target);
        var made = Junctions.Make(HostKind.Windows, link, target);

        refusal.Should().Contain(sentence);
        if (kind.StartsWith("too-long", StringComparison.Ordinal))
        {
            refusal.Should().Contain(target.Length.ToString(), "the refusal names the number");
        }

        made.Should().Be(refusal, "Make answers the refusal and goes no further");
        if (kind.StartsWith("link-is", StringComparison.Ordinal))
        {
            Directory.Exists(link).Should().Be(kind != "link-is-a-file", "what was there is still there, untouched");
        }
        else
        {
            NothingIsAt(link).Should().BeTrue("not even an empty folder is left at the link's path");
        }
    }

    [Fact]
    public void ATargetThatExistsButOverflowsTheReparseBuffer_IsRefusedBeforeAnythingIsMade()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "a 5 000-character folder cannot be made off Windows (PATH_MAX); the arithmetic is pinned by ATargetOverTheReparseBound_IsRefusedByItsLength_OnEveryHost");
        // A REAL folder whose path overflows a reparse point's 16 KiB (M2 of the E4.1 review): the 32 000-character ceiling the
        // first draft had let it through to the kernel; the refusal now knows the real bound.
        var deep = Target;
        for (var i = 0; i < 26; i++)
        {
            deep = Path.Combine(deep, new string((char)('a' + i), 200));
        }

        Directory.CreateDirectory(deep);

        var answer = Junctions.Make(HostKind.Windows, Link, deep);

        answer.Should().Contain("a junction's reparse point can name");
        NothingIsAt(Link).Should().BeTrue("refused before anything is made — not even the empty folder");
    }

    [Theory]
    [InlineData("a-file-for-a-parent")]
    [InlineData("a-nul-in-the-name")]
    public void AMakeThatCannotCreateItsFolder_SaysSo_AndLeavesNothingBehind(string kind)
    {
        using var standIn = WindowsStandIn.Begin();
        Person("secret.json");
        var link = kind == "a-file-for-a-parent" ? _dir.At("a-file", "link") : _dir.At("a\0b");
        File.WriteAllText(_dir.At("a-file"), "x");

        var answer = Junctions.Make(HostKind.Windows, link, Target);

        answer.Should().Match(kind == "a-file-for-a-parent" ? $"the junction at {link} could not be made*" : $"*is not a path a folder can be made at*", "an exception from the file system is a sentence, never an escape");
        if (kind == "a-file-for-a-parent")
        {
            NothingIsAt(link).Should().BeTrue();
        }
    }

    [Fact]
    public void AMappedNetworkDrive_IsNotALocalDrive()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "a drive letter is a Windows spelling; ADrivePath_IsLocalWhenItIsRootedOnAFixedOrRemovableDrive asks the same on every host");
        Person("secret.json");

        var refusal = Junctions.Refusal(HostKind.Windows, Link, Target, _ => DriveType.Network);
        var local = Junctions.Refusal(HostKind.Windows, Link, Target, _ => DriveType.Fixed);

        refusal.Should().Contain("is not a local drive path", "a mapped drive looks like C: and lives on a server");
        local.Should().BeEmpty();
    }

    [Fact]
    public void ALinkThatCannotBeTakenBackOut_SaysSo()
    {
        using var standIn = WindowsStandIn.Begin();
        Person("secret.json");
        var sibling = _dir.At("somewhere-else");
        Directory.CreateDirectory(sibling);
        try
        {
            var answer = Junctions.Make(HostKind.Windows, Link, Target, _ => sibling, _ => throw new IOException("locked by somebody"));

            answer.Should().Contain("reads back as").And.Contain("could not be removed: locked by somebody", "the half-trusted link is still there, and the sentence must not say otherwise");
        }
        finally
        {
            DirectoryLink.Remove(Link);
        }
    }

    [Fact]
    public async Task TheProductsJunction_HasMklinksRawReparseBytes()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the oracle is fsutil's dump of mklink's reparse point; the layout itself is pinned by TheReparseBuffer_IsTheMountPointLayout_ByteForByte");
        Person("secret.json");
        var mine = _dir.At("product-link");
        var theirs = _dir.At("mklink-link");
        Junctions.Make(HostKind.Windows, mine, Target).Should().BeEmpty();
        await DirectoryLink.MakeAsync(new ProcessLauncher(), theirs, Target);

        var queryMine = await ReparseQueryAsync(mine);
        var queryTheirs = await ReparseQueryAsync(theirs);

        queryMine.Should().Contain("Reparse Tag Value", "fsutil answered with the reparse data");
        queryMine.Should().Be(queryTheirs, "the same tag, names and bytes as the junction mklink /J makes for the same target");
    }

    private static async Task<string> ReparseQueryAsync(string link)
    {
        var result = await new ProcessLauncher().RunAsync(
            new ProcessRequest(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "fsutil.exe"), ["reparsepoint", "query", link], Path.GetDirectoryName(link)!));
        result.ExitCode.Should().Be(0, $"fsutil: {result.StdErr}{result.StdOut}");

        return result.StdOut.Replace(link, "<link>", StringComparison.OrdinalIgnoreCase);
    }

    // ---------- S1.4 ----------

    [Theory]
    [InlineData(HostKind.Linux)]
    [InlineData(HostKind.Wsl)]
    public void ALinuxOrWslHost_MakesASymlink_ThatReadsBackAsTheTarget(HostKind host)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the product's Unix shell and the mount table of the machine it runs on: Linux's (the host's door is HostAdmitted, S-K1)");
        Directory.CreateDirectory(Target);

        var made = Junctions.Make(host, Link, Target);

        made.Should().BeEmpty($"a sign-in link is made on a {HostKinds.Word(host)} host (S-K1)");
        Junctions.Target(Link).Should().Be(Target, "read back through the link's own target");
        OwnedPaths.IsLinkIfThere(Link).Should().BeTrue("a symbolic link is what the deleter treats as a link: unlinked by its own name, never entered");
    }

    [Theory]
    [InlineData(HostKind.MacOs)]
    [InlineData(HostKind.Other)]
    public void AMacOsOrOtherHost_IsStillRefused_AndNothingIsMade(HostKind host)
    {
        Directory.CreateDirectory(Target);

        var refusal = Junctions.Refusal(host, Link, Target);
        var made = Junctions.Make(host, Link, Target);

        refusal.Should().Contain("D18").And.Contain(HostKinds.Word(host)).And.Contain("a sign-in link is made only on a host that admits Kimi").And.NotContain("hosts only").And.NotContain("measured", "the reason is the missing lock family, not that nobody measured it");
        made.Should().Be(refusal);
        NothingIsAt(Link).Should().BeTrue("nothing is made on a host Kimi is refused on");
    }

    [Theory]
    [InlineData(HostKind.Linux)]
    [InlineData(HostKind.Wsl)]
    public void ALinuxOrWslHost_AsksTheMountTable_AndATableItCannotReadRefuses(HostKind host)
    {
        using var standIn = WindowsStandIn.Begin();
        Directory.CreateDirectory(Target);
        Junctions.StandInMountInfo(() => throw new IOException("no /proc here"));
        try
        {
            var refusal = Junctions.Refusal(host, Link, Target);

            refusal.Should().Contain("/proc/self/mountinfo").And.Contain("cannot tell which filesystem").And.Contain("refused", "a table coai could not read was read as 'local'");
            NothingIsAt(Link).Should().BeTrue();
        }
        finally
        {
            Junctions.StandInMountInfo(null);
        }
    }

    [Fact]
    public void AWindowsHost_NeverAsksTheMountTable()
    {
        using var standIn = WindowsStandIn.Begin();
        Directory.CreateDirectory(Target);
        Junctions.StandInMountInfo(() => throw new IOException("a Windows host must not read a Unix mount table"));
        try
        {
            Junctions.Refusal(HostKind.Windows, Link, Target).Should().BeEmpty();
        }
        finally
        {
            Junctions.StandInMountInfo(null);
        }
    }

    // ---------- S-K2 ----------

    [Fact]
    public void ATargetOnADrvfsMount_IsRefused_AndNothingIsMade()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the mount table is Unix's, and a Windows run judges the real path as a drive path; MountLocalityTests (S-K12) run the rule everywhere");
        Directory.CreateDirectory(Target);
        var mountPoint = WorktreePaths.Real(_dir.Path);
        using var standIn = WindowsStandIn.Begin();
        Junctions.StandInMountInfo(() => $"36 25 0:32 / {mountPoint} rw,noatime - 9p C:\\134 rw,aname=drvfs;path=C:\\134;uid=1000\n25 1 8:1 / / rw - ext4 /dev/sda1 rw", _ => new MountIdentity.NotAsked());
        try
        {
            var refusal = Junctions.Refusal(HostKind.Wsl, Link, Target);
            var made = Junctions.Make(HostKind.Wsl, Link, Target);

            refusal.Should().Contain("Windows filesystem", "a sign-in folder on the Windows drives should be refused on wsl").And.Contain("9p");
            made.Should().Be(refusal);
            NothingIsAt(Link).Should().BeTrue("nothing is made on a refused filesystem");
        }
        finally
        {
            Junctions.StandInMountInfo(null);
        }
    }

    // ---------- S1.5 / S1.6 ----------

    [Fact]
    public void TheReadBackIsNormalised()
    {
        using var standIn = WindowsStandIn.Begin();
        Person("secret.json");
        // The case half is Windows' (a case-sensitive file system keeps FOLDER and folder apart); the trailing separator is every host's.
        var spelled = (OperatingSystem.IsWindows() ? Target.ToUpperInvariant() : Target) + Path.DirectorySeparatorChar;

        var answer = Junctions.Make(HostKind.Windows, Link, spelled);
        var again = Junctions.Make(HostKind.Windows, _dir.At("second"), Target, _ => OperatingSystem.IsWindows() ? spelled.ToLowerInvariant() : spelled);

        answer.Should().BeEmpty();
        again.Should().BeEmpty("SameFolder ignores case and the trailing separator on Windows");
        OwnedPaths.IsLinkIfThere(Link).Should().BeTrue();
        OwnedPaths.IsLinkIfThere(_dir.At("second")).Should().BeTrue();
        Junctions.SameFolder(Target, spelled).Should().BeTrue();
    }

    [Fact]
    public void AReadBackThatNamesAnotherFolder_UnlinksAndRefuses()
    {
        using var standIn = WindowsStandIn.Begin();
        Person("secret.json");
        var sibling = _dir.At("somewhere-else");
        Directory.CreateDirectory(sibling);

        var answer = Junctions.Make(HostKind.Windows, Link, Target, _ => sibling);

        answer.Should().Contain("reads back as").And.Contain(sibling).And.Contain("removed again");
        NothingIsAt(Link).Should().BeTrue("the half-trusted link is removed by its own name");
        ThePersonsFolderIsWhole("secret.json");
    }

    // ---------- S1.14 ----------

    [Theory]
    [InlineData("TempDir.Dispose")]
    [InlineData("GitScratch.DeleteEvenIfReadOnly")]
    public void ADisposalOfAHomeMadeWithTheProductsJunction_LeavesThePersonsFolderWhole(string deleter)
    {
        using var standIn = WindowsStandIn.Begin();
        // Two folders of this test, both testsToDelete-: the person's must outlive the deletion of the home to be looked at,
        // and TempDir.Dispose — the tests' own path — removes its folder whole.
        using var persons = TempDir.For("testsToDelete-junc-persons-");
        using var home = TempDir.For("testsToDelete-junc-home-");
        foreach (var name in (string[])["kimi-code-env.json", "kimi-code.json"])
        {
            File.WriteAllBytes(persons.At(name), Token);
            File.SetAttributes(persons.At(name), FileAttributes.ReadOnly);
        }

        Directory.CreateDirectory(home.At("kh-x", "home", "sessions"));
        File.WriteAllText(home.At("kh-x", "home", "config.toml"), "[loop_control]");
        File.WriteAllText(home.At("kh-x", "home", "sessions", "index.jsonl"), "{}");
        Junctions.Make(HostKind.Windows, home.At("kh-x", "home", "credentials"), persons).Should().BeEmpty();
        var names = Directory.EnumerateFileSystemEntries(persons).Select(Path.GetFileName).ToList();

        if (deleter == "TempDir.Dispose")
        {
            home.Dispose();
        }
        else
        {
            GitScratch.DeleteEvenIfReadOnly(home.Path);
        }

        Directory.Exists(home.Path).Should().BeFalse("the home and its junction are gone");
        Directory.EnumerateFileSystemEntries(persons).Select(Path.GetFileName).Should().BeEquivalentTo(names);
        foreach (var name in names)
        {
            File.ReadAllBytes(persons.At(name!)).Should().Equal(Token);
            File.GetAttributes(persons.At(name!)).HasFlag(FileAttributes.ReadOnly).Should().BeTrue();
        }
    }

    [Fact]
    public void ATempDirWhoseLinkLostItsTarget_ThrowsOnTheWayOut_NeverALeftover()
    {
        using var standIn = WindowsStandIn.Begin();
        // The person's folder is NOT inside the tree being deleted (E4.1 deviation 17: two TempDirs). Inside it, whether the deleter meets the link before the
        // folder it points at is the file system's enumeration order — NTFS happens to sort "home" before "persons", ext4 hashes names — and a target deleted
        // first is simply gone, which is not a loss anybody can see (PR #817's Linux job, 2026-10-16).
        using var person = TempDir.For("testsToDelete-junc-lost-person-");
        var scope = TempDir.For("testsToDelete-junc-lost-");
        try
        {
            var persons = person.At("persons");
            Directory.CreateDirectory(persons);
            File.WriteAllText(Path.Combine(persons, "kimi-code.json"), "{}");
            Junctions.Make(HostKind.Windows, scope.At("home", "credentials"), persons).Should().BeEmpty();
            GitScratch.AfterUnlink = _ => File.Delete(Path.Combine(persons, "kimi-code.json"));

            var dispose = scope.Dispose;

            dispose.Should().Throw<LinkTargetLostException>("in a test a lost target is a failing test");
        }
        finally
        {
            GitScratch.AfterUnlink = null;
            scope.Dispose();
        }
    }

    // ---------- E4.2 S2.46: an ANCESTOR of the target may be a link (decision 13) ----------

    /// <summary>A person's <c>credentials</c> reached through an ancestor junction (<c>alias</c> → <c>persons</c>): the shape of a relocated profile.</summary>
    private string ThroughAnAncestorLink()
    {
        var persons = _dir.At("persons");
        Directory.CreateDirectory(Path.Combine(persons, "credentials"));
        var secret = Path.Combine(persons, "credentials", "kimi-code.json");
        File.WriteAllBytes(secret, Token);
        File.SetAttributes(secret, FileAttributes.ReadOnly);
        var alias = _dir.At("alias");
        Junctions.Make(HostKind.Windows, alias, persons).Should().BeEmpty("the fixture's ancestor link");

        return Path.Combine(alias, "credentials");
    }

    [Fact]
    public void AnAncestorThatIsALink_IsAdmitted_AndTheJunctionKeepsTheSpelling()
    {
        using var standIn = WindowsStandIn.Begin();
        var target = ThroughAnAncestorLink();

        Junctions.Refusal(HostKind.Windows, Link, target).Should().BeEmpty("a relocated profile is an ordinary machine");
        Junctions.Make(HostKind.Windows, Link, target).Should().BeEmpty();

        Junctions.Target(Link).Should().Be(target, "one level — never resolved into the ancestor junction's own target");
        File.ReadAllBytes(Path.Combine(Link, "kimi-code.json")).Should().Equal(Token);
        DirectoryLink.Remove(Link);
        File.ReadAllBytes(Path.Combine(_dir.At("persons"), "credentials", "kimi-code.json")).Should().Equal(Token);
        File.GetAttributes(Path.Combine(_dir.At("persons"), "credentials", "kimi-code.json")).HasFlag(FileAttributes.ReadOnly).Should().BeTrue();
    }

    [Fact]
    public void AnAncestorWhoseResolutionLeavesTheLocalDrives_IsRefused_AndNothingIsMade()
    {
        using var standIn = WindowsStandIn.Begin();
        var target = ThroughAnAncestorLink();

        var refusal = Junctions.Make(HostKind.Windows, Link, target, Junctions.Target, _ => { }, _ => @"\\server\share\x\credentials");

        refusal.Should().Contain("resolves to").And.Contain(@"\\server\share\x\credentials").And.Contain("not a local drive path");
        NothingIsAt(Link).Should().BeTrue();
    }

    [Fact]
    public void ATargetThatCannotBeNamedByItsHandle_IsRefused_AndNothingIsMade()
    {
        using var standIn = WindowsStandIn.Begin();
        var target = ThroughAnAncestorLink();

        var refusal = Junctions.Make(HostKind.Windows, Link, target, Junctions.Target, _ => { }, _ => string.Empty);

        refusal.Should().Contain("cannot be named by its handle on this host").And.Contain("refused (D19)");
        NothingIsAt(Link).Should().BeTrue();
    }

    [Fact]
    public async Task ATargetThatItselfIsALink_StaysRefused()
    {
        using var standIn = WindowsStandIn.Begin();
        Person("secret.json");
        var viaLink = _dir.At("target-is-link");
        await DirectoryLink.MakeAsync(new ProcessLauncher(), viaLink, Target);

        Junctions.Refusal(HostKind.Windows, Link, viaLink).Should().Contain("is itself a link");
    }
    // ---------- the decisions that need no kernel: the arithmetic, the buffer's layout, the spelling of a local path ----------

    /// <summary>A target path of exactly <paramref name="characters"/> characters, spelled as an absolute path of THIS host.</summary>
    private static string Spelled(int characters)
    {
        var root = OperatingSystem.IsWindows() ? @"C:\" : "/";

        return root + new string('a', characters - root.Length);
    }

    /// <summary>The longest target whose two names still fit a reparse point's 16 KiB: 16 + 2(n+4) + 2 + 2n + 2 = 16 384, so n = 4 089.</summary>
    private const int MostCharacters = 4089;

    [Fact]
    public void ATargetOverTheReparseBound_IsRefusedByItsLength_OnEveryHost()
    {
        var target = Spelled(MostCharacters + 1);

        var refusal = Junctions.Refusal(HostKind.Windows, Link, target, _ => DriveType.Fixed, _ => target);

        refusal.Should().Contain($"the target path is {MostCharacters + 1} characters").And.Contain($"more than the {MostCharacters} a junction's reparse point can name");
        Junctions.ReparseSize(target).Should().BeGreaterThan(16 * 1024);
    }

    [Fact]
    public void ATargetAtTheReparseBound_PassesTheLength_AndIsRefusedForNotExisting()
    {
        var target = Spelled(MostCharacters);

        var refusal = Junctions.Refusal(HostKind.Windows, Link, target, _ => DriveType.Fixed, _ => target);

        Junctions.ReparseSize(target).Should().Be(16 * 1024, "the bound is exact: one more character overflows");
        refusal.Should().Be($"{target} does not exist", "the length check passed, and the existence check is the next in line");
    }

    [Fact]
    public void TheReparseBuffer_IsTheMountPointLayout_ByteForByte()
    {
        const string target = @"C:\persons\credentials";

        var buffer = Junctions.ReparseBuffer(target);

        buffer.Length.Should().Be(Junctions.ReparseSize(target));
        System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(0)).Should().Be(0xA0000003, "IO_REPARSE_TAG_MOUNT_POINT");
        System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(4)).Should().Be((ushort)(buffer.Length - 8), "ReparseDataLength: everything after the first eight bytes");
        System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(6)).Should().Be(0, "reserved");
        var substituteAt = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(8));
        var substituteBytes = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(10));
        var printAt = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(12));
        var printBytes = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(14));
        substituteAt.Should().Be(0);
        printAt.Should().Be((ushort)(substituteBytes + 2), "the print name follows the substitute name and its NUL");
        Encoding.Unicode.GetString(buffer, 16 + substituteAt, substituteBytes).Should().Be(@"\??\" + target, "the NT prefix belongs to the substitute name only");
        Encoding.Unicode.GetString(buffer, 16 + printAt, printBytes).Should().Be(target);
        buffer[^2..].Should().Equal(new byte[] { 0, 0 }, "the print name is NUL-terminated");
    }

    [Theory]
    [InlineData(@"C:\x", DriveType.Fixed, true)]
    [InlineData("C:/x", DriveType.Fixed, true)]
    [InlineData(@"D:\x", DriveType.Removable, true)]
    [InlineData(@"C:\x", DriveType.Network, false)]
    [InlineData(@"C:\x", DriveType.Unknown, false)]
    [InlineData(@"C:\x", DriveType.NoRootDirectory, false)]
    [InlineData(@"\\server\share\k", DriveType.Fixed, false)]
    [InlineData(@"\\?\C:\x", DriveType.Fixed, false)]
    [InlineData(@"\\.\C:\x", DriveType.Fixed, false)]
    [InlineData(@"\\?\UNC\server\share\k", DriveType.Fixed, false)]
    [InlineData("C:", DriveType.Fixed, false)]
    [InlineData(@"relative\x", DriveType.Fixed, false)]
    [InlineData(@"1:\x", DriveType.Fixed, false)]
    public void ADrivePath_IsLocalWhenItIsRootedOnAFixedOrRemovableDrive(string path, DriveType drive, bool local)
    {
        List<string> asked = [];

        Junctions.IsLocalDrivePath(path, root =>
        {
            asked.Add(root);

            return drive;
        }).Should().Be(local);

        asked.Should().AllBe(path[..Math.Min(3, path.Length)], "the drive is asked by its root, never by the whole path");
    }

    [Theory]
    [InlineData("/home/someone/.kimi-code/credentials", true)]
    [InlineData("/net/server/share/k", false)]
    [InlineData("/misc/x", false)]
    [InlineData("/Network/x", false)]
    [InlineData("//server/share/k", false)]
    [InlineData("relative/x", false)]
    public void OffWindows_APathIsLocalWhenItIsRootedAndNotUnderAnAutomountFolder(string path, bool local)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the Unix rule is the one a non-Windows host applies; Windows applies the drive rule above");

        Junctions.IsLocal(path, _ => DriveType.Fixed).Should().Be(local);
    }

    /// <summary>C of the review of 2026-10-18: the D19 SPELLING check and the network override say one thing. With <c>coai.kimiAllowNetworkSignIn</c> on, a Linux or WSL host also admits the automount spellings a network home has; a Windows host never does (UNC stays refused), and a bare '//' share never is one.</summary>
    [Theory]
    [InlineData(HostKind.Wsl, "/net/server/share/k", true, true)]
    [InlineData(HostKind.Linux, "/misc/x", true, true)]
    [InlineData(HostKind.Wsl, "/Network/x", true, true)]
    [InlineData(HostKind.Wsl, "/net/server/share/k", false, false)]
    [InlineData(HostKind.Windows, "/net/server/share/k", true, false)]
    [InlineData(HostKind.Wsl, "//server/share/k", true, false)]
    [InlineData(HostKind.Wsl, "relative/x", true, false)]
    public void TheSpellingCheck_FollowsTheOverride_OnALinuxOrWslHostOnly(HostKind host, string path, bool allow, bool local) =>
        Junctions.IsLocalSpelling(host, path, _ => DriveType.Fixed, allow).Should().Be(local);

    /// <summary>
    /// E6 gate finding 11: a home under an automount spelling (<c>/net/</c>, <c>/misc/</c>) is refused by its SPELLING before the mount table is read, and that sentence carries the same cure as the mount refusal (DL6) — a person whose
    /// <c>KIMI_CODE_HOME</c> is under <c>/net/</c> never reaches the mount refusal, so the cure has to be here.
    /// </summary>
    [Theory]
    [InlineData(HostKind.Linux, "/net/server/share/k")]
    [InlineData(HostKind.Wsl, "/misc/x")]
    public void AnAutomountSpelling_IsRefusedWithTheCure_OnALinuxOrWslHost(HostKind host, string target)
    {
        var refusal = Junctions.Refusal(host, Link, target, _ => DriveType.Fixed);

        refusal.Should().Contain("not a local drive path").And.Contain("KIMI_CODE_HOME").And.Contain("kimi login").And.Contain("coai.kimiAllowNetworkSignIn");
    }

    [Fact]
    public void AUncHome_OnAWindowsHost_KeepsItsOwnSentence_WithNoOverrideToOffer()
    {
        var refusal = Junctions.Refusal(HostKind.Windows, Link, @"\server\share\k", _ => DriveType.Fixed);

        refusal.Should().Contain("not a local drive path").And.NotContain("coai.kimiAllowNetworkSignIn", "an override a Windows host never has is not offered");
    }
}

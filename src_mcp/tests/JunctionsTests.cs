using System.Text;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
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

    private static void OnWindows() =>
        Assert.SkipUnless(OperatingSystem.IsWindows(), "a directory junction is made on Windows only (D18 pins the other hosts in S1.4)");

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

    private bool NothingIsAt(string path) =>
        !File.Exists(path) && !Directory.Exists(path) && !OwnedPaths.IsLinkIfThere(path);

    // ---------- S1.1 ----------

    [Fact]
    public void AJunctionIsMade_AndReadsBackAsItsTarget()
    {
        OnWindows();
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
        OnWindows();
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
    [InlineData("target-is-a-junction", "is itself a link, and coai never chains links (D19)")]
    [InlineData("link-is-a-folder", "already exists, and a junction is only ever made at a fresh path")]
    [InlineData("link-is-a-file", "already exists, and a junction is only ever made at a fresh path")]
    [InlineData("link-is-a-link", "already exists, and a junction is only ever made at a fresh path")]
    [InlineData("too-long", "characters, more than the 32000")]
    public async Task ARefusalBeforeAnythingIsMade(string kind, string sentence)
    {
        OnWindows();
        Person("secret.json");
        var target = Target;
        var link = Link;
        switch (kind)
        {
            case "unc": target = @"\\server\share\k"; break;
            case "wsl": target = @"\\wsl$\Ubuntu\home\k"; break;
            case "relative": target = "k"; break;
            case "missing": target = _dir.At("not-there"); break;
            case "target-is-a-junction":
                target = _dir.At("a-junction");
                await DirectoryLink.MakeAsync(new ProcessLauncher(), target, Target);
                break;
            case "link-is-a-folder": Directory.CreateDirectory(link); break;
            case "link-is-a-file": await File.WriteAllTextAsync(link, "x", TestContext.Current.CancellationToken); break;
            case "link-is-a-link": await DirectoryLink.MakeAsync(new ProcessLauncher(), link, Target); break;
            case "too-long": target = @"C:\" + new string('a', 33_000); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "a row this test does not know");
        }

        var refusal = Junctions.Refusal(HostKind.Windows, link, target);
        var made = Junctions.Make(HostKind.Windows, link, target);

        refusal.Should().Contain(sentence);
        if (kind == "too-long")
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
    public void AMakeThatCannotBeCompleted_LeavesNothingBehind()
    {
        OnWindows();
        // A target path whose two names overflow a reparse point's 16 KiB, under the 32 000 characters Refusal allows: the
        // buffer is never laid, and the folder made for the link must not outlive that.
        var deep = Target;
        for (var i = 0; i < 26; i++)
        {
            deep = Path.Combine(deep, new string((char)('a' + i), 200));
        }

        Directory.CreateDirectory(deep);

        var answer = Junctions.Make(HostKind.Windows, Link, deep);

        answer.Should().StartWith($"the junction at {Link} could not be made");
        NothingIsAt(Link).Should().BeTrue("a half-made link never survives a failed call");
    }

    // ---------- S1.4 ----------

    [Theory]
    [InlineData(HostKind.Linux)]
    [InlineData(HostKind.Wsl)]
    [InlineData(HostKind.MacOs)]
    [InlineData(HostKind.Other)]
    public void ANonWindowsHost_IsRefusedWithTheD18Sentence(HostKind host)
    {
        Directory.CreateDirectory(Target);

        var refusal = Junctions.Refusal(host, Link, Target);
        var made = Junctions.Make(host, Link, Target);

        refusal.Should().Contain("D18").And.Contain(HostKinds.Word(host));
        made.Should().Be(refusal);
        NothingIsAt(Link).Should().BeTrue("nothing is made on a host Kimi is refused on");
    }

    // ---------- S1.5 / S1.6 ----------

    [Fact]
    public void TheReadBackIsNormalised()
    {
        OnWindows();
        Person("secret.json");
        var spelled = Target.ToUpperInvariant() + Path.DirectorySeparatorChar;

        var answer = Junctions.Make(HostKind.Windows, Link, spelled);
        var again = Junctions.Make(HostKind.Windows, _dir.At("second"), Target, _ => spelled.ToLowerInvariant());

        answer.Should().BeEmpty();
        again.Should().BeEmpty("SameFolder ignores case and the trailing separator on Windows");
        OwnedPaths.IsLinkIfThere(Link).Should().BeTrue();
        OwnedPaths.IsLinkIfThere(_dir.At("second")).Should().BeTrue();
        Junctions.SameFolder(Target, Target.ToUpperInvariant() + @"\").Should().BeTrue();
    }

    [Fact]
    public void AReadBackThatNamesAnotherFolder_UnlinksAndRefuses()
    {
        OnWindows();
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
        OnWindows();
        // Two folders of this test, both testsToDelete-: the person's must outlive the deletion of the home to be looked at,
        // and TempDir.Dispose — the tests' own path — removes its folder whole.
        using var persons = TempDir.For("testsToDelete-junc-persons-");
        var home = TempDir.For("testsToDelete-junc-home-");
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
        OnWindows();
        var scope = TempDir.For("testsToDelete-junc-lost-");
        var persons = scope.At("persons");
        Directory.CreateDirectory(persons);
        File.WriteAllText(Path.Combine(persons, "kimi-code.json"), "{}");
        Junctions.Make(HostKind.Windows, scope.At("home", "credentials"), persons).Should().BeEmpty();
        GitScratch.AfterUnlink = _ => File.Delete(Path.Combine(persons, "kimi-code.json"));
        try
        {
            var dispose = scope.Dispose;

            dispose.Should().Throw<LinkTargetLostException>("in a test a lost target is a failing test");
        }
        finally
        {
            GitScratch.AfterUnlink = null;
            scope.Dispose();
        }
    }
}

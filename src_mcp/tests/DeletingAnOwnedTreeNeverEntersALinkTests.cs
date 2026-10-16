using System.Text;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The shared deleter of coai's own trees (<see cref="GitScratch.DeleteEvenIfReadOnly"/>) removes a link by its own name and
/// never walks through it — so a tree holding a junction to somebody else's folder (a Kimi home's <c>credentials</c>, D19
/// of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>) is gone afterwards while the folder it pointed at keeps every file, its
/// bytes AND its read-only attribute.
/// </summary>
/// <remarks>
/// <para>Landed with the consultant home sweep (D20, S3), which deletes persistent homes through this deleter and whose
/// tests include a home with a junction. Before it, the deleter enumerated <c>SearchOption.AllDirectories</c> — which
/// ENTERS a directory reparse point — and cleared the read-only attribute of every file it met, the target's included.</para>
/// <para>A junction on Windows (no privilege needed), a symbolic link elsewhere — the shapes <see cref="DirectoryLink"/>
/// makes.</para>
/// </remarks>
public sealed class DeletingAnOwnedTreeNeverEntersALinkTests : IAsyncLifetime
{
    private static readonly byte[] Token = Encoding.UTF8.GetBytes("""{"refresh_token":"not-a-real-one"}""");

    private readonly TempDir _dir = TempDir.For("coai-answers-linkdelete-");

    private string Target => _dir.At("persons-credentials");

    private string Secret => Path.Combine(Target, "kimi-code-env.json");

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Target);
        await File.WriteAllBytesAsync(Secret, Token, TestContext.Current.CancellationToken);
        File.SetAttributes(Secret, File.GetAttributes(Secret) | FileAttributes.ReadOnly);
    }

    public ValueTask DisposeAsync()
    {
        if (File.Exists(Secret))
        {
            File.SetAttributes(Secret, File.GetAttributes(Secret) & ~FileAttributes.ReadOnly);
        }

        _dir.Dispose();

        return ValueTask.CompletedTask;
    }

    private async Task<string> HomeWithALinkAsync(string relativeLink)
    {
        var home = _dir.At("home");
        var link = Path.Combine([home, .. relativeLink.Split('/')]);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        // A read-only file of the home's own, as git leaves in every clone — the reason the deleter clears attributes at all.
        var own = Path.Combine(home, "sessions", "index.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(own)!);
        await File.WriteAllTextAsync(own, "{}", TestContext.Current.CancellationToken);
        File.SetAttributes(own, FileAttributes.ReadOnly);
        await DirectoryLink.MakeAsync(new ProcessLauncher(), link, Target);
        File.Exists(Path.Combine(link, "kimi-code-env.json")).Should().BeTrue("the fixture's link must reach the target, or this proves nothing");

        return home;
    }

    private void TheTargetIsUntouched()
    {
        Directory.Exists(Target).Should().BeTrue("the folder a link pointed at is somebody else's");
        File.ReadAllBytes(Secret).Should().Equal(Token, "its file keeps its bytes");
        File.GetAttributes(Secret).HasFlag(FileAttributes.ReadOnly).Should().BeTrue(
            "its file keeps its read-only attribute — clearing it means the deleter walked through the link");
    }

    [Theory]
    [InlineData("credentials")]
    [InlineData("a/b/credentials")]
    public async Task AJunctionInTheTree_IsRemovedByItsOwnName_AndItsTargetKeepsItsFile(string relativeLink)
    {
        var home = await HomeWithALinkAsync(relativeLink);

        GitScratch.DeleteEvenIfReadOnly(home);

        Directory.Exists(home).Should().BeFalse("the tree and the link in it are gone");
        TheTargetIsUntouched();
    }

    [Fact]
    public async Task ATreeThatIsItselfALink_IsUnlinked_NeverEmptied()
    {
        var link = _dir.At("home-link");
        await DirectoryLink.MakeAsync(new ProcessLauncher(), link, Target);

        GitScratch.DeleteEvenIfReadOnly(link);

        Directory.Exists(link).Should().BeFalse();
        TheTargetIsUntouched();
    }

    [Fact]
    public async Task ATamperedHomeWhoseCredentialsIsARealFolder_IsDeletedWhole_AndNothingOutsideIt()
    {
        var home = _dir.At("home");
        Directory.CreateDirectory(Path.Combine(home, "credentials"));
        var inside = Path.Combine(home, "credentials", "copy.json");
        await File.WriteAllBytesAsync(inside, Token, TestContext.Current.CancellationToken);
        File.SetAttributes(inside, FileAttributes.ReadOnly);

        GitScratch.DeleteEvenIfReadOnly(home);

        Directory.Exists(home).Should().BeFalse("a real folder is the home's own, and goes with it");
        TheTargetIsUntouched();
    }

    /// <summary>
    /// Vibe unpacks its plugin package with the ReadOnly attribute on its FOLDERS (research/RESULTS_vibe_confinement.md §26):
    /// a deleter that cleared files only removed every file and then failed on the first read-only folder, so a round's
    /// <c>vh-*</c> home was left for ever — the six-hour sweep calls the same deleter.
    /// </summary>
    [Fact]
    public void ATreeWithReadOnlyFolders_IsDeletedWhole()
    {
        var home = _dir.At("vh-0123456789");
        var skill = Path.Combine(home, "logs", "session", "plugins", "packages", "6b", "6bb6380c", "skills", "create-plugin");
        Directory.CreateDirectory(skill);
        File.WriteAllText(Path.Combine(skill, "SKILL.md"), "x");
        File.SetAttributes(Path.Combine(skill, "SKILL.md"), FileAttributes.ReadOnly);
        foreach (var folder in (string[])[skill, Path.GetDirectoryName(skill)!, Path.Combine(home, "logs", "session", "plugins", "packages", "6b", "6bb6380c")])
        {
            new DirectoryInfo(folder).Attributes |= FileAttributes.ReadOnly;
        }

        GitScratch.DeleteEvenIfReadOnly(home);

        Directory.Exists(home).Should().BeFalse("read-only folders go with their tree");
    }

    /// <summary>
    /// E4.1 S1.7: the person's folder lost a file while its link was being removed — the deletion STOPS with the name, and
    /// what the home still held is still there (the sign-in is the person's; a second loss is not made worse by a sweep that
    /// goes on).
    /// </summary>
    [Fact]
    public async Task ALinkWhoseTargetLostAName_StopsTheDeletion_AndSaysWhich()
    {
        var other = Path.Combine(Target, "kimi-code.json");
        await File.WriteAllBytesAsync(other, Token, TestContext.Current.CancellationToken);
        var home = await HomeWithALinkAsync("credentials");
        await File.WriteAllTextAsync(Path.Combine(home, "config.toml"), "[loop_control]", TestContext.Current.CancellationToken);
        GitScratch.AfterUnlink = _ => File.Delete(other);
        try
        {
            var delete = () => GitScratch.DeleteEvenIfReadOnly(home);

            var thrown = delete.Should().Throw<LinkTargetLostException>().Which;
            thrown.Message.Should().Contain("kimi-code.json").And.Contain(Target).And.Contain("changed while the link was removed");
        }
        finally
        {
            GitScratch.AfterUnlink = null;
        }

        File.Exists(Path.Combine(home, "config.toml")).Should().BeTrue("nothing more was deleted once the loss was seen");
        File.Exists(Path.Combine(home, "sessions", "index.jsonl")).Should().BeTrue();
        OwnedPaths.IsLinkIfThere(Path.Combine(home, "credentials")).Should().BeFalse("the link itself is gone");
        Directory.Exists(Path.Combine(home, "credentials")).Should().BeFalse();
        TheTargetIsUntouched();
    }

    /// <summary>E4.1 S1.9: a link whose target is gone, and a link to a FILE, are removed whole and their targets kept.</summary>
    [Theory]
    [InlineData("dangling-junction")]
    [InlineData("file-symlink")]
    public async Task ADanglingLink_AndAFileLink(string kind)
    {
        var tree = _dir.At("tree");
        Directory.CreateDirectory(tree);
        var link = Path.Combine(tree, "credentials");
        if (kind == "dangling-junction")
        {
            var gone = _dir.At("gone");
            Directory.CreateDirectory(gone);
            await DirectoryLink.MakeAsync(new ProcessLauncher(), link, gone);
            Directory.Delete(gone);
        }
        else
        {
            try
            {
                File.CreateSymbolicLink(link, Secret);
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                Assert.Skip($"this OS refuses to make a file symbolic link without privilege: {e.Message}");
            }
        }

        GitScratch.DeleteEvenIfReadOnly(tree);

        Directory.Exists(tree).Should().BeFalse();
        TheTargetIsUntouched();
    }

    /// <summary>
    /// E4.1 S1.10: .NET's own recursive delete does not enter a junction either — which is what keeps the three raw
    /// recursive deletes left in product code (an escalation's plan folder, two git worktrees) safe, and the reason S1.11
    /// pins that list.
    /// </summary>
    [Fact]
    public async Task DotNetsRecursiveDelete_DoesNotEnterAJunction()
    {
        var tree = _dir.At("tree");
        var link = Path.Combine(tree, "a", "credentials");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        var writable = Path.Combine(Target, "kimi-code.json");
        await File.WriteAllBytesAsync(writable, Token, TestContext.Current.CancellationToken);
        await DirectoryLink.MakeAsync(new ProcessLauncher(), link, Target);

        var ended = EndOfRawRecursiveDelete(tree);
        TestContext.Current.SendDiagnosticMessage($"Directory.Delete(recursive) over a junction ended: {ended}");

        // The type is the one thing that differs by machine; the guarantee below does not.
        ended.Should().BeOneOf("completed", nameof(UnauthorizedAccessException), nameof(IOException));
        OwnedPaths.IsLinkIfThere(link).Should().BeFalse($"the junction itself is removed, by its own name (the delete ended: {ended})");
        TheTargetIsUntouched();
        File.ReadAllBytes(writable).Should().Equal(Token, "a WRITABLE file would have been deleted had .NET entered the link");
    }

    /// <summary>
    /// How <c>Directory.Delete(tree, recursive: true)</c> ended over a tree holding a junction: <c>completed</c>, or the NAME of the
    /// exception it threw. Both exceptions are accepted because the type is a fact about the filesystem and not about coai —
    /// MEASURED 2026-10-14 on a local NTFS drive, .NET unmounts the junction by its own name and then throws
    /// <see cref="UnauthorizedAccessException"/> ("Access to the path 'credentials' is denied"); MEASURED 2026-10-16 on the
    /// <c>windows-latest</c> runner's D: drive it throws <see cref="IOException"/> ("The parameter is incorrect") from
    /// <c>RemoveDirectoryRecursive</c>. Either way the emptied parent folders are left and the link is not entered — the only
    /// property the three raw sites need, and what the caller asserts. All three sites swallow BOTH types.
    /// </summary>
    private static string EndOfRawRecursiveDelete(string tree)
    {
        try
        {
            Directory.Delete(tree, recursive: true);

            return "completed";
        }
        catch (UnauthorizedAccessException e)
        {
            return e.GetType().Name;
        }
        catch (IOException e)
        {
            return e.GetType().Name;
        }
    }

    /// <summary>
    /// E4.1 review B1: the deleter READS a link's target (its names) to verify nothing was lost — so it must never read one that
    /// is not on a local drive. A link planted in a coai tree pointing at a share would otherwise make every sweep connect out
    /// (Windows sends NTLM) or wait out the SMB timeout, twice per link, on the round's start path.
    /// </summary>
    [Theory]
    [InlineData(@"\\server\share\k")]
    [InlineData(@"\\?\C:\somewhere")]
    [InlineData(@"\\.\pipe\somewhere")]
    [InlineData(@"\\?\UNC\server\share\k")]
    [InlineData("relative-folder")]
    public void ATargetThatIsNotLocal_IsNeverRead(string target)
    {
        List<string> read = [];
        GitScratch.Reading = read.Add;
        try
        {
            var held = GitScratch.Look(target, isDirectory: true);

            held.Known.Should().BeFalse("nothing was read, so nothing is claimed");
        }
        finally
        {
            GitScratch.Reading = null;
        }

        read.Should().BeEmpty("a target off the local drives is never listed, however it is spelled");
    }

    [Fact]
    public void ALocalTarget_IsRead()
    {
        List<string> read = [];
        GitScratch.Reading = read.Add;
        try
        {
            GitScratch.Look(Target, isDirectory: true).Known.Should().BeTrue();
        }
        finally
        {
            GitScratch.Reading = null;
        }

        read.Should().ContainSingle().Which.Should().Be(Target);
    }

    /// <summary>E4.1 review M1: a name that vanished between the two listings but is back after a pause (a rotation by temp file and rename) is no loss.</summary>
    [Fact]
    public async Task ANameThatCameBackAfterAPause_IsNotALoss()
    {
        var rotating = Path.Combine(Target, "kimi-code.json");
        await File.WriteAllBytesAsync(rotating, Token, TestContext.Current.CancellationToken);
        var home = await HomeWithALinkAsync("credentials");
        GitScratch.AfterUnlink = _ => File.Delete(rotating);
        GitScratch.BeforeRelist = _ => File.WriteAllBytes(rotating, Token);
        try
        {
            GitScratch.DeleteEvenIfReadOnly(home);
        }
        finally
        {
            GitScratch.AfterUnlink = null;
            GitScratch.BeforeRelist = null;
        }

        Directory.Exists(home).Should().BeFalse("the rename settled, nothing was lost, the deletion went on");
        TheTargetIsUntouched();
    }

    /// <summary>E4.1 review M1: a link that points at its own parent would report its own name as lost.</summary>
    [Fact]
    public async Task ALinkPointingAtItsOwnParent_IsNotAFalseAlarm()
    {
        var tree = _dir.At("tree");
        Directory.CreateDirectory(tree);
        await File.WriteAllTextAsync(Path.Combine(tree, "own.txt"), "x", TestContext.Current.CancellationToken);
        await DirectoryLink.MakeAsync(new ProcessLauncher(), Path.Combine(tree, "self"), tree);

        GitScratch.DeleteEvenIfReadOnly(tree);

        Directory.Exists(tree).Should().BeFalse();
        TheTargetIsUntouched();
    }

    /// <summary>E4.1 review M6/M10: a question row's scratch whose link target lost a file is a WARNING in its log, naming the path — not the Debug line an ordinary busy file gets.</summary>
    [Fact]
    public async Task AQuestionRowsScratchWhoseLinkTargetChanged_IsAWarning()
    {
        var other = Path.Combine(Target, "kimi-code.json");
        await File.WriteAllBytesAsync(other, Token, TestContext.Current.CancellationToken);
        var home = await HomeWithALinkAsync("credentials");
        var sink = new ListSink();
        var log = new Serilog.LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
        GitScratch.AfterUnlink = _ => File.Delete(other);
        try
        {
            CoaiMcp.Server.QuestionRowLaunch.RemoveScratch(home, log);
        }
        finally
        {
            GitScratch.AfterUnlink = null;
        }

        sink.Levels.Should().ContainSingle().Which.Should().Be(Serilog.Events.LogEventLevel.Warning);
        sink.Lines.Single().Should().Contain(home).And.Contain("kimi-code.json");
    }
}

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
}

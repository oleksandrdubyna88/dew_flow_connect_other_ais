using CoaiMcp.Core.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A document path is resolved ROOT-DOWN, every directory inside a link's target included — and what the
/// resolver says is inside is what the operating system reads.
/// </summary>
/// <remarks>
/// <para><b>The defect (2026-11-01).</b> The resolver took <c>ResolveLinkTarget(returnFinalTarget: true).FullName</c>
/// for a link: the directories INSIDE that target path were never resolved, and a <c>..</c> in the link text was
/// folded lexically. Layout: <c>root/l2</c> links to a folder outside the root, <c>root/alias</c> links to
/// <c>root/l2/secret.md</c>. The check saw <c>root/l2/secret.md</c> — plainly inside — and the read followed
/// <c>l2</c> to the outside file, which <c>review_document</c> then sent to three vendors.</para>
/// <para>Every test here goes through the public behaviour: <see cref="DocumentReader.Read"/> and the list a
/// consultant is served. None of them names the resolver, so a replacement resolver keeps them honest.</para>
/// </remarks>
public sealed class ALinkCannotCarryTheReaderOutsideTests : IDisposable
{
    private const string Inside = "INSIDE-TEXT-3141";
    private const string Outside = "OUTSIDE-SECRET-2718";

    private readonly TempDir _work = TempDir.For("testsToDelete-realpath-");
    private readonly string _root;
    private readonly string _outside;

    public ALinkCannotCarryTheReaderOutsideTests()
    {
        _root = Directory.CreateDirectory(_work.At("root")).FullName;
        _outside = Directory.CreateDirectory(_work.At("outside")).FullName;
        Directory.CreateDirectory(Path.Combine(_outside, "deeper"));
        File.WriteAllText(Path.Combine(_outside, "secret.md"), Outside);
        File.WriteAllText(Path.Combine(_outside, "deeper", "secret2.md"), Outside);
        File.WriteAllText(Path.Combine(_root, "secret.md"), Inside);
        Directory.CreateDirectory(Path.Combine(_root, "docs"));
        File.WriteAllText(Path.Combine(_root, "docs", "spec.md"), Inside);
    }

    public void Dispose() => _work.Dispose();

    private string In(string name) => Path.Combine(_root, name);

    private void Link(string name, string text, string? at = null)
    {
        try
        {
            File.CreateSymbolicLink(at ?? In(name), text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"this machine cannot create a symbolic link: {e.Message}");
        }
    }

    private DocumentOutcome Read(string path) =>
        DocumentReader.Read(_root, new DocumentRequest(Path: path));

    private static string Said(DocumentOutcome outcome) => outcome switch
    {
        DocumentOutcome.Ready ready => $"READ: {ready.Text}",
        DocumentOutcome.Refused refused => refused.Sentence,
        _ => throw new InvalidOperationException("the union is closed"),
    };

    private void NothingOutsideIsRead(string path)
    {
        var outcome = Read(path);

        outcome.Should().BeOfType<DocumentOutcome.Refused>(
            $"'{path}' leads outside the repository, and what came back was: {Said(outcome)}");
    }

    [Fact]
    public void AFileLinkWhoseTargetPathCrossesAnotherLink_IsRefused()
    {
        Link("l2", _outside);
        Link("alias", In("l2/secret.md"));

        NothingOutsideIsRead("alias");
    }

    [Fact]
    public void ADirectoryLinkWhoseTargetPathCrossesAnotherLink_IsRefused()
    {
        Link("l2", _outside);
        Link("alias3", In("l2/deeper"));

        NothingOutsideIsRead("alias3/secret2.md");
    }

    [Fact]
    public void ADotDotInsideALinkText_IsFoldedByTheDiskNotByTheString()
    {
        // `dd` says "l3/../secret.md". Lexically that is root/secret.md — an inside decoy. On the disk l3 is a
        // link to outside/deeper, so `..` is outside, and the file the OS opens is outside/secret.md.
        Link("l3", Path.Combine(_outside, "deeper"));
        Link("dd", "l3/../secret.md");

        NothingOutsideIsRead("dd");
    }

    [Fact]
    public void ADotDotAfterAMissingSegmentInALinkText_IsRefused_NotFoldedToAFileThatWasNeverLookedAt()
    {
        // Coordinator's PoC 1: `a` says "nonexistent/../evil". The kernel answers ENOENT for it; a lexical pop
        // lands on root/evil, which links outside and was never inspected.
        Link("evil", Path.Combine(_outside, "secret.md"));
        Link("a", "nonexistent/../evil");

        NothingOutsideIsRead("a");
    }

    [Fact]
    public void ADotDotThatClimbsOutOfAMissingFolderLink_IsRefused()
    {
        // Coordinator's PoC 2: `a2` says "zz/.." with zz absent; the read asks for a2/evil.
        Link("evil", Path.Combine(_outside, "secret.md"));
        Link("a2", "zz/..");

        NothingOutsideIsRead("a2/evil");
    }

    [Fact]
    public void ADanglingLink_IsNotAMissingEntry_AndNeverResolvedVerbatimAsInside()
    {
        // `d` points into l2, which leaves the repository, at a name that is not there.
        Link("l2", _outside);
        Link("d", In("l2/missing"));

        NothingOutsideIsRead("d/x");
        Said(Read("d")).Should().NotContain(Outside);
    }

    [Fact]
    public void ARepositoryReachedThroughASymlinkedAncestor_StillReadsItsOwnLinks()
    {
        // The false-refusal side: the root is spelled through a link (every macOS temp folder), and an inside link is
        // spelled through the same one.
        var via = _work.At("via");
        Link("via", _work.Path, via);
        var spelled = Path.Combine(via, "root");
        Link("inlink", Path.Combine(spelled, "docs", "spec.md"));

        var outcome = DocumentReader.Read(spelled, new DocumentRequest(Path: Path.Combine(spelled, "inlink")));

        outcome.Should().BeOfType<DocumentOutcome.Ready>(Said(outcome));
        ((DocumentOutcome.Ready)outcome).Text.Should().Be(Inside);
    }

    [Fact]
    public void ALoopOfLinks_IsRefusedPromptly_NotThrownAndNotChased()
    {
        Link("loop-a", "loop-b");
        Link("loop-b", "loop-a");

        var started = System.Diagnostics.Stopwatch.StartNew();
        var outcome = Read("loop-a");

        outcome.Should().BeOfType<DocumentOutcome.Refused>(Said(outcome));
        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void AChainOfFortyLinks_Resolves_AndFortyOneIsRefused()
    {
        // The budget is 40 link expansions for one whole resolution; plain directories cost nothing.
        Chain("h", 40);
        Chain("k", 41);

        Said(Read("h1")).Should().Be($"READ: {Inside}", "forty link expansions is the budget, and it is enough");
        Read("k1").Should().BeOfType<DocumentOutcome.Refused>("forty-one expansions is one more than the budget");
    }

    /// <summary>Links <c>{name}1 → {name}2 → … → {name}{count} → docs/spec.md</c>: reading the first expands <paramref name="count"/> links.</summary>
    private void Chain(string name, int count)
    {
        for (var i = 1; i < count; i++)
        {
            Link($"{name}{i}", $"{name}{i + 1}");
        }

        Link($"{name}{count}", In("docs/spec.md"));
    }

    [Fact]
    public void ADeepPlainPath_Resolves_BecauseOnlyLinksCostTheBudget()
    {
        var deep = _root;
        for (var i = 0; i < 60; i++)
        {
            deep = Path.Combine(deep, $"d{i}");
        }

        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(deep, "note.md"), Inside);

        Said(Read(Path.Combine(deep, "note.md"))).Should().Be($"READ: {Inside}");
    }

    [Fact]
    public async Task AChainOfJunctions_OnWindows_IsRefused()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "a junction is a Windows reparse point; Unix has symbolic links, covered above");
        var launcher = new ProcessLauncher();
        await DirectoryLink.MakeAsync(launcher, In("j2"), _outside);
        await DirectoryLink.MakeAsync(launcher, In("j1"), In("j2"));
        try
        {
            NothingOutsideIsRead("j1/secret.md");
        }
        finally
        {
            DirectoryLink.Remove(In("j1"));
            DirectoryLink.Remove(In("j2"));
        }
    }

    [Fact]
    public void AListOfAFolderBehindALinkToALink_IsOutsideTheGrantedRoots()
    {
        Link("l2", _outside);
        Link("alias3", In("l2/deeper"));
        var lookup = new WorkspaceLookup([_root]);

        var served = lookup.Serve([new LookupRequest.List("list alias3", "alias3")], TestContext.Current.CancellationToken).Text;

        served.Should().Contain("outside the granted roots").And.NotContain("secret2.md");
    }
}

using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Snapshots;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The snapshot's path mapping (D6: a finding's absolute path mapped back ONLY when the manifest holds it) and the
/// read-serving <c>Resolve</c> S4's ACP shim will serve <c>fs/read_text_file</c> through: lexical normalisation,
/// containment by components, a manifest entry — and a refusal for every spelling that could reach past it.
/// </summary>
public sealed class ReviewSnapshotResolveTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private TempGitRepo _repo = null!;
    private TempDir _scratch = null!;
    private ReviewSnapshot _snapshot = null!;

    public async ValueTask InitializeAsync()
    {
        _repo = await TempGitRepo.InitAsync(_launcher, "coai-snap-repo-");
        _scratch = TempDir.For("coai-answers-snap-");
        await Write("a.txt", "alpha");
        await Write("dir/inner.txt", "inner");
        await Write("longfilename.txt", "long");
        await _repo.CommitAsync("base");
        _snapshot = await SnapshotTestKit.MadeAsync(SnapshotTestKit.Snapshots(_launcher), new SnapshotSource.Pinned(_repo.Path), _scratch);
    }

    public async ValueTask DisposeAsync()
    {
        _snapshot.Dispose();
        _scratch.Dispose();
        await _repo.DisposeAsync();
    }

    private async Task Write(string relative, string text)
    {
        var full = Path.Combine(_repo.Path, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, text);
    }

    private string Inside(string relative) => Path.Combine(_snapshot.Root, relative.Replace('/', Path.DirectorySeparatorChar));

    // ---------------------------------------------------------------------------------------
    // MapBack
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void MapBack_MapsAnAbsolutePathInsideTheSnapshot_ToTheRepositoryRelativeOne()
    {
        _snapshot.MapBack(Inside("dir/inner.txt")).Should().Be("dir/inner.txt");
        _snapshot.MapBack(Inside("a.txt")).Should().Be("a.txt");
    }

    [Fact]
    public void MapBack_RefusesAPathOutsideTheSnapshot_OrNotInTheManifest()
    {
        _snapshot.MapBack(Path.Combine(_repo.Path, "a.txt")).Should().BeEmpty("the checkout is not the snapshot");
        _snapshot.MapBack(Inside("not-there.txt")).Should().BeEmpty("only what the manifest holds maps back");
        _snapshot.MapBack(Path.Combine(_snapshot.Root + "-sibling", "a.txt")).Should().BeEmpty("a prefix of the root's spelling is not the root");
        _snapshot.MapBack("dir/inner.txt").Should().BeEmpty("a relative path is nobody's absolute path");
        _snapshot.MapBack(string.Empty).Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------
    // Resolve
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Resolve_ServesAManifestEntry_ByTheManifestsOwnSpelling()
    {
        var asked = Inside("dir/./inner.txt").Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var resolved = _snapshot.Resolve(asked);

        resolved.Should().BeOfType<SnapshotPath.Inside>().Which.Path.Should().Be(Inside("dir/inner.txt"), "the stored path, never the supplied spelling");
    }

    [Fact]
    public void Resolve_RefusesACaseVariant_WhatTheManifestDoesNotSpell()
    {
        _snapshot.Resolve(Inside("A.TXT")).Should().BeOfType<SnapshotPath.Refused>("matching is ordinal");
        _snapshot.Resolve(Inside("LONGFI~1.TXT")).Should().BeOfType<SnapshotPath.Refused>("an 8.3 short name is not a manifest entry");
    }

    public static TheoryData<string, string> RefusedForms => new()
    {
        { "", "empty" },
        { "a.txt", "relative" },
        { "C:a.txt", "drive-relative" },
        { "\\a.txt", "root-relative" },
        { "\\\\?\\C:\\x\\a.txt", "device" },
        { "\\\\.\\pipe\\x", "device" },
        { "\\\\server\\share\\a.txt", "UNC" },
        { "//server/share/a.txt", "UNC" },
    };

    [Theory]
    [MemberData(nameof(RefusedForms))]
    public void Resolve_RefusesEveryFormThatIsNotAPlainAbsolutePath(string requested, string form)
    {
        var resolved = _snapshot.Resolve(requested);

        resolved.Should().BeOfType<SnapshotPath.Refused>(form).Which.Reason.Should().NotBeEmpty();
    }

    [Fact]
    public void Resolve_RefusesAnAlternateDataStream()
    {
        _snapshot.Resolve(Inside("a.txt") + ":hidden").Should().BeOfType<SnapshotPath.Refused>().Which.Reason.Should().NotBeEmpty();
    }

    [Fact]
    public void Resolve_RefusesATrailingDotOrSpaceComponent()
    {
        _snapshot.Resolve(Inside("dir.") + Path.DirectorySeparatorChar + "inner.txt").Should().BeOfType<SnapshotPath.Refused>("Windows strips a trailing dot and would open dir");
        _snapshot.Resolve(Inside("dir ") + Path.DirectorySeparatorChar + "inner.txt").Should().BeOfType<SnapshotPath.Refused>();
        _snapshot.Resolve(Inside("a.txt.")).Should().BeOfType<SnapshotPath.Refused>();
        _snapshot.Resolve(Inside("a.txt ")).Should().BeOfType<SnapshotPath.Refused>();
    }

    [Fact]
    public void Resolve_RefusesAPathThatLeavesTheRoot_OrOneOutsideIt()
    {
        _snapshot.Resolve(Inside("dir/../../outside.txt")).Should().BeOfType<SnapshotPath.Refused>();
        _snapshot.Resolve(Path.Combine(_repo.Path, "a.txt")).Should().BeOfType<SnapshotPath.Refused>();
        _snapshot.Resolve(Path.Combine(_snapshot.Root + "-sibling", "a.txt")).Should().BeOfType<SnapshotPath.Refused>();
        _snapshot.Resolve(Inside("not-there.txt")).Should().BeOfType<SnapshotPath.Refused>();
    }

    [Fact]
    public void Resolve_ServesAPathWhoseDotDotStaysInside()
    {
        // `dir/../a.txt` is `a.txt` lexically, and `a.txt` is in the manifest — inside, by components.
        _snapshot.Resolve(Inside("dir/../a.txt")).Should().BeOfType<SnapshotPath.Inside>().Which.Path.Should().Be(Inside("a.txt"));
    }

    // ---------------------------------------------------------------------------------------
    // Read
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Read_ServesTheBytesOfAManifestEntry()
    {
        var read = _snapshot.Read(Inside("a.txt"));

        read.Should().BeOfType<SnapshotRead.Served>().Which.Bytes.Should().Equal("alpha"u8.ToArray());
    }

    [Fact]
    public void Read_RefusesWhatResolveRefuses()
    {
        _snapshot.Read(Inside("not-there.txt")).Should().BeOfType<SnapshotRead.Refused>();
        _snapshot.Read(Path.Combine(_repo.Path, "a.txt")).Should().BeOfType<SnapshotRead.Refused>();
    }

    /// <summary>
    /// A link swapped in AFTER the snapshot was made — the one way the manifest's stored path could reach outside.
    /// </summary>
    [Fact]
    public async Task ALinkSwappedInAfterTheSnapshot_IsNotFollowedByRead()
    {
        using var outside = TempDir.For("coai-snap-outside-");
        await File.WriteAllTextAsync(outside.At("inner.txt"), "OUTSIDE");
        Directory.Delete(Inside("dir"), recursive: true);
        await DirectoryLink.MakeAsync(_launcher, Inside("dir"), outside);
        try
        {
            var read = _snapshot.Read(Inside("dir/inner.txt"));

            if (read is SnapshotRead.Served served)
            {
                served.Bytes.Should().Equal("inner"u8.ToArray(), "the original bytes or a refusal — never the outside marker");
            }
            else
            {
                read.Should().BeOfType<SnapshotRead.Refused>().Which.Reason.Should().ContainEquivalentOf("link");
            }
        }
        finally
        {
            DirectoryLink.Remove(Inside("dir"));
        }

        File.ReadAllText(outside.At("inner.txt")).Should().Be("OUTSIDE", "nothing touched the target");
    }

    /// <summary>Finding 3b of the S1 review: <c>Resolve</c> answered a path under a swapped-in link, leaving the link check to <c>Read</c> alone.</summary>
    [Fact]
    public async Task Resolve_RefusesAPathUnderALinkSwappedInAfterTheSnapshot()
    {
        using var outside = TempDir.For("coai-snap-outside-");
        await File.WriteAllTextAsync(outside.At("inner.txt"), "OUTSIDE");
        Directory.Delete(Inside("dir"), recursive: true);
        await DirectoryLink.MakeAsync(_launcher, Inside("dir"), outside);
        try
        {
            _snapshot.Resolve(Inside("dir/inner.txt")).Should().BeOfType<SnapshotPath.Refused>().Which.Reason.Should().ContainEquivalentOf("link");
        }
        finally
        {
            DirectoryLink.Remove(Inside("dir"));
        }
    }

    /// <summary>Finding 3c: a component that cannot be inspected is treated as a link — the check fails closed.</summary>
    [Fact]
    public void AComponentThatCannotBeInspected_CountsAsALink()
    {
        SnapshotLinks.FirstLinkOnTheWay(_snapshot.Root, "missing/inner.txt")
            .Should().Be(Path.Combine(_snapshot.Root, "missing"), "what cannot be inspected is not known to be safe");
    }

    /// <summary>Finding D of the risk consultations: the walk checked every descendant and never the root itself.</summary>
    [Fact]
    public async Task ARootSwappedForALink_RefusesResolveAndRead()
    {
        using var outside = TempDir.For("coai-snap-outside-");
        await File.WriteAllTextAsync(outside.At("a.txt"), "OUTSIDE");
        var aside = _snapshot.Root + "-aside";
        Directory.Move(_snapshot.Root, aside);
        await DirectoryLink.MakeAsync(_launcher, _snapshot.Root, outside);
        try
        {
            _snapshot.Resolve(Inside("a.txt")).Should().BeOfType<SnapshotPath.Refused>("the root itself is a link now");
            _snapshot.Read(Inside("a.txt")).Should().BeOfType<SnapshotRead.Refused>("never the canary behind the link");
        }
        finally
        {
            DirectoryLink.Remove(_snapshot.Root);
            Directory.Move(aside, _snapshot.Root);
        }
    }

    /// <summary>Finding F: the walk and the open were two operations on a path; a link swapped in between them was followed.</summary>
    [Fact]
    public async Task ALinkSwappedInBetweenTheWalkAndTheOpen_IsNotFollowedByRead()
    {
        using var outside = TempDir.For("coai-snap-outside-");
        await File.WriteAllTextAsync(outside.At("inner.txt"), "OUTSIDE");
        _snapshot.BeforeOpen = () =>
        {
            Directory.Delete(Inside("dir"), recursive: true);
            SnapshotTestKit.LinkNow(Inside("dir"), outside);
        };
        try
        {
            var read = _snapshot.Read(Inside("dir/inner.txt"));

            read.Should().BeOfType<SnapshotRead.Refused>("what was opened is not the manifest's entry").Which.Reason.Should().NotContain("OUTSIDE");
        }
        finally
        {
            _snapshot.BeforeOpen = static () => { };
            DirectoryLink.Remove(Inside("dir"));
        }
    }

    /// <summary>Finding F: a size checked before the open is a size the file can outgrow; the read itself must be capped.</summary>
    [Fact]
    public void AFileThatGrowsPastTheCap_BeforeTheOpen_IsRefused()
    {
        _snapshot.BeforeOpen = () =>
        {
            using var grown = new FileStream(Inside("a.txt"), FileMode.Open, FileAccess.Write);
            grown.SetLength(4L * 1024 * 1024 + 1);
        };
        try
        {
            _snapshot.Read(Inside("a.txt")).Should().BeOfType<SnapshotRead.Refused>("over the cap at the moment of the read");
        }
        finally
        {
            _snapshot.BeforeOpen = static () => { };
        }
    }
}

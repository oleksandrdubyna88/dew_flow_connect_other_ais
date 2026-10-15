using CoaiMcp.Runners.Files;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The Windows flow of <see cref="PrivateFolder"/> — create, open ONE handle, verify path / owner / DACL, re-ACL through it, walk
/// and reset the children — driven through a fake <see cref="IFolderShell"/> over real folders, so every branch runs on any OS
/// (SonarCloud measures on Linux). The real shell (<c>PrivateFolderWindowsNative</c>) is exercised by
/// <see cref="PrivateFolderWindowsTests"/> on Windows. Every root is a <c>testsToDelete-vh-*</c> folder.
/// </summary>
public sealed class PrivateFolderWindowsDecisionTests : IDisposable
{
    private static readonly Sid Me = new("S-1-5-21-1111-2222-3333-1001");
    private static readonly Sid Everyone = new("S-1-1-0");

    private readonly TempDir _root = TempDir.For("testsToDelete-vh-decide-");

    public void Dispose() => _root.Dispose();

    private sealed class Handle : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    /// <summary>A shell whose security lives in a dictionary keyed by path, and whose every call is recorded.</summary>
    private sealed class FakeShell : IFolderShell
    {
        public Dictionary<string, AclFacts> Acls { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Calls { get; } = [];

        public Handle Opened { get; } = new();

        public int OpenError { get; set; }

        public string? FinalPath { get; set; }

        public bool ReplaceTakes { get; set; } = true;

        public bool ResetTakes { get; set; } = true;

        public Exception? ReadThrows { get; set; }

        public HashSet<string> Unreadable { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Sid Me => PrivateFolderWindowsDecisionTests.Me;

        public void Create(string path, AclFacts wanted)
        {
            Calls.Add("create");
            Directory.CreateDirectory(path);
            Acls[path] = wanted with { Owner = Me };
        }

        public ShellOpen Open(string path)
        {
            Calls.Add("open");
            Current = path;

            return OpenError != 0 ? new ShellOpen.Failed(OpenError) : new ShellOpen.Held(Opened);
        }

        private string Current { get; set; } = string.Empty;

        public string FinalPathOf(IDisposable handle) => FinalPath ?? Path.Combine(HandleRead.CanonicalDirectory(Path.GetDirectoryName(Current)!), Path.GetFileName(Current));

        public AclFacts Read(IDisposable handle) => ReadThrows is { } failure ? throw failure : Acls[Current];

        public bool Replace(IDisposable handle, AclFacts wanted)
        {
            Calls.Add("replace");
            if (ReplaceTakes)
            {
                Acls[Current] = wanted with { Owner = Acls[Current].Owner };
            }

            return ReplaceTakes;
        }

        public string Name(Sid who) => who == Everyone ? "Everyone" : who.Value;

        public AclFacts AclOf(string entry, bool isFolder)
        {
            if (Unreadable.Contains(entry))
            {
                throw new UnauthorizedAccessException($"{entry} is denied");
            }

            return Acls.TryGetValue(entry, out var facts) ? facts : Inherits();
        }

        public void ResetToInherit(string entry, bool isFolder)
        {
            Calls.Add($"reset {Path.GetFileName(entry)}");
            if (ResetTakes)
            {
                Acls[entry] = Inherits();
            }
        }
    }

    /// <summary>A descendant as propagation leaves it: the person, SYSTEM and Administrators, all inherited, not protected.</summary>
    private static AclFacts Inherits() => new(Me, false, true, [.. PrivateAcl.Wanted(Me).Aces.Select(ace => ace with { Inherited = true })]);

    /// <summary>A descendant with an explicit Everyone entry — what a reset must remove.</summary>
    private static AclFacts Planted(Sid owner) => new(owner, false, true, [.. Inherits().Aces, new AceFact(Everyone, true, false, 0x1200A9, AceInheritance.None)]);

    /// <summary>The DACL a private folder is made with, on a folder the person owns.</summary>
    private static AclFacts OursOwnedByMe() => PrivateAcl.Wanted(Me) with { Owner = Me };

    private static AclFacts WideFolder(Sid owner) => new(owner, true, true, [.. PrivateAcl.Wanted(Me).Aces, new AceFact(Everyone, true, false, 0x1200A9, PrivateAcl.Inheriting)]);

    private PrivateFolderWindows.Target Target(List<string>? notes = null, string name = "coai-vibe")
        => new(_root, name, Path.Combine(_root, name), (notes ?? []).Add);

    private static PrivateFolderOutcome.Opened Opened(PrivateFolderOutcome outcome) =>
        outcome.Should().BeOfType<PrivateFolderOutcome.Opened>(outcome is PrivateFolderOutcome.Refused r ? r.Sentence : string.Empty).Subject;

    private static PrivateFolderOutcome.Refused Refused(PrivateFolderOutcome outcome) => outcome.Should().BeOfType<PrivateFolderOutcome.Refused>().Subject;

    private FakeShell ExistingFolder(AclFacts acl, string name = "coai-vibe")
    {
        var path = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;

        return new FakeShell { Acls = { [path] = acl } };
    }

    [Fact]
    public void AMissingFolder_IsCreatedWithTheWantedDacl_ThenVerifiedAndHeld()
    {
        var shell = new FakeShell();

        var opened = Opened(PrivateFolderWindows.Open(shell, Target()));

        shell.Calls.Should().Equal("create", "open");
        shell.Acls[opened.Path].Should().BeEquivalentTo(OursOwnedByMe());
        opened.Held.Should().BeSameAs(shell.Opened);
        shell.Opened.Disposed.Should().BeFalse("the handle is what the reservation holds");
    }

    [Fact]
    public void AFolderThatIsExactlyOurs_IsOpenedAsItIs_WithoutACreateOrAReplace()
    {
        var shell = ExistingFolder(OursOwnedByMe());
        var notes = new List<string>();

        var opened = Opened(PrivateFolderWindows.Open(shell, Target(notes)));

        shell.Calls.Should().Equal("open");
        notes.Should().BeEmpty();
        opened.Held.Should().BeSameAs(shell.Opened);
    }

    [Fact]
    public void AFolderOwnedByAdministrators_WithTheRightDacl_IsOurs()
    {
        var shell = ExistingFolder(OursOwnedByMe() with { Owner = Sid.Administrators });

        Opened(PrivateFolderWindows.Open(shell, Target())).Held.Should().BeSameAs(shell.Opened);
    }

    [Fact]
    public void AFolderOwnedBySomeoneElse_IsRefusedNamingTheOwnerAndWhoCouldUseIt_AndTheHandleIsGivenBack()
    {
        var shell = ExistingFolder(WideFolder(Sid.System));

        var refused = Refused(PrivateFolderWindows.Open(shell, Target()));

        refused.Sentence.Should().Contain("not private to you").And.Contain("owner S-1-5-18").And.Contain("Everyone could read it").And.Contain("COAI_CONSULTANT_HOMES");
        shell.Calls.Should().Equal("open");
        shell.Opened.Disposed.Should().BeTrue();
    }

    [Fact]
    public void AWideFolderOfOurs_IsReAcledThroughTheHandle_ItsChildrenAreReset_AndOneLineSaysWhoCouldUseIt()
    {
        var shell = ExistingFolder(WideFolder(Me));
        var parent = Path.Combine(_root, "coai-vibe");
        var planted = Directory.CreateDirectory(Path.Combine(parent, "id", "h")).FullName;
        var file = Path.Combine(planted, "transcript.jsonl");
        File.WriteAllText(file, "the reviewed code");
        shell.Acls[file] = Planted(Me);
        shell.Acls[planted] = Planted(Me);
        var notes = new List<string>();

        var opened = Opened(PrivateFolderWindows.Open(shell, Target(notes)));

        shell.Acls[parent].Should().BeEquivalentTo(OursOwnedByMe(), "replaced through the handle");
        shell.Calls.Should().Contain(["replace", "reset transcript.jsonl", "reset h"]).And.NotContain("reset id", "a folder that already inherits is left alone");
        shell.Acls[file].Aces.Should().OnlyContain(ace => ace.Inherited);
        notes.Should().ContainSingle().Which.Should().Be($"made {parent} private; Everyone could read it until now");
        shell.Opened.Disposed.Should().BeFalse();
        opened.Held.Should().BeSameAs(shell.Opened);
    }

    [Fact]
    public void AWideFolderThatTheSystemWillNotReAcl_IsRefused_AndNothingBelowIsTouched()
    {
        var shell = ExistingFolder(WideFolder(Me));
        shell.ReplaceTakes = false;

        var refused = Refused(PrivateFolderWindows.Open(shell, Target()));

        refused.Sentence.Should().Contain("could not make it so").And.Contain("Everyone could read it");
        shell.Calls.Should().NotContain(call => call.StartsWith("reset", StringComparison.Ordinal));
        shell.Opened.Disposed.Should().BeTrue();
    }

    [Fact]
    public void ADaclThatTheReReadSaysIsStillWide_IsRefused_NotTrusted()
    {
        var shell = ExistingFolder(WideFolder(Me));
        var path = Path.Combine(_root, "coai-vibe");
        shell.Acls[path] = WideFolder(Me);

        // "Replace" reports success but leaves the DACL as it was: only the read-back can tell.
        var stubborn = new StubbornShell(shell);

        Refused(PrivateFolderWindows.Open(stubborn, Target())).Sentence.Should().Contain("could not make it so");
        shell.Opened.Disposed.Should().BeTrue();
    }

    /// <summary>Takes a replace and keeps the old DACL — a system that said yes and did nothing.</summary>
    private sealed class StubbornShell(FakeShell inner) : IFolderShell
    {
        public Sid Me => inner.Me;

        public void Create(string path, AclFacts wanted) => inner.Create(path, wanted);

        public ShellOpen Open(string path) => inner.Open(path);

        public string FinalPathOf(IDisposable handle) => inner.FinalPathOf(handle);

        public AclFacts Read(IDisposable handle) => inner.Read(handle);

        public bool Replace(IDisposable handle, AclFacts wanted) => true;

        public string Name(Sid who) => inner.Name(who);

        public AclFacts AclOf(string entry, bool isFolder) => inner.AclOf(entry, isFolder);

        public void ResetToInherit(string entry, bool isFolder) => inner.ResetToInherit(entry, isFolder);
    }

    [Fact]
    public void AChildOwnedBySomeoneElse_RefusesTheWholeFolder_NamingTheChild_AndNothingIsSaid()
    {
        var shell = ExistingFolder(WideFolder(Me));
        var child = Path.Combine(_root, "coai-vibe", "transcript.jsonl");
        File.WriteAllText(child, "x");
        shell.Acls[child] = Planted(Sid.System);
        var notes = new List<string>();

        var refused = Refused(PrivateFolderWindows.Open(shell, Target(notes)));

        refused.Sentence.Should().Contain(child).And.Contain("S-1-5-18").And.Contain("WRITE_DAC");
        notes.Should().BeEmpty("a folder that is refused was not made private");
        shell.Opened.Disposed.Should().BeTrue();
    }

    [Fact]
    public void AChildThatDoesNotInheritAfterTheReset_RefusesTheFolder()
    {
        var shell = ExistingFolder(WideFolder(Me));
        var child = Path.Combine(_root, "coai-vibe", "transcript.jsonl");
        File.WriteAllText(child, "x");
        shell.Acls[child] = Planted(Me);
        shell.ResetTakes = false;

        Refused(PrivateFolderWindows.Open(shell, Target())).Sentence.Should().Contain(child).And.Contain("did not inherit the three entries after a reset");
    }

    [Fact]
    public void AChildThatCannotBeRead_RefusesTheFolder_WithTheReasonTheSystemGave()
    {
        var shell = ExistingFolder(WideFolder(Me));
        var child = Path.Combine(_root, "coai-vibe", "locked.jsonl");
        File.WriteAllText(child, "x");
        shell.Unreadable.Add(child);

        Refused(PrivateFolderWindows.Open(shell, Target())).Sentence.Should().Contain(child).And.Contain("could not be made private").And.Contain("is denied");
    }

    [Fact]
    public void ALinkInsideTheFolder_IsNeverEnteredOrTouched()
    {
        var shell = ExistingFolder(WideFolder(Me));
        using var outside = TempDir.For("testsToDelete-vh-outside-");
        File.WriteAllText(outside.At("outside.txt"), "not ours");
        var link = Path.Combine(_root, "coai-vibe", "link");
        SnapshotTestKit.LinkNow(link, outside);
        try
        {
            Opened(PrivateFolderWindows.Open(shell, Target()));
        }
        finally
        {
            Directory.Delete(link);
        }

        shell.Calls.Should().NotContain(call => call.StartsWith("reset", StringComparison.Ordinal));
        shell.Acls.Keys.Should().NotContain(key => key.Contains("outside", StringComparison.Ordinal), "the link's target was not read either");
    }

    [Fact]
    public void AFolderWhoseFinalPathIsAnother_IsRefusedAsALinkOrASwap_AndTheHandleIsGivenBack()
    {
        var shell = ExistingFolder(OursOwnedByMe());
        shell.FinalPath = Path.Combine(_root, "somewhere-else");

        var refused = Refused(PrivateFolderWindows.Open(shell, Target()));

        refused.Sentence.Should().Contain("is not the folder it names").And.Contain(Path.Combine(_root, "coai-vibe"));
        shell.Opened.Disposed.Should().BeTrue();
    }

    [Fact]
    public void AFolderThatCannotBeOpened_IsRefusedWithTheErrorTheSystemGave()
    {
        var shell = ExistingFolder(OursOwnedByMe());
        shell.OpenError = PrivateAcl.SharingViolation;

        Refused(PrivateFolderWindows.Open(shell, Target())).Sentence.Should().Contain("open elsewhere");
        shell.OpenError = 5;
        Refused(PrivateFolderWindows.Open(shell, Target())).Sentence.Should().Contain("could not be opened").And.Contain("error 5");
    }

    [Fact]
    public void ALinkThatCannotBeOpened_IsSaidToBeALink()
    {
        using var target = TempDir.For("testsToDelete-vh-target-");
        var link = Path.Combine(_root, "coai-vibe");
        SnapshotTestKit.LinkNow(link, target);
        var shell = new FakeShell { OpenError = 5 };

        try
        {
            Refused(PrivateFolderWindows.Open(shell, Target())).Sentence.Should().Contain("is a link").And.Contain("error 5");
        }
        finally
        {
            Directory.Delete(link);
        }

        shell.Calls.Should().NotContain("create", "the path is there (as a link): nothing is created through it");
    }

    [Fact]
    public void AnAclThatCannotBeRead_GivesTheHandleBack_AndTheFailureReachesTheCaller()
    {
        var shell = ExistingFolder(OursOwnedByMe());
        shell.ReadThrows = new IOException("the folder's owner and DACL could not be read");

        var open = () => PrivateFolderWindows.Open(shell, Target());

        open.Should().Throw<IOException>().WithMessage("*could not be read*");
        shell.Opened.Disposed.Should().BeTrue("a handle that failed its check is never left open");
    }
}

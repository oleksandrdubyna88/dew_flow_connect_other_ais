using CoaiMcp.Runners.Files;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The one type that enters <c>&lt;homes root&gt;\coai-*</c> (D7 of <c>todo/PLAN_vibe_live_round_fixes.md</c>, per the security
/// review of 2026-10-15): the folder is made with a protected DACL of the person, SYSTEM and Administrators — or an existing one
/// is verified BY HANDLE (its path, its owner, its DACL), re-ACLed when only its DACL is wrong, and refused when its owner is
/// another — and the handle is held for the reservation's life.
/// </summary>
/// <remarks>
/// Windows only (E1–E8), plus E9 on the Unix legs. Every root is a <c>testsToDelete-vh-*</c> folder, never the machine's own. The
/// CI runner is ELEVATED, so a folder it makes may be owned by Administrators rather than the user: E1 asserts the owner is either.
/// </remarks>
public sealed class PrivateFolderTests : IDisposable
{
    private readonly TempDir _root = TempDir.For("testsToDelete-vh-private-");

    public void Dispose() => _root.Dispose();

    private static PrivateFolderOutcome.Opened Opened(PrivateFolderOutcome outcome) =>
        outcome.Should().BeOfType<PrivateFolderOutcome.Opened>(outcome is PrivateFolderOutcome.Refused r ? r.Sentence : string.Empty).Subject;

    [Fact]
    public void E8_AMissingRoot_IsRefusedWithTheDoesNotExistSentence_AndNothingIsCreated()
    {
        var missing = Path.Combine(_root, "not-made-yet");

        var refused = PrivateFolder.Open(missing, "coai-vh").Should().BeOfType<PrivateFolderOutcome.Refused>().Subject;

        refused.Sentence.Should().Contain($"the Vibe homes folder {missing} does not exist").And.Contain("COAI_CONSULTANT_HOMES");
        Directory.Exists(missing).Should().BeFalse();
    }

    [Theory]
    [InlineData(@"\\server\share\homes")]
    [InlineData(@"\\?\C:\homes")]
    [InlineData(@"relative\homes")]
    public void ARootThatIsNotAFullLocalPath_IsRefused(string root)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows paths");

        PrivateFolder.Open(root, "coai-vh").Should().BeOfType<PrivateFolderOutcome.Refused>().Which.Sentence.Should().Contain("local");
    }

    private const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>
    /// E9 on a real Unix file system: a missing folder is made 0700, and a pre-existing 0755 folder of the test user's is MIGRATED —
    /// chmod 0700, Opened, one line saying what the mode was — as the same case is re-ACLed on Windows. (The refusal needs a folder
    /// somebody else owns, which a test without root cannot make: it is covered through the seam below.)
    /// </summary>
    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void E9_OnUnix_AFolderIsMade0700_AndAWiderOneOfOursIsMigrated()
    {
        Assert.SkipUnless(!OperatingSystem.IsWindows(), "the Windows legs are E1–E8");
        var notes = new List<string>();
        using (Opened(PrivateFolder.Open(_root, "coai-vh", notes.Add)).Held)
        {
            File.GetUnixFileMode(Path.Combine(_root, "coai-vh")).Should().Be(Private);
            notes.Should().BeEmpty("a folder made private says nothing");
        }

        var wide = Directory.CreateDirectory(Path.Combine(_root, "coai-vibe"));
        File.SetUnixFileMode(wide.FullName, (UnixFileMode)0b111_101_101);

        using var held = Opened(PrivateFolder.Open(_root, "coai-vibe", notes.Add)).Held;

        File.GetUnixFileMode(wide.FullName).Should().Be(Private);
        notes.Should().ContainSingle().Which.Should().Contain(wide.FullName).And.Contain("made").And.Contain("755");
    }

    [Fact]
    public void TheUnixRule_AFolderOfOurOwnThatIsWide_IsMigratedAndSaysSo()
    {
        var notes = new List<string>();

        var outcome = PrivateFolder.UnixVerdict("/homes/coai-vh", (UnixFileMode)0b111_101_101, () => Private, notes.Add);

        outcome.Should().BeOfType<PrivateFolderOutcome.Opened>();
        notes.Should().ContainSingle().Which.Should().Contain("/homes/coai-vh").And.Contain("made").And.Contain("private").And.Contain("755");
    }

    [Fact]
    public void TheUnixRule_APrivateFolder_IsOpenedWithoutTouchingIt()
    {
        var touched = false;
        var notes = new List<string>();

        var outcome = PrivateFolder.UnixVerdict("/homes/coai-vh", Private, () =>
        {
            touched = true;

            return Private;
        }, notes.Add);

        outcome.Should().BeOfType<PrivateFolderOutcome.Opened>();
        touched.Should().BeFalse("no chmod for a folder that is already private");
        notes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(IOException))]
    public void TheUnixRule_AFolderSomebodyElseOwns_ChmodFails_AndItIsRefusedNamingTheMode(Type failure)
    {
        var outcome = PrivateFolder.UnixVerdict("/homes/coai-vh", (UnixFileMode)0b111_101_101, () => throw (Exception)Activator.CreateInstance(failure, "EPERM")!, _ => { });

        outcome.Should().BeOfType<PrivateFolderOutcome.Refused>().Which.Sentence.Should().Contain("755").And.Contain("not private to you").And.Contain("COAI_CONSULTANT_HOMES");
    }

    [Fact]
    public void TheUnixRule_AChmodThatLeavesBitsBehind_IsRefused_NotTrusted()
    {
        var notes = new List<string>();

        var outcome = PrivateFolder.UnixVerdict("/homes/coai-vh", (UnixFileMode)0b111_101_101, () => (UnixFileMode)0b111_101_000, notes.Add);

        outcome.Should().BeOfType<PrivateFolderOutcome.Refused>().Which.Sentence.Should().Contain("755");
        notes.Should().BeEmpty("nothing was made private, so nothing is said to have been");
    }

    /// <summary>
    /// The one door: no production code combines a path with the homes root or a <c>coai-*</c> folder name except
    /// <see cref="PrivateFolder"/> — so a new entry into <c>&lt;root&gt;coai-*</c> cannot skip the private-folder check.
    /// </summary>
    [Fact]
    public void EveryEntryIntoACoaiFolderUnderTheHomesRoot_GoesThroughPrivateFolder()
    {
        // Inside ONE argument list: a bare closing parenthesis ends the search, one level of nested calls is read through.
        const string pattern = "Path[.]Combine[(](?:[^;()]|[(][^()]*[)])*?(?:ConsultantHomes[.]|OneShotFolder|HomesFolder|HomesRoot|homesRoot)";
        const string theDoor = "runners/Files/PrivateFolder.cs";
        static string Slashed(string file) => file.Replace(Path.DirectorySeparatorChar, '/');

        var offenders = ProductionSources.Files()
            .Where(file => file.EndsWith(".cs", StringComparison.Ordinal) && !Slashed(file).EndsWith(theDoor, StringComparison.Ordinal))
            .Where(file => System.Text.RegularExpressions.Regex.IsMatch(ProductionSources.CodeOf(file), pattern))
            .ToList();

        offenders.Should().BeEmpty("a Path.Combine with the homes root or a coai-* folder belongs in PrivateFolder.PathOf/Open (D7)");
        ProductionSources.Files().Select(Slashed).Should().Contain(file => file.EndsWith(theDoor, StringComparison.Ordinal), "the scan really sees the type");
    }

    /// <summary>
    /// Wider than the `Path.Combine` census: a file that spells a folder under the root through the helpers (`PathOf`, `OneShotIn`,
    /// `ConversationFolder`) must also ENTER it through `PrivateFolder.Open`. The one file that only DEFINES a name (`ConsultantHomes`) is
    /// the exemption, named.
    /// </summary>
    [Fact]
    public void EveryFileThatSpellsAFolderUnderTheHomesRoot_AlsoOpensItThroughPrivateFolder()
    {
        string[] spellings = ["PrivateFolder.PathOf(", "OneShotIn(", "ConversationFolder("];
        string[] definersOfAName = ["runners/Consultation/ConsultantHomes.cs"];
        static string Slashed(string file) => file.Replace(Path.DirectorySeparatorChar, '/');

        var offenders = ProductionSources.Files()
            .Where(file => file.EndsWith(".cs", StringComparison.Ordinal) && !Slashed(file).EndsWith("runners/Files/PrivateFolder.cs", StringComparison.Ordinal))
            .Where(file => !definersOfAName.Any(one => Slashed(file).EndsWith(one, StringComparison.Ordinal)))
            .Where(file => spellings.Any(spelling => ProductionSources.CodeOf(file).Contains(spelling, StringComparison.Ordinal))
                && !ProductionSources.CodeOf(file).Contains("PrivateFolder.Open(", StringComparison.Ordinal))
            .ToList();

        offenders.Should().BeEmpty("a file that names a folder under the homes root must enter it through PrivateFolder.Open (D7)");
        ProductionSources.Files().Count(file => file.EndsWith(".cs", StringComparison.Ordinal) && spellings.Any(spelling => ProductionSources.CodeOf(file).Contains(spelling, StringComparison.Ordinal)))
            .Should().BeGreaterThan(3, "the census really sees the files that spell such a folder");
    }
}

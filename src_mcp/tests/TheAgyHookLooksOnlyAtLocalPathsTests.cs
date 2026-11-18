using System.Diagnostics;
using System.Text.RegularExpressions;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The agy hook never looks at a network or device path: not for <c>Path</c>, a root, the payload's <c>cwd</c>, the coai folder, or a link
/// target met on the way. Looking is what offers the user's credentials to a host (research/PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text_E1_S2_spec.md, W2).
/// </summary>
/// <remarks>
/// <para>The decision is <c>deny</c> whether or not the look was made, so every row asserts BOTH the decision and what the reader was handed:
/// <see cref="Recorder"/> stands in for the reader, never touches the disk for a path that is not local, and says which remote paths it saw.</para>
/// </remarks>
public sealed class TheAgyHookLooksOnlyAtLocalPathsTests : AgyHookFixture
{
    /// <summary>An <see cref="EntryReader"/> that writes down every path it is handed and never touches the disk for a remote one.</summary>
    private sealed class Recorder(Func<string, bool> isRemote, Func<string, Entry?>? standIn = null)
    {
        public List<string> Seen { get; } = [];

        public IReadOnlyList<string> Remote => [.. Seen.Where(isRemote)];

        public EntryReader Read => path =>
        {
            Seen.Add(path);

            return Look(path);
        };

        private Entry Look(string path) =>
            isRemote(path) ? Entry.Missing
            : !OperatingSystem.IsWindows() && path == "/net" ? Entry.Directory
            : standIn?.Invoke(Path.TrimEndingDirectorySeparator(path)) ?? RealPath.Lstat(path);
    }

    /// <summary>The host the tests name, spelled as a literal: what counts as a remote look does not depend on the rule under test.</summary>
    private static string RemoteHost => OperatingSystem.IsWindows() ? @"\\coai-no-such-host" : "/net/coai-no-such-host";

    /// <summary>A recorder whose remote places are the ones under <see cref="RemoteHost"/> — independent of <see cref="Local"/>, which is the rule under test.</summary>
    private static Recorder Seeing(Func<string, Entry?>? standIn = null) => new(path => path.StartsWith(RemoteHost, StringComparison.OrdinalIgnoreCase), standIn);

    /// <summary>
    /// A recorder whose remote places are the Windows spellings of a share or a device, written as LITERAL text: two leading separators of either kind (a share, a
    /// device, <c>\\?\</c>, <c>\\.\</c>, <c>//</c>), the NT prefix <c>\??\</c>, or a place under <see cref="RemoteHost"/>. Not <c>Junctions.IsLocal</c> — that is the rule under test, and a
    /// recorder that asked it would call a path remote exactly when the rule refuses it, so a rule that said "local" to everything would pass these rows.
    /// </summary>
    private static Recorder SeeingNonLocal(Func<string, Entry?>? standIn = null) => new(IsSpelledRemote, standIn);

    private static bool IsSpelledRemote(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal)
        || path.StartsWith("//", StringComparison.Ordinal)
        || path.StartsWith(@"\??\", StringComparison.Ordinal)
        || path.StartsWith(RemoteHost, StringComparison.OrdinalIgnoreCase);

    private static string RemoteFile => Path.Combine(Remote, "x.txt");

    private void DeniedWithoutALook(string payload, Recorder recorder, HookSite site, string? control = null, string? why = null)
    {
        Denied(payload, control: control, site: site, why: why);

        recorder.Remote.Should().BeEmpty($"the hook must not look at a remote path ({why ?? payload})");
    }

    private HookSite Watched(Recorder recorder, string? coaiFolder = null) => SiteAt(coaiFolder ?? Home, recorder.Read);

    [Fact]
    public void N0_TheRecorder_SeesWhatTheHookLooksAt()
    {
        var recorder = Seeing();

        Allowed(Call(Inside), site: Watched(recorder));

        recorder.Seen.Should().NotBeEmpty("the recorder is the reader the hook walks with");
    }

    [Fact]
    public void N1_APathOnANetworkPlace_IsDenied_WithoutALook()
    {
        var recorder = Seeing();

        DeniedWithoutALook(Call(RemoteFile), recorder, Watched(recorder));
    }

    [Fact]
    public void N2_ANetworkRoot_ThatMatches_IsNotSupported()
    {
        var recorder = Seeing();

        DeniedWithoutALook(Call(RemoteFile, roots: [Job1, Second, Remote]), recorder, Watched(recorder));
    }

    [Fact]
    public void N3_ANetworkRoot_ThatDoesNotMatch_DeniesTheWholePayload()
    {
        var recorder = Seeing();

        DeniedWithoutALook(Call(Inside, roots: [Job1, Second, Remote]), recorder, Watched(recorder));
    }

    [Fact]
    public void N4_ANetworkCwd_IsDenied_WithoutALook()
    {
        var recorder = Seeing();
        string[] entries = [Remote, Home, Job1];

        DeniedWithoutALook(Sent(Inside, Remote, entries).ToJsonString(), recorder, Watched(recorder), why: "a local path");
        DeniedWithoutALook(Sent(RemoteFile, Remote, entries).ToJsonString(), recorder, Watched(recorder), why: "a remote path");
    }

    [Fact]
    public void N5_ANetworkCoaiFolder_IsDenied_WithoutALook()
    {
        var recorder = Seeing();

        DeniedWithoutALook(Call(Inside), recorder, Watched(recorder, Path.Combine(Remote, "h1")));
    }

    [Fact]
    public void N6_ALinkInsideTheRoot_ToANetworkFile_IsDenied_WithoutALook()
    {
        var link = At("job-1/jl");
        var recorder = Seeing(path => path == link ? Entry.Link(RemoteFile) : null);

        DeniedWithoutALook(Call(link), recorder, Watched(recorder), why: "the link");
        DeniedWithoutALook(Call(Path.Combine(link, "deeper.txt")), recorder, Watched(recorder), why: "below the link");
    }

    [Fact]
    public void N7_ARootThatIsALink_ToANetworkPlace_IsNotSupported()
    {
        var linked = At("linked-root");
        var recorder = Seeing(path => path == linked ? Entry.Link(Remote) : null);
        string[] roots = [Job1, Second, linked];

        DeniedWithoutALook(Call(Inside, roots: roots), recorder, Watched(recorder), why: "a path inside the other root");
        DeniedWithoutALook(Call(linked, roots: roots), recorder, Watched(recorder), why: "the linked root itself");
    }

    [Fact]
    public void N8_ALinkChain_ThatEndsOnANetworkFile_IsDenied_WithoutALook()
    {
        var recorder = Seeing(path => path == At("job-1/a") ? Entry.Link(At("job-1/b")) : path == At("job-1/b") ? Entry.Link(RemoteFile) : null);

        DeniedWithoutALook(Call(At("job-1/a")), recorder, Watched(recorder));
    }

    [Fact]
    public void N9_TheGuard_ChangesNeitherAnAllowedLink_NorADeniedOne()
    {
        Link("job-1/in-link", "inside.txt");
        var recorder = Seeing(path => path == At("job-1/stood-in") ? Entry.Link(Outside) : null);

        Allowed(Call(At("job-1/in-link")), site: Watched(recorder));
        DeniedWithoutALook(Call(At("job-1/stood-in")), recorder, Watched(recorder));
    }

    [Fact]
    public void N10_TheProductsSite_KnowsWhatIsLocal()
    {
        var site = HookSite.OfThisProcess();

        site.IsLocal(RemoteFile).Should().BeFalse();
        site.IsLocal(W).Should().BeTrue();
    }

    [Theory]
    [InlineData(@"\\h\s\x", true, DriveType.Fixed, false)]
    [InlineData("//h/s/x", true, DriveType.Fixed, false)]
    [InlineData(@"\\?\C:\x", true, DriveType.Fixed, false)]
    [InlineData(@"\\.\C:\x", true, DriveType.Fixed, false)]
    [InlineData(@"\\?\UNC\h\s\x", true, DriveType.Fixed, false)]
    [InlineData(@"\??\C:\x", true, DriveType.Fixed, false)]
    [InlineData(@"Z:\x", true, DriveType.Network, false)]
    [InlineData(@"Z:\x", true, DriveType.Unknown, false)]
    [InlineData(@"Z:\x", true, DriveType.NoRootDirectory, false)]
    [InlineData("/net/h/x", false, DriveType.Fixed, false)]
    [InlineData("/misc/x", false, DriveType.Fixed, false)]
    [InlineData("//h/s", false, DriveType.Fixed, false)]
    [InlineData(@"C:\x", true, DriveType.Fixed, true)]
    [InlineData("/data/x", false, DriveType.Fixed, true)]
    public void N11_TheGuard_RefusesBeforeAnyLook_AndPassesALocalPathOn(string path, bool windows, DriveType driveType, bool local)
    {
        var calls = 0;
        EntryReader inner = _ =>
        {
            calls++;

            return Entry.Directory;
        };
        var guarded = AgyHookPlaces.Guarded(inner, text => Junctions.IsLocal(text, _ => driveType, windows));

        if (local)
        {
            guarded(path).Should().Be(Entry.Directory);
            calls.Should().Be(1);

            return;
        }

        var look = () => guarded(path);

        look.Should().Throw<IOException>().Which.Message.Should().Be(AgyHookPlaces.NotLocal).And.NotContain(path);
        calls.Should().Be(0, "the guard asks first and never calls the inner reader for a path that is not local");
    }

    [Theory]
    [InlineData(@"\\h\s\x", true, false)]
    [InlineData("//h/s/x", true, false)]
    [InlineData(@"\\?\C:\x", true, false)]
    [InlineData(@"\\.\C:\x", true, false)]
    [InlineData(@"\\?\UNC\h\s\x", true, false)]
    [InlineData(@"\??\C:\x", true, false)]
    [InlineData(@"C:x", true, false)]
    [InlineData(@"\x", true, false)]
    [InlineData("x", true, false)]
    [InlineData("", true, false)]
    [InlineData(@"C:\x", true, true)]
    [InlineData("c:/x", true, true)]
    [InlineData("/net/h/x", false, false)]
    [InlineData("/misc/x", false, false)]
    [InlineData("/Network/x", false, false)]
    [InlineData("//h/s", false, false)]
    [InlineData(@"C:x", false, false)]
    [InlineData(@"\x", false, false)]
    [InlineData("x", false, false)]
    [InlineData("", false, false)]
    [InlineData("/data/x", false, true)]
    [InlineData("/net", false, true)]
    public void N12_TheLocalityRule_IsTextAndTakesTheOs(string text, bool windows, bool local) =>
        Junctions.IsLocal(text, _ => DriveType.Fixed, windows).Should().Be(local, $"{text} on {(windows ? "Windows" : "Unix")}");

    [Fact]
    public void N13_TheGuardedReaderIsTheOnlyRoadToTheDisk_InThePlaceCheck()
    {
        var code = ProductionSources.CodeOf("src_mcp/runners/Reviewers/AgyHookPlaces.cs");

        code.Should().NotBeEmpty("the scan found the file");
        foreach (var named in new[] { @"\bFile\b", @"\bDirectory\b", @"\bFileInfo\b", @"\bDirectoryInfo\b", @"\bDriveInfo\b", @"\bLstat\b", @"\.LinkTarget\b", @"GetFullPath\(" })
        {
            Regex.IsMatch(code, named).Should().BeFalse($"the place check reaches the disk only through site.Read and the drive table only through site.IsLocal ({named})");
        }

        ProductionSources.Occurrences(code, "RealPath.Resolve(").Should().Be(1, "one method resolves, with the guarded reader");
    }

    [Theory]
    [InlineData("//x/y", true)]
    [InlineData(@"\\x\y", true)]
    [InlineData(@"/\x", true)]
    [InlineData(@"\/x", true)]
    [InlineData("/x", false)]
    [InlineData(@"\x", false)]
    [InlineData(@"C:\x", false)]
    [InlineData("a//b", false)]
    [InlineData("", false)]
    public void N14_APathThatStartsWithTwoSeparators_IsSpelledNonLocal_OnEveryOs(string text, bool two) =>
        AgyHookPlaces.StartsWithTwoSeparators(text).Should().Be(two, text);

    [Fact]
    public void N15_APathThatStartsWithTwoSeparators_IsDenied_ByItsSpelling_OnEveryOs()
    {
        // RealPath folds //tmp/x to /tmp/x before the reader sees it, so neither the guard nor the result check would refuse it on Linux.
        var doubled = OperatingSystem.IsWindows() ? @"\\" + Inside : "/" + Inside;
        var recorder = Seeing();

        DeniedWithoutALook(Call(doubled), recorder, Watched(recorder), control: Call(Inside));
    }

    [Fact]
    public void N16_TheWindowsRecorder_CallsEverySpellingRemote_AndALocalPathNot_OnEveryOs()
    {
        // The recorder's judge is its own literal text, so it is alive on every host even though rows N20-N25 run on Windows only: a judge that matched nothing would pass them for ever.
        foreach (var spelling in WindowsSpellings)
        {
            IsSpelledRemote(spelling).Should().BeTrue(spelling);
        }

        foreach (var local in new[] { Inside, W, @"C:\x", "/data/x", @"\x", "x" })
        {
            IsSpelledRemote(local).Should().BeFalse(local);
        }

        IsSpelledRemote(Path.Combine(RemoteHost, "x.txt")).Should().BeTrue();
    }

    // ---------- Windows only: the paths mean what Windows means by them ----------

    private static readonly string[] WindowsSpellings =
    [
        @"\\h\s\x", "//h/s/x", @"\\?\C:\Windows\win.ini", @"\\.\C:\Windows\win.ini", @"\\?\UNC\h\s\x", @"\\.\pipe\x", @"\??\C:\x",
    ];

    [Fact]
    public void N20_EveryWindowsSpellingOfARemoteOrDevicePath_IsDenied_WithoutALook()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "these spellings mean a share or a device only on Windows; N11 and N12 judge them as text on every host");
        var recorder = SeeingNonLocal();
        var site = SiteAt(Home, recorder.Read, HookSite.OfThisProcess().IsLocal);

        foreach (var spelling in WindowsSpellings)
        {
            DeniedWithoutALook(Call(spelling), recorder, site, why: spelling);
        }
    }

    [Fact]
    public void N21_ARealLookAtAShareThatIsNotThere_IsNeverMade_AndTheAnswerIsQuick()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "a share is a Windows spelling");
        var recorder = SeeingNonLocal();
        var site = SiteAt(Home, recorder.Read, HookSite.OfThisProcess().IsLocal);
        var clock = Stopwatch.StartNew();

        DeniedWithoutALook(Call(@"\\127.0.0.1\coai-nonexistent\x"), recorder, site);

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData(DriveType.Network)]
    [InlineData(DriveType.Unknown)]
    [InlineData(DriveType.NoRootDirectory)]
    public void N22_ARootOnAMappedNetworkDrive_IsNotSupported(DriveType type)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "a drive letter is a Windows spelling");
        var recorder = new Recorder(path => path.StartsWith("Z:", StringComparison.OrdinalIgnoreCase));
        var site = SiteAt(Home, recorder.Read, text => Junctions.IsLocal(text, root => root.StartsWith("Z", StringComparison.OrdinalIgnoreCase) ? type : DriveType.Fixed, windows: true));
        string[] roots = [Job1, Second, @"Z:\repo"];

        DeniedWithoutALook(Call(@"Z:\repo\x.txt", roots: roots), recorder, site, why: "a path on the mapped drive");
        DeniedWithoutALook(Call(Inside, roots: roots), recorder, site, why: "a path inside the local root");
    }

    [Fact]
    public void N23_ALinkWhoseTextIsABareShare_IsDenied_ByTheResultCheckAlone()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "a bare share is a Windows spelling");
        var linked = At("linked-root");
        var recorder = SeeingNonLocal(path => path == linked ? Entry.Link(@"\\h\s") : null);
        string[] roots = [Job1, Second, linked];

        DeniedWithoutALook(Call(linked, roots: roots), recorder, Watched(recorder), why: "the linked root itself");
        DeniedWithoutALook(Call(Inside, roots: roots), recorder, Watched(recorder), why: "a path inside the other root");
    }

    [Fact]
    public void N24_ARealJunctionInsideTheRoot_StillWorks()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "a junction is a Windows link");
        Junctions.Shell.MakeLink(At("job-1/jn"), At("job-1/sub")).Should().BeEmpty("a junction needs no privilege");

        Allowed(Call(At("job-1/jn/deep.txt")), site: SiteAt(Home, RealPath.Lstat, HookSite.OfThisProcess().IsLocal));
    }

    [Fact]
    public void N25_EveryWindowsSpelling_AsARoot_OrAsTheCwd_DeniesThePayload()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "these spellings mean a share or a device only on Windows");
        var recorder = SeeingNonLocal();
        var site = SiteAt(Home, recorder.Read, HookSite.OfThisProcess().IsLocal);

        foreach (var spelling in WindowsSpellings)
        {
            DeniedWithoutALook(Call(Inside, roots: [Job1, Second, spelling]), recorder, site, why: $"root {spelling}");
            DeniedWithoutALook(Sent(Inside, spelling, [spelling, Home, Job1]).ToJsonString(), recorder, site, why: $"cwd {spelling}");
        }
    }
}

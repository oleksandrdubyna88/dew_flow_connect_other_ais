using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The coai-owned Kimi home (E4.2 brief sections 8 and 14, S2.15–S2.21, S2.36–S2.38, S2.40 and S2.44 of
/// <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>): made as the junction to the person's <c>credentials</c> plus ONE coai-written
/// <c>config.toml</c> beside an empty <c>os/</c>; checked as an ALLOWLIST before every launch; disposed junction-first, with the
/// person's folder verified, and never under a live tree.
/// </summary>
/// <remarks>Every fake person's home and every junction is inside this test's own <c>testsToDelete-kimi-</c> folder — never the real <c>~/.kimi-code</c>.</remarks>
public sealed class KimiHomeTests : IDisposable
{
    private readonly KimiFixture _person = new();

    private KimiPlaces Places => _person.Places();

    public void Dispose() => _person.Dispose();

    private static void OnWindows() => Assert.SkipUnless(OperatingSystem.IsWindows(), "the owned home is a Windows shape (D18 pins the other hosts in S1.4)");

    private byte[] Expected()
    {
        var slot = ((SlotRead.Found)KimiPersonsConfig.ReadFile(_person.PersonsConfig)).Slot;
        var text = File.ReadAllText(_person.PersonsConfig);
        var block = ((BlockRead.Found)KimiModelBlocks.For(KimiModels.Default, text, ((BlocksLoad.Loaded)KimiModelBlocks.Builtin).Blocks)).Block;

        return KimiConfig.Bytes(KimiConfig.Render(slot, block));
    }

    private KimiPlaces Made(string rootName = "kh-x")
    {
        var places = _person.Places(rootName);
        KimiHome.Made(HostKind.Windows, places, Expected()).Should().BeEmpty();

        return places;
    }

    private static void Write(string path, string text = "x")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private void ThePersonsFolderIsWhole(IReadOnlyList<string> before) =>
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and attributes as before");

    // ---------- S2.15 ----------

    [Fact]
    public void AFreshHome_IsMade_WithTheJunctionTheConfigAndAnEmptyOs()
    {
        OnWindows();
        var before = _person.Hash();
        var places = Places;

        var made = KimiHome.Made(HostKind.Windows, places, Expected());

        made.Should().BeEmpty();
        OwnedPaths.IsLinkIfThere(places.Link).Should().BeTrue("credentials is a link");
        Junctions.SameFolder(Junctions.Target(places.Link), _person.Credentials).Should().BeTrue();
        File.ReadAllBytes(places.Config).Should().Equal(Expected());
        Directory.Exists(places.Os).Should().BeTrue();
        Directory.EnumerateFileSystemEntries(places.Os).Should().BeEmpty();
        ThePersonsFolderIsWhole(before);
    }

    [Fact]
    public void ANonWindowsHost_MakesNothing()
    {
        var places = _person.Places("kh-linux");

        KimiHome.Made(HostKind.Linux, places, Expected()).Should().Contain("D18").And.Contain("sign-in could not be linked");
        File.Exists(places.Config).Should().BeFalse("nothing is written when the junction was refused");
    }

    [Fact]
    public void AnExistingHome_IsNotWrittenToAgain()
    {
        OnWindows();
        var places = Made();
        File.WriteAllText(places.Config, "tampered");

        KimiHome.Made(HostKind.Windows, places, Expected()).Should().BeEmpty("the validation inside the turn judges it, not the maker");
        File.ReadAllText(places.Config).Should().Be("tampered");
    }

    [Fact]
    public void AnOwnedRootThatIsALink_IsRefused_NothingMade()
    {
        OnWindows();
        var places = _person.Places("kh-linked");
        var elsewhere = Path.Combine(_person.Folder, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        Directory.CreateDirectory(Path.GetDirectoryName(places.OwnedRoot)!);
        Junctions.Make(HostKind.Windows, places.OwnedRoot, elsewhere).Should().BeEmpty();

        KimiHome.Made(HostKind.Windows, places, Expected()).Should().Contain("is a link").And.Contain(places.OwnedRoot);
        Directory.EnumerateFileSystemEntries(elsewhere).Should().BeEmpty("nothing was made through the link");
    }

    // ---------- S2.16 ----------

    private static readonly string[] Inventory =
    [
        "cache/query-store/cluster.meta.json", "logs/kimi-code.log", "session_index.jsonl", "sessions/.index-dirty/a.b",
        "sessions/wd_x_y/session_z/agents/main/wire.jsonl", "workspaces.json", "user-history/h", "file-history/f", "telemetry/t", "updates/latest.json",
        "device_id", // Kimi writes it when a launch's token refresh rotates the sign-in (M-K15, RESULTS section 27)
    ];

    [Fact]
    public void AMadeHome_Passes_AndAHomeKimiWroteDataInto_Passes()
    {
        OnWindows();
        var places = Made();
        var expected = Expected();

        KimiHome.Refusal(places, expected).Should().BeEmpty();
        foreach (var entry in Inventory)
        {
            Write(Path.Combine(places.Home, entry.Replace('/', Path.DirectorySeparatorChar)));
        }

        Directory.CreateDirectory(Path.Combine(places.Home, "workspace-trust"));

        KimiHome.Refusal(places, expected).Should().BeEmpty("the inventory Kimi 2.1.1 writes into its home (RESULTS section 18) is admitted");
    }

    // ---------- S2.17 ----------

    public static TheoryData<string> Forbidden => new()
    {
        "mcp.json", "tui.toml", "skills/s/SKILL.md", "agents/a.md", "AGENTS.md", "agents.md", "hooks.toml", "hooks/h", "plugins/p", "themes/t",
        "local.toml", "region", "migrations-effort.json", "statusline.json", "workspace-trust/wd_x", "notes.txt", "cache/x/link",
    };

    [Theory]
    [MemberData(nameof(Forbidden))]
    public async Task EveryForbiddenName_RefusesNamingThePath(string row)
    {
        OnWindows();
        var places = Made();
        var path = Path.Combine(places.Home, row.Replace('/', Path.DirectorySeparatorChar));
        if (row == "cache/x/link")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await DirectoryLink.MakeAsync(new ProcessLauncher(), path, Path.Combine(_person.Folder, "persons"));
        }
        else
        {
            Write(path);
        }

        var refusal = KimiHome.Refusal(places, Expected());

        var named = row.Split('/')[0] == "workspace-trust" || row == "cache/x/link" ? row : row.Split('/')[0];
        refusal.Should().Contain(named, "the sentence names the path").And.Contain("delete " + places.OwnedRoot).And.Contain("D19");
        if (row == "cache/x/link")
        {
            refusal.Should().Contain("is a link");
        }
    }

    // ---------- S2.18 ----------

    [Fact]
    public void ABinFolder_RefusesWithTheRipgrepCure()
    {
        OnWindows();
        var places = Made();
        Write(Path.Combine(places.Home, "bin", "rg.exe"));

        KimiHome.Refusal(places, Expected()).Should().Contain("bin/").And.Contain("downloaded ripgrep or fd").And.Contain("rg and fd").And.Contain("winget");
    }

    // ---------- S2.19 ----------

    [Theory]
    [InlineData("real")]
    [InlineData("absent")]
    [InlineData("elsewhere")]
    public async Task ACredentialsThatIsReal_Absent_OrElsewhere_Refuses(string row)
    {
        OnWindows();
        var places = Made();
        DirectoryLink.Remove(places.Link);
        if (row == "real")
        {
            Write(Path.Combine(places.Link, "kimi-code.json"));
        }
        else if (row == "elsewhere")
        {
            var other = Path.Combine(_person.Folder, "persons2", "credentials");
            Directory.CreateDirectory(other);
            await DirectoryLink.MakeAsync(new ProcessLauncher(), places.Link, other);
        }

        var refusal = KimiHome.Refusal(places, Expected());

        refusal.Should().Contain(row switch
        {
            "real" => "credentials is a real folder, not the junction to",
            "absent" => "has no credentials junction",
            _ => "credentials points at",
        });
    }

    // ---------- S2.20 ----------

    [Theory]
    [InlineData("one byte")]
    [InlineData("crlf")]
    [InlineData("a re-login")]
    public void AConfigWhoseBytesDiffer_RefusesWithTheReLoginCure(string row)
    {
        OnWindows();
        var places = Made();
        var expected = Expected();
        switch (row)
        {
            case "one byte":
                File.WriteAllBytes(places.Config, [.. expected.Skip(1)]);
                break;
            case "crlf":
                File.WriteAllText(places.Config, System.Text.Encoding.UTF8.GetString(expected).Replace("\n", "\r\n"));
                break;
            default:
                expected = KimiConfig.Bytes(System.Text.Encoding.UTF8.GetString(expected).Replace("oauth/kimi-code-env-x", "oauth/kimi-code-env-y"));
                break;
        }

        KimiHome.Refusal(places, expected).Should().Contain("config.toml is not what coai writes").And.Contain("re-login");
    }

    // ---------- S2.21 ----------

    [Theory]
    [InlineData("os file")]
    [InlineData("os missing")]
    [InlineData("home is a link")]
    public async Task AnOsFolderThatReceivedAFile_OrIsMissing_OrAHomeThatIsALink_Refuses(string row)
    {
        OnWindows();
        var places = Made();
        switch (row)
        {
            case "os file":
                Write(Path.Combine(places.Os, ".agents", "AGENTS.md"));
                break;
            case "os missing":
                Directory.Delete(places.Os);
                break;
            default:
                var real = Path.Combine(_person.Folder, "real-home");
                Directory.Move(places.Home, real);
                await DirectoryLink.MakeAsync(new ProcessLauncher(), places.Home, real);
                break;
        }

        var refusal = KimiHome.Refusal(places, Expected());

        refusal.Should().Contain(row switch
        {
            "os file" => ".agents",
            "os missing" => "os is missing",
            _ => "is a link",
        });
    }

    [Fact]
    public void ARootHoldingMoreThanHomeAndOs_IsJudgedOnHomeAndOsAlone()
    {
        OnWindows();
        var places = Made();
        Write(Path.Combine(places.OwnedRoot, "tree", "Parser.cs"));
        Write(Path.Combine(places.OwnedRoot, "coai-conversation.txt"));
        Write(Path.Combine(places.OwnedRoot, KimiCredentialsLock.HomeHoldName), string.Empty);

        KimiHome.Refusal(places, Expected()).Should().BeEmpty("Kimi never reads the root (decision 8)");
    }

    [Fact]
    public void AHomeNobodyCanRead_IsRefusedRatherThanStartedOn()
    {
        OnWindows();
        var places = Made();
        using var hold = File.Open(places.Config, FileMode.Open, FileAccess.Read, FileShare.None);

        KimiHome.Refusal(places, Expected()).Should().NotBeEmpty("a read failure is a refusal, never a start on a home nobody checked");
    }

    [Fact]
    public void TheHomeFor_IsKhPlusTenHexOfTheSameHashRuleAsVibes()
    {
        var home = KimiHome.HomeFor(@"C:\scratch", "kimi", "architecture");

        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Path.GetFileName(home)[3..].Should().Be(Path.GetFileName(Runners.Vibe.VibeRuntime.HomeFor(@"C:\scratch", "kimi", "architecture"))[3..]);
        Path.GetDirectoryName(home).Should().Be(@"C:\scratch");
    }

    // ---------- S2.36 – S2.38 ----------

    [Fact]
    public void TheDisposal_RemovesTheJunctionFirst_VerifiesTheTarget_ThenTheRest()
    {
        OnWindows();
        var before = _person.Hash();
        var places = Made();
        Write(Path.Combine(places.Home, "sessions", "x"));
        var gitScratchMetALink = false;
        GitScratch.AfterUnlink = _ => gitScratchMetALink = true;
        try
        {
            var disposal = KimiHome.Dispose(places.OwnedRoot, _ => { });

            disposal.Should().Be(new Disposal.Clean(true));
        }
        finally
        {
            GitScratch.AfterUnlink = null;
        }

        Directory.Exists(places.OwnedRoot).Should().BeFalse();
        gitScratchMetALink.Should().BeFalse("the deleter met no link — the junction went first, by KimiHome's own explicit step");
        ThePersonsFolderIsWhole(before);
    }

    [Fact]
    public void AVerificationThatFails_StopsTheDisposal_WithTheSentence()
    {
        OnWindows();
        var places = Made();
        Write(Path.Combine(places.Home, "sessions", "x"));
        var lost = Path.Combine(_person.Credentials, "kimi-code.json");
        KimiHome.AfterUnlink = _ =>
        {
            File.SetAttributes(lost, FileAttributes.Normal);
            File.Delete(lost);
        };
        try
        {
            var disposal = KimiHome.Dispose(places.OwnedRoot, _ => { });

            var stopped = disposal.Should().BeOfType<Disposal.Stopped>($"the disposal was {disposal}").Subject;
            stopped.Reason.Should().Contain("kimi-code.json").And.Contain(_person.Credentials);
        }
        finally
        {
            KimiHome.AfterUnlink = null;
        }

        File.Exists(places.Config).Should().BeTrue("nothing more was deleted");
        File.Exists(Path.Combine(places.Home, "sessions", "x")).Should().BeTrue();
        OwnedPaths.IsLinkIfThere(places.Link).Should().BeFalse("the link itself is gone");
    }

    [Theory]
    [InlineData("junction already gone")]
    [InlineData("credentials is real")]
    public void AHomeWhoseJunctionIsGone_OrWhoseCredentialsIsReal_GoesWhole(string row)
    {
        OnWindows();
        var before = _person.Hash();
        var places = Made();
        DirectoryLink.Remove(places.Link);
        if (row == "credentials is real")
        {
            Write(Path.Combine(places.Link, "kimi-code.json"));
            File.SetAttributes(Path.Combine(places.Link, "kimi-code.json"), FileAttributes.ReadOnly);
        }

        var disposal = KimiHome.Dispose(places.OwnedRoot, _ => { });

        disposal.Should().Be(new Disposal.Clean(false));
        Directory.Exists(places.OwnedRoot).Should().BeFalse();
        ThePersonsFolderIsWhole(before);
    }

    [Fact]
    public void ARootThatIsNotThere_IsAlreadyClean()
    {
        KimiHome.Dispose(Path.Combine(_person.Folder, "owned", "kh-never"), _ => { }).Should().Be(new Disposal.Clean(false));
    }

    // ---------- S2.40 ----------

    [Fact]
    public void DisposeAllUnder_FindsTheFolderItselfAndEveryKhChild()
    {
        OnWindows();
        var before = _person.Hash();
        var folder = Path.Combine(_person.Folder, "conversation");
        var places = new[] { folder, Path.Combine(folder, "kh-aaaaaaaaaa") }.Select(root => new KimiPlaces(root, _person.PersonsHome, _person.Bin, [], string.Empty, "kimi")).ToList();
        foreach (var each in places)
        {
            KimiHome.Made(HostKind.Windows, each, Expected()).Should().BeEmpty();
        }

        Directory.CreateDirectory(Path.Combine(folder, "kh-bbbbbbbbbb"));
        Directory.CreateDirectory(Path.Combine(folder, "vh-cccccccccc"));
        Directory.CreateDirectory(Path.Combine(folder, "tree"));
        var gitScratchMetALink = false;
        GitScratch.AfterUnlink = _ => gitScratchMetALink = true;
        try
        {
            KimiHome.DisposeAllUnder(folder, _ => { }).Should().Be(3);
        }
        finally
        {
            GitScratch.AfterUnlink = null;
        }

        places.Should().OnlyContain(each => !OwnedPaths.IsLinkIfThere(each.Link), "both junctions are gone");
        gitScratchMetALink.Should().BeFalse();
        Directory.Exists(Path.Combine(folder, "vh-cccccccccc")).Should().BeTrue("left for the caller's deleter");
        Directory.Exists(Path.Combine(folder, "tree")).Should().BeTrue();
        ThePersonsFolderIsWhole(before);
    }

    // ---------- M2: never unlink THROUGH a link ----------

    /// <summary>An outside folder shaped like a home — <c>home/credentials</c> a junction to the fake person's — that a link under the swept folder points at.</summary>
    private async Task<KimiPlaces> OutsideHomeAsync()
    {
        var outside = new KimiPlaces(Path.Combine(_person.Folder, "outside-root"), _person.PersonsHome, _person.Bin, [], string.Empty, "kimi");
        KimiHome.Made(HostKind.Windows, outside, Expected()).Should().BeEmpty();
        await Task.CompletedTask;

        return outside;
    }

    [Fact]
    public async Task AKhChildThatIsALink_IsNeverUnlinkedThrough()
    {
        OnWindows();
        var outside = await OutsideHomeAsync();
        var folder = Path.Combine(_person.Folder, "conversation");
        Directory.CreateDirectory(folder);
        var child = Path.Combine(folder, "kh-aaaaaaaaaa");
        await DirectoryLink.MakeAsync(new ProcessLauncher(), child, outside.OwnedRoot);
        var before = _person.Hash();

        var count = KimiHome.DisposeAllUnder(folder, _ => { });
        var direct = KimiHome.Dispose(child, _ => { });

        OwnedPaths.IsLinkIfThere(outside.Link).Should().BeTrue("the junction OUTSIDE the swept folder is untouched");
        count.Should().Be(0, "a link is no home of this folder's to unlink in");
        direct.Should().BeOfType<Disposal.Stopped>().Which.Reason.Should().Contain("is a link").And.Contain(child);
        File.Exists(outside.Config).Should().BeTrue();
        ThePersonsFolderIsWhole(before);
        DirectoryLink.Remove(child);
    }

    [Fact]
    public async Task AHomeFolderThatIsALink_IsNeverUnlinkedThrough()
    {
        OnWindows();
        var outside = await OutsideHomeAsync();
        var root = Path.Combine(_person.Folder, "owned", "kh-linkedhome");
        Directory.CreateDirectory(root);
        await DirectoryLink.MakeAsync(new ProcessLauncher(), Path.Combine(root, "home"), outside.Home);

        var disposal = KimiHome.Dispose(root, _ => { });

        OwnedPaths.IsLinkIfThere(outside.Link).Should().BeTrue("home/ is a link to a folder holding a credentials junction: it is not followed");
        disposal.Should().BeOfType<Disposal.Stopped>().Which.Reason.Should().Contain("is a link");
        KimiHome.DisposeAllUnder(root, _ => { }).Should().Be(0);
        OwnedPaths.IsLinkIfThere(outside.Link).Should().BeTrue();
        DirectoryLink.Remove(Path.Combine(root, "home"));
    }

    // ---------- S2.44 ----------

    [Fact]
    public void ADisposalUnderALiveTree_IsStopped_AndTheHomeIsKept()
    {
        OnWindows();
        var before = _person.Hash();
        var places = Made();
        var finished = Made("kh-b");
        var holdPath = Path.Combine(places.OwnedRoot, KimiCredentialsLock.HomeHoldName);
        var read = new List<string>();
        GitScratch.Reading = read.Add;
        try
        {
            using (var held = ((Hold.Taken)HeldFile.Take(holdPath)).Stream)
            {
                var disposal = KimiHome.Dispose(places.OwnedRoot, _ => { });

                var stopped = disposal.Should().BeOfType<Disposal.Stopped>($"the disposal was {disposal}").Subject;
                stopped.Reason.Should().Contain("still running").And.Contain(places.OwnedRoot).And.Contain(KimiCredentialsLock.HomeHoldName);
                read.Should().BeEmpty("nothing outside coai's tree was opened before the Stopped");
                OwnedPaths.IsLinkIfThere(places.Link).Should().BeTrue("the junction is still a link");
                File.Exists(places.Config).Should().BeTrue();
                ThePersonsFolderIsWhole(before);
                KimiHome.Dispose(finished.OwnedRoot, _ => { }).Should().Be(new Disposal.Clean(true), "the probe is per HOME: another home's tree does not stop it");
                Directory.Exists(finished.OwnedRoot).Should().BeFalse();
            }
        }
        finally
        {
            GitScratch.Reading = null;
        }

        KimiHome.Dispose(places.OwnedRoot, _ => { }).Should().Be(new Disposal.Clean(true), "the hold released, the next disposal finishes");
        ThePersonsFolderIsWhole(before);
    }

    [Fact]
    public void ATreeFileNobodyCanOpen_IsNotKnownToBeFree()
    {
        OnWindows();
        var places = Made();
        Directory.CreateDirectory(Path.Combine(places.OwnedRoot, KimiCredentialsLock.HomeHoldName));

        KimiHome.Dispose(places.OwnedRoot, _ => { }).Should().BeOfType<Disposal.Stopped>("a kept home costs a beat where a wrong unlink costs a sign-in");
        OwnedPaths.IsLinkIfThere(places.Link).Should().BeTrue();
    }
}

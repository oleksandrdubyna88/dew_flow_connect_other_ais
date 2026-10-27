using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Vibe;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The Vibe launch control on a WSL2 host (DL1, DL2, DL3 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>; S-V1, S-V2, S-V8, S-V9, S-V15,
/// S-V16, S-V17): the sign-in is LINKED into the owned home — after the shape check, before <c>--version</c> — the manifest entry is the
/// host's platform's, and a link that cannot be made costs the launch its sign-in and nothing more.
/// </summary>
/// <remarks>
/// A scripted launcher stands in for Vibe (<see cref="FakeVibe"/>). Every arm that needs only the launch control's DECISIONS runs on every
/// host, with the disk steps stood in (<see cref="FakeSignInDisk"/>); the arms that need a real symbolic link and a real descriptor judge run on
/// Linux and skip elsewhere. What this does NOT prove: a real Vibe, a real WSL kernel, the person's real <c>~/.vibe/.env</c>.
/// </remarks>
public sealed class AVibeLaunchOnWslLinksTheSignInTests : IDisposable
{
    private const string Persons = "/home/p/.vibe/.env";

    private readonly TempDir _scratch = TempDir.For("testsToDelete-vlw-");
    private readonly TempDir _work = TempDir.For("testsToDelete-vlw-work-");

    public void Dispose()
    {
        _scratch.Dispose();
        _work.Dispose();
    }

    private ReviewerInvocation Invocation(VibeHost host) =>
        new VibeRuntime("vibe", host, VibePathBudget.None).Build(RoleCatalog.ArchitectureRole, "review this", _work, "/s.json", _scratch,
            new ReviewerSettings("vibe") { Model = "glm-5-3", OwnedScratch = _scratch, Timeout = TimeSpan.FromMinutes(1) });

    private static string HomeOf(ReviewerInvocation invocation) => invocation.Request.Environment[VibeLaunch.HomeVariable]!;

    private static VibeHost Wsl2(VibeSignIn signIn) => new(HostKind.Wsl, WslGeneration.Wsl2, signIn);

    private static VibeSignIn StoodIn(FakeSignInDisk disk) => new(Persons) { Steps = disk.Steps };

    private static Task<LaunchTurn> TakeAsync(VibeHost host, ReviewerInvocation invocation, FakeVibe vibe) =>
        VibeLaunch.TakeAsync(host, invocation.Request, FakeVibe.ExportOf(invocation.Request), vibe, VibePathBudget.None, TestContext.Current.CancellationToken);

    [Fact]
    public async Task AWslHost_WithASignIn_Starts_AndTheLinkIsMadeBeforeVersionIsAsked()
    {
        var disk = new FakeSignInDisk();
        var host = Wsl2(StoodIn(disk));
        var invocation = Invocation(host);
        var linkedWhenVersionWasAsked = false;
        var vibe = new FakeVibe { WhileTheVersionIsAsked = _ => linkedWhenVersionWasAsked = disk.Links.Count == 1 };

        using var turn = await TakeAsync(host, invocation, vibe);

        turn.Refusal.Should().BeEmpty("a Vibe launch on a wsl host should start, but was refused");
        disk.Links.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>(Path.Combine(HomeOf(invocation), ".env"), Persons));
        linkedWhenVersionWasAsked.Should().BeTrue("the link is made after the shape check and BEFORE --version, which may read the sign-in");
        turn.Warning.Should().BeEmpty("a linked sign-in and a measured version carry no warning");
    }

    /// <summary>The person's file changed between two launches on one home: the second is refused before `--version` — nothing of Vibe's starts on an un-judged sign-in.</summary>
    [Fact]
    public async Task ASecondLaunchOnTheSameHome_JudgesTheSignInAgain_AndRefusesBeforeVersion()
    {
        var disk = new FakeSignInDisk();
        var host = Wsl2(StoodIn(disk));
        var invocation = Invocation(host);
        using (var first = await TakeAsync(host, invocation, new FakeVibe()))
        {
            first.Refusal.Should().BeEmpty();
        }

        disk.Judged = new FileReadResult.Content("MISTRAL_API_KEY=sk-test-canary\nVIBE_MCP_SERVERS=[]\n"u8.ToArray());
        var vibe = new FakeVibe();

        using var second = await TakeAsync(host, invocation, vibe);

        second.Refusal.Should().Contain("line 2").And.NotContain("VIBE_MCP_SERVERS");
        vibe.Started.Should().BeEmpty("the sign-in changed under the link and the turn ran un-judged");
    }
    [Fact]
    public async Task ATargetThatIsNotASignInCoaiLinks_RefusesTheLaunch_BeforeAnythingOfVibesStarts()
    {
        var disk = new FakeSignInDisk { Judged = new FileReadResult.Content("VIBE_MCP_SERVERS=[]\n"u8.ToArray()) };
        var host = Wsl2(StoodIn(disk));
        var vibe = new FakeVibe();

        using var turn = await TakeAsync(host, Invocation(host), vibe);

        turn.Refusal.Should().Contain("line 1").And.Contain(Persons).And.NotContain("VIBE_MCP_SERVERS");
        vibe.Started.Should().BeEmpty("not even --version runs on a home whose sign-in was refused");
        disk.LinkCalls.Should().Be(0);
    }

    /// <summary>S-V15, the launch half: no `.env` link — the launch goes on signed out, and the person is told WHY, as the launch's warning.</summary>
    [Fact]
    public async Task WhenTheLinkCannotBeMade_VibeRunsSignedOut_AndTheWarningSaysWhy()
    {
        var disk = new FakeSignInDisk { LinkThrows = new IOException("Operation not supported") };
        var host = Wsl2(StoodIn(disk));
        var invocation = Invocation(host);

        using var turn = await TakeAsync(host, invocation, new FakeVibe());

        turn.Refusal.Should().BeEmpty("the launch goes on; Vibe answers signed out in its own measured words");
        turn.Warning.Should().Contain("could not be made").And.Contain("Operation not supported", "the warning did not say why the .env link could not be made");
        disk.Links.Should().BeEmpty("a half-made .env was left in the home");
        Directory.Exists(HomeOf(invocation)).Should().BeTrue();
        Directory.GetFiles(HomeOf(invocation)).Select(Path.GetFileName).Should().NotContain(".env", "a copy of .env was made as a fallback");
    }

    [Fact]
    public async Task WhenThePersonHasNoSignInFile_VibeRunsSignedOut_AndTheWarningNamesTheSetup()
    {
        var disk = new FakeSignInDisk { Judged = new FileReadResult.Missing() };
        var host = Wsl2(StoodIn(disk));

        using var turn = await TakeAsync(host, Invocation(host), new FakeVibe());

        turn.Refusal.Should().BeEmpty();
        turn.Warning.Should().Contain(Persons).And.Contain("vibe --setup");
    }

    /// <summary>DL2 where: the person's Vibe home is VIBE_HOME of coai's OWN environment when set, else ~/.vibe — and a Windows host has no link at all.</summary>
    [Fact]
    public void TheSignIn_IsTheEnvFileOfThePersonsVibeHome_AndNoneOnWindows()
    {
        Func<string, string?> nothing = _ => null;
        Func<string, string?> redirected = name => name == "VIBE_HOME" ? "/data/vibe" : null;

        VibeSignIn.For(HostKind.Windows, nothing, "/home/p").Should().Be(VibeSignIn.None, "on Windows the sign-in is in the keyring: no link is ever made or admitted");
        VibeSignIn.For(HostKind.Wsl, nothing, "/home/p").PersonsEnvFile.Should().Be(Path.GetFullPath(Path.Combine("/home/p", ".vibe", ".env")));
        VibeSignIn.For(HostKind.Wsl, redirected, "/home/p").PersonsEnvFile.Should().Be(Path.GetFullPath(Path.Combine("/data/vibe", ".env")));

        // A relative or unexpanded VIBE_HOME would resolve against coai-mcp's cwd - often the reviewed repository, whose committed .vibe/.env would be linked.
        foreach (var unsafeHome in new[] { ".vibe", "~/.vibe", "relative/dir", "$HOME/.vibe", "  " })
        {
            VibeSignIn.For(HostKind.Wsl, name => name == "VIBE_HOME" ? unsafeHome : null, "/home/p").PersonsEnvFile
                .Should().Be(Path.GetFullPath(Path.Combine("/home/p", ".vibe", ".env")), $"VIBE_HOME='{unsafeHome}' is not an absolute folder: the person's home is ~/.vibe");
        }
        VibeSignIn.For(HostKind.Wsl, nothing, string.Empty).Should().Be(VibeSignIn.None, "no profile, no Vibe home to name");
        VibeSignIn.For(HostKind.Wsl, nothing, "/home/p").Admitted.Should().Be(new AdmittedLink(".env", VibeSignIn.For(HostKind.Wsl, nothing, "/home/p").PersonsEnvFile));
        VibeSignIn.None.Admitted.Should().Be(AdmittedLink.None);
    }
    /// <summary>S-V17, the launch half: a WSL2 banner starts; a native Linux host and a WSL1 host are refused before anything starts.</summary>
    [Theory]
    [InlineData(HostKind.Wsl, WslGeneration.Wsl2, true)]
    [InlineData(HostKind.Wsl, WslGeneration.Wsl1, false)]
    [InlineData(HostKind.Linux, WslGeneration.None, false)]
    [InlineData(HostKind.MacOs, WslGeneration.None, false)]
    public async Task OnlyAWsl2Host_StartsVibe(HostKind kind, WslGeneration generation, bool starts)
    {
        var host = new VibeHost(kind, generation, VibeSignIn.None);
        var vibe = new FakeVibe();

        using var turn = await TakeAsync(host, Invocation(host), vibe);

        (turn.Refusal.Length == 0).Should().Be(starts, "a WSL1 banner was admitted as WSL2, or a native Linux host started");
        (vibe.Started.Count > 0).Should().Be(starts);
    }

    // ----------------------------------------------------------------------------- DL3: the manifest entry is the platform's

    private static VibeHomeManifest Shipped => ((VibeManifestLoad.Loaded)VibeHomeManifest.Builtin).Manifest;

    /// <summary>S-V8: a Linux Vibe 2.26.1 is a MEASURED version — checked against the linux entry, with no warning on a fresh home.</summary>
    [Fact]
    public async Task ALinuxHost_OnVibe2261_IsMeasured_AndAFreshHomeCarriesNoWarning()
    {
        var host = Wsl2(VibeSignIn.None);

        using var turn = await TakeAsync(host, Invocation(host), new FakeVibe());

        turn.Refusal.Should().BeEmpty();
        turn.Warning.Should().BeEmpty("a Linux 2.26.1 is measured: its own entry, not the win32 hashes");
    }

    [Fact]
    public async Task ALinuxHost_WithATamperedPluginFile_IsRefused_WithThePath_AgainstTheLinuxEntrysHashes()
    {
        var host = Wsl2(VibeSignIn.None);
        var invocation = Invocation(host);
        var windows = Shipped.For("2.26.1", "win32")[0].Pinned.Select(file => file.Path).ToHashSet();
        var linuxOnly = Shipped.For("2.26.1", "linux")[0].Pinned.Select(file => file.Path).First(path => !windows.Contains(path) && path.Contains("/blobs/", StringComparison.Ordinal));
        var file = Path.Combine([HomeOf(invocation), .. linuxOnly.Split('/')]);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "tampered", TestContext.Current.CancellationToken);

        using var turn = await TakeAsync(host, invocation, new FakeVibe());

        turn.Refusal.Should().Contain(linuxOnly).And.Contain("bytes", "a path only the linux entry pins is judged by its hash there, and refused");
    }

    /// <summary>S-V9: a version measured on ANOTHER platform only is unmeasured here — warn and run, and say where it was measured.</summary>
    [Fact]
    public void AVersionMeasuredOnWin32Only_IsRunOnLinux_WithTheSharedWarningNamingWin32()
    {
        var onlyWindows = Shipped with { Versions = [.. Shipped.Versions.Where(entry => entry.Platform == "win32")] };

        var verdict = VibeLaunch.Verdict(onlyWindows, _scratch, "2.26.1", "linux", AdmittedLink.None);

        verdict.Refusal.Should().BeEmpty();
        verdict.Warning.Should().Contain("2.26.1 (win32)").And.Contain("is not a version coai has measured");
        VibeLaunch.VersionWarning(onlyWindows, "2.26.1", "linux").Should().Be(verdict.Warning, "the card and the launch say the same sentence");
        VibeLaunch.VersionWarning(onlyWindows, "2.26.1", "win32").Should().BeEmpty();
    }

    // ----------------------------------------------------------------------------- the real link, on Linux

    private static void OwnerOnly(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static void OnLinux() => Assert.SkipUnless(OperatingSystem.IsLinux(), "a real symbolic link and a real descriptor judge: runs on the Linux legs");

    /// <summary>S-V1 and S-V2 with the REAL steps: the home holds `.env` → the person's file, and the next launch on the same home admits it.</summary>
    [Fact]
    public async Task OnLinux_TheRealLink_IsMade_AndTheNextLaunchOnTheSameHomeAdmitsIt()
    {
        OnLinux();
        var persons = Path.Combine(Directory.CreateDirectory(_scratch.At("person")).FullName, ".env");
        await File.WriteAllTextAsync(persons, "MISTRAL_API_KEY=sk-test-canary\n", TestContext.Current.CancellationToken);
        OwnerOnly(persons);
        var host = Wsl2(new VibeSignIn(persons));
        var invocation = Invocation(host);

        using (var first = await TakeAsync(host, invocation, new FakeVibe()))
        {
            first.Refusal.Should().BeEmpty("a Vibe launch on a wsl host should start, but was refused");
            new FileInfo(Path.Combine(HomeOf(invocation), ".env")).LinkTarget.Should().Be(persons);
        }

        using var second = await TakeAsync(host, invocation, new FakeVibe());

        second.Refusal.Should().BeEmpty("the .env link to the person's Vibe sign-in should be admitted, but the home was refused");
        File.ReadAllText(persons).Should().Be("MISTRAL_API_KEY=sk-test-canary\n");
    }

    [Fact]
    public async Task OnLinux_AHomeWhoseEnvLinksElsewhere_IsRefused_AndNothingIsDeleted()
    {
        OnLinux();
        var persons = Path.Combine(Directory.CreateDirectory(_scratch.At("person")).FullName, ".env");
        await File.WriteAllTextAsync(persons, "MISTRAL_API_KEY=x\n", TestContext.Current.CancellationToken);
        OwnerOnly(persons);
        var host = Wsl2(new VibeSignIn(persons));
        var invocation = Invocation(host);
        Directory.CreateDirectory(HomeOf(invocation));
        var link = Path.Combine(HomeOf(invocation), ".env");
        File.CreateSymbolicLink(link, "/elsewhere/.env");
        var vibe = new FakeVibe();

        using var turn = await TakeAsync(host, invocation, vibe);

        turn.Refusal.Should().Contain("holds .env,").And.Contain("somewhere other than the sign-in file");
        vibe.Started.Should().BeEmpty();
        new FileInfo(link).LinkTarget.Should().Be("/elsewhere/.env", "the launch control refuses a link that is not its own; it never deletes it");
    }
}

using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Server.Keys;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s7 t6 (judgement S3 and S4, SPEC C2 item 3, risks A11 and A12): the sign-in of a vendor that signs in with a file is a COPY made for the job. The copy holds the recipe's fields and a placeholder where the refresh token was (codex) or nothing of it (agy), is mode 0600 in a
/// folder of mode 0700, and is gated before every launch on the access token's expiry; a refresh runs in a unit of its own, on a scratch home that holds the full copy and that no other unit sees, only when the real file lacks the margin; the real file is opened for reading only and
/// its hash and time go into the envelope; and the token texts of EVERY copy of the job are searched for in what the coder commits.
/// </summary>
/// <remarks>
/// Every token here is made up. Nothing touches the real home: the stand-in is a temp folder (<see cref="CoderBed"/>), and a real vendor's CLI is never started — the program of a refresh is the fake CLI.
/// </remarks>
public sealed class ASignInCopyTests
{
    private const string Access = "made-up-access-token-one";

    private const string IdToken = "made-up-id-token-one";

    private const string Refresh = "made-up-refresh-token-one";

    private static readonly DateTime Now = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

    private static void NeedAUserManager() => Assert.SkipUnless(SystemdUser.Works(out var why), why);

    /// <summary>What a codex-shaped recipe is: the access and id tokens kept, the refresh token a placeholder, the gate on the access token's <c>exp</c> with a margin of an hour, no refresh step.</summary>
    private static CoderSignInRecipe.CopyForJob CodexLike() =>
        new(".probe/auth.json", ["tokens/access_token", "tokens/id_token"], new Dictionary<string, string> { ["tokens/refresh_token"] = SignInCopy.Placeholder }, string.Empty, "CX-R1")
        {
            ExpiryField = "tokens/access_token",
            ExpiryIsJwt = true,
            Margin = TimeSpan.FromMinutes(60),
            TokenFields = ["tokens/access_token", "tokens/id_token"],
        };

    /// <summary>What an agy-shaped recipe is: the access token and its expiry kept, no refresh token at all, a margin of five minutes, a refresh step that needs no model, a fresh copy for every turn.</summary>
    private static CoderSignInRecipe.CopyForJob AgyLike(params string[] refreshArgv) =>
        new(".probe/auth.json", ["token/access_token", "token/expiry"], new Dictionary<string, string>(), "models", "AG-R2")
        {
            ExpiryField = "token/expiry",
            Margin = TimeSpan.FromMinutes(5),
            TokenFields = ["token/access_token"],
            RefreshArgv = refreshArgv,
            FreshForEveryTurn = true,
        };

    private static string Jwt(DateTime expires) =>
        $"{B64("{\"alg\":\"none\"}")}.{B64($"{{\"exp\":{new DateTimeOffset(expires).ToUnixTimeSeconds()}}}")}.{B64("sig")}";

    private static string B64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string CodexFile(DateTime expires, string access = "") =>
        new JsonObject
        {
            ["OPENAI_API_KEY"] = "made-up-api-key-must-not-be-copied",
            ["account"] = "someone",
            ["tokens"] = new JsonObject { ["access_token"] = access.Length > 0 ? access : Jwt(expires), ["id_token"] = IdToken, ["refresh_token"] = Refresh },
        }.ToJsonString();

    private static string AgyFile(DateTime expires, string access = Access) =>
        new JsonObject
        {
            ["token"] = new JsonObject { ["access_token"] = access, ["refresh_token"] = Refresh, ["expiry"] = expires.ToString("O"), ["token_type"] = "Bearer" },
            ["extra"] = "must not be copied",
        }.ToJsonString();

    // ---------- the copy ----------

    [Fact]
    [SupportedOSPlatform("linux")]
    public void TheCopy_HoldsOnlyWhatTheRecipeKeeps_ThePlaceholderStandsWhereTheRefreshTokenWas_AndTheModesAre0600In0700()
    {
        using var dir = TempDir.For("sic-");
        var real = Path.Combine(dir.Path, "real.json");
        File.WriteAllText(real, CodexFile(Now.AddHours(3)));
        var copy = Path.Combine(dir.Path, "home", ".probe", "auth.json");

        Directory.CreateDirectory(Path.Combine(dir.Path, "home"));
        var made = SignInCopy.Make(CodexLike(), real, copy, Path.Combine(dir.Path, "home"));

        made.Should().BeOfType<SignInCopyResult.Made>();
        var text = File.ReadAllText(copy);
        text.Should().NotContain(Refresh, "the refresh token never reaches the coder's home").And.NotContain("made-up-api-key").And.NotContain("someone");
        var root = JsonNode.Parse(text)!.AsObject();
        root["tokens"]!["refresh_token"]!.GetValue<string>().Should().Be(SignInCopy.Placeholder);
        root["tokens"]!["id_token"]!.GetValue<string>().Should().Be(IdToken);
        root.Select(pair => pair.Key).Should().BeEquivalentTo(["tokens"]);
        File.GetUnixFileMode(copy).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.GetUnixFileMode(Path.GetDirectoryName(copy)!).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [Fact]
    public void AnAgyCopy_HasNoRefreshTokenAtAll()
    {
        using var dir = TempDir.For("sic-");
        var real = Path.Combine(dir.Path, "real.json");
        File.WriteAllText(real, AgyFile(Now.AddHours(3)));
        var copy = Path.Combine(dir.Path, "home", ".probe", "auth.json");

        Directory.CreateDirectory(Path.Combine(dir.Path, "home"));
        SignInCopy.Make(AgyLike(), real, copy, Path.Combine(dir.Path, "home")).Should().BeOfType<SignInCopyResult.Made>();

        var text = File.ReadAllText(copy);
        text.Should().NotContain(Refresh).And.NotContain("refresh_token").And.NotContain("must not be copied");
        JsonNode.Parse(text)!["token"]!["access_token"]!.GetValue<string>().Should().Be(Access);
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void TheRealFile_IsOnlyRead_ItsHashAndTimeAreTheSameAfterTheCopyIsMade_EvenWhenItIsReadOnly()
    {
        using var dir = TempDir.For("sic-");
        var real = Path.Combine(dir.Path, "real.json");
        File.WriteAllText(real, CodexFile(Now.AddHours(3)));
        File.SetUnixFileMode(real, UnixFileMode.UserRead);
        var before = SignInCopy.Snapshot(real);

        Directory.CreateDirectory(Path.Combine(dir.Path, "home"));
        SignInCopy.Make(CodexLike(), real, Path.Combine(dir.Path, "home", ".probe", "auth.json"), Path.Combine(dir.Path, "home")).Should().BeOfType<SignInCopyResult.Made>();

        before.Exists.Should().BeTrue();
        before.Sha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(real))));
        SignInCopy.Snapshot(real).Should().Be(before);
    }

    [Fact]
    public void ALinkThatTheCoderMadeWhereTheFolderBelongs_DoesNotLeadTheCopyOutOfTheHome()
    {
        using var dir = TempDir.For("sic-");
        var real = Path.Combine(dir.Path, "real.json");
        File.WriteAllText(real, CodexFile(Now.AddHours(3)));
        var home = Path.Combine(dir.Path, "home");
        var outside = Path.Combine(dir.Path, "outside");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(home, ".probe"), outside);

        var made = SignInCopy.Make(CodexLike(), real, Path.Combine(home, ".probe", "auth.json"), home);

        made.Should().BeOfType<SignInCopyResult.Refused>();
        Directory.GetFileSystemEntries(outside).Should().BeEmpty("nothing was written through the link");
    }

    [Fact]
    public void AFileThatIsNotJson_IsRefused_WithASentenceThatHoldsNoText()
    {
        using var dir = TempDir.For("sic-");
        var real = Path.Combine(dir.Path, "real.json");
        File.WriteAllText(real, "made-up-secret-text-that-is-not-json");

        var made = SignInCopy.Make(CodexLike(), real, Path.Combine(dir.Path, "home", ".probe", "auth.json"), Path.Combine(dir.Path, "home"));

        made.Should().BeOfType<SignInCopyResult.Refused>().Which.Sentence.Should().NotContain("made-up-secret-text");
    }

    // ---------- the gate ----------

    [Theory]
    [InlineData(180, 60, true)]
    [InlineData(121, 60, true)]
    [InlineData(119, 60, false)]
    [InlineData(10, 60, false)]
    public void TheCodexGate_NeedsTheUnitsEndPlusAnHour_AndRefusesOnLess(int minutesLeft, int runtimeMinutes, bool launches)
    {
        var expiry = SignInCopy.ExpiryOf(CodexLike(), JsonNode.Parse(CodexFile(Now.AddMinutes(minutesLeft)))!.AsObject());

        var said = SignInCopy.Gate(CodexLike(), expiry, Now, TimeSpan.FromMinutes(runtimeMinutes), "codex");

        (said.Length == 0).Should().Be(launches, said);
        said.Should().NotContain(Refresh).And.NotContain(IdToken);
    }

    [Theory]
    [InlineData(66, true)]
    [InlineData(64, false)]
    public void TheAgyGate_NeedsTheUnitsEndPlusFiveMinutes(int minutesLeft, bool launches)
    {
        var expiry = SignInCopy.ExpiryOf(AgyLike(), JsonNode.Parse(AgyFile(Now.AddMinutes(minutesLeft)))!.AsObject());

        var said = SignInCopy.Gate(AgyLike(), expiry, Now, TimeSpan.FromMinutes(60), "agy");

        (said.Length == 0).Should().Be(launches, said);
    }

    [Fact]
    public void AnExpiryThatCannotBeRead_IsARefusal_NotALaunchOnAGuess()
    {
        var root = JsonNode.Parse("{\"tokens\":{\"access_token\":\"not-a-jwt\"}}")!.AsObject();

        var said = SignInCopy.Gate(CodexLike(), SignInCopy.ExpiryOf(CodexLike(), root), Now, TimeSpan.FromMinutes(30), "codex");

        said.Should().Contain("could not be read").And.NotContain("not-a-jwt");
    }

    // ---------- the tokens ----------

    [Fact]
    public void TheTokensOfACopy_AreItsTokenFields_NeverThePlaceholder()
    {
        using var dir = TempDir.For("sic-");
        var real = Path.Combine(dir.Path, "real.json");
        File.WriteAllText(real, CodexFile(Now.AddHours(3), access: "made-up-access-text"));
        var copy = Path.Combine(dir.Path, "home", ".probe", "auth.json");
        Directory.CreateDirectory(Path.Combine(dir.Path, "home"));
        SignInCopy.Make(CodexLike(), real, copy, Path.Combine(dir.Path, "home"));

        SignInCopy.TokensOf(CodexLike(), copy).Should().BeEquivalentTo(["made-up-access-text", IdToken]);
    }

    // ---------- the real file in the envelope ----------

    [Fact]
    public void TheEnvelopeRecord_SaysChanged_OnlyWhenTheHashOrTheTimeDiffer_AndHoldsNoText()
    {
        var same = new RealSignInState(true, new string('a', 64), Now);
        var other = new RealSignInState(true, new string('b', 64), Now.AddMinutes(1));

        CoderSignInRecord.Of("codex", same, same).Changed.Should().BeFalse();
        var changed = CoderSignInRecord.Of("codex", same, other);
        changed.Changed.Should().BeTrue();
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            changed.Write(json);
        }

        var text = Encoding.UTF8.GetString(stream.ToArray());
        text.Should().Contain("\"vendor\":\"codex\"").And.Contain("\"changed\":true").And.Contain(new string('a', 64)).And.NotContain(Refresh);
    }

    // ---------- the launch: the copy, the refresh, the gate ----------

    [Fact]
    public async Task ARealFileWithTheMargin_IsCutIntoACopy_NoRefreshUnitRuns_AndTheRealFileIsAsItWas()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        var vendor = new CopyVendor("probe", AgyLike("set-json-field", "unused", "token/expiry", "x"));
        Write(bed, AgyFile(Now.AddHours(5)));
        var before = SignInCopy.Snapshot(RealFile(bed));
        var states = new SignInJobs();

        var said = await bed.Boundary().For(vendor).PrepareSignInAsync(bed.Job, FakeCliInvocations.Exe, TimeSpan.FromMinutes(30), states, Now, TestContext.Current.CancellationToken);

        said.Should().BeEmpty();
        states.All().Single().Refreshes.Should().Be(0);
        File.ReadAllText(Path.Combine(bed.Job.CoderHomeOf("probe"), ".probe", "auth.json")).Should().Contain(Access).And.NotContain(Refresh);
        SignInCopy.Snapshot(RealFile(bed)).Should().Be(before);
        bed.Launcher.Requests.Should().NotContain(request => request.Arguments.Any(argument => argument.Contains("refresh", StringComparison.Ordinal) && argument.StartsWith("--unit", StringComparison.Ordinal)), "no unit of the refresh was started");
    }

    [Fact]
    public async Task ARealFileWithoutTheMargin_IsRefreshedInAUnitOfItsOwn_OnAScratchHomeThatIsGoneBeforeTheCoderStarts_AndTheRealFileIsNotWritten()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        bed.Inner.UseJobsFolderWithAnotherJob();
        var scratch = bed.Job.RefreshFolder;
        var fresh = DateTime.UtcNow.AddHours(5).ToString("O");
        var vendor = new CopyVendor("probe", AgyLike("set-json-field", Path.Combine(scratch, ".probe", "auth.json"), "token/expiry", fresh));
        Write(bed, AgyFile(DateTime.UtcNow.AddMinutes(1)));
        var before = SignInCopy.Snapshot(RealFile(bed));
        var states = new SignInJobs();

        var said = await bed.Boundary().For(vendor).PrepareSignInAsync(bed.Job, FakeCliInvocations.Exe, TimeSpan.FromMinutes(30), states, DateTime.UtcNow, TestContext.Current.CancellationToken);

        said.Should().BeEmpty("the refreshed file has the margin");
        states.All().Single().Refreshes.Should().Be(1);
        Directory.Exists(scratch).Should().BeFalse("the full copy is deleted before any coder starts");
        var copy = JsonNode.Parse(File.ReadAllText(Path.Combine(bed.Job.CoderHomeOf("probe"), ".probe", "auth.json")))!;
        copy["token"]!["expiry"]!.GetValue<string>().Should().Be(fresh);
        copy["token"]!["refresh_token"].Should().BeNull("the coder's copy of the refreshed file has no refresh token either");
        SignInCopy.Snapshot(RealFile(bed)).Should().Be(before, "the refresh ran on a copy; the person's own file was only read");
    }

    [Fact]
    public async Task ARefreshThatFails_RefusesTheLaunch_WithTheExitCode_NoCopyIsMade_AndTheScratchHomeIsGone()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        bed.Inner.UseJobsFolderWithAnotherJob();
        var scratch = bed.Job.RefreshFolder;
        var vendor = new CopyVendor("probe", AgyLike("set-json-field", Path.Combine(scratch, "no-such-file.json"), "token/expiry", "x"));
        Write(bed, AgyFile(DateTime.UtcNow.AddMinutes(1)));

        var said = await bed.Boundary().For(vendor).PrepareSignInAsync(bed.Job, FakeCliInvocations.Exe, TimeSpan.FromMinutes(30), new SignInJobs(), DateTime.UtcNow, TestContext.Current.CancellationToken);

        said.Should().Contain("exited 1").And.NotContain(Refresh).And.NotContain(Access);
        Directory.Exists(scratch).Should().BeFalse();
        File.Exists(Path.Combine(bed.Job.CoderHomeOf("probe"), ".probe", "auth.json")).Should().BeFalse("no copy is left to launch with");
    }

    [Fact]
    public async Task ACodexLikeRealFileThatExpiresTooSoon_IsRefusedBeforeTheLaunch_AndNoRefreshIsTried()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        var vendor = new CopyVendor("probe", CodexLike());
        Write(bed, CodexFile(DateTime.UtcNow.AddMinutes(20)));

        var said = await bed.Boundary().For(vendor).PrepareSignInAsync(bed.Job, FakeCliInvocations.Exe, TimeSpan.FromMinutes(30), new SignInJobs(), DateTime.UtcNow, TestContext.Current.CancellationToken);

        said.Should().Contain("minutes left").And.Contain("start probe once yourself").And.NotContain(Refresh).And.NotContain(IdToken);
        bed.Launcher.Requests.Should().NotContain(request => request.Executable.Contains("systemd-run", StringComparison.Ordinal), "a codex-like recipe has no refresh step: no unit was started");
    }

    [Fact]
    public async Task ARealFileThatIsNotThere_IsARefusalThatSaysToSignIn_NoUnitIsStarted()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        var vendor = new CopyVendor("probe", AgyLike("set-json-field", "x", "y", "z"));
        var requests = bed.Launcher.Requests.Count;

        var said = await bed.Boundary().For(vendor).PrepareSignInAsync(bed.Job, FakeCliInvocations.Exe, TimeSpan.FromMinutes(30), new SignInJobs(), DateTime.UtcNow, TestContext.Current.CancellationToken);

        said.Should().Contain("not found").And.Contain("sign in");
        bed.Launcher.Requests.Count.Should().Be(requests, "no process was started");
    }

    [Fact]
    public void TheRefreshUnit_HidesTheFolderOfTheJobs_AndGivesItsScratchHomeAndNothingOfTheJob()
    {
        using var dir = TempDir.For("sic-");
        var job = new BoundaryJob("job-x", new CoaiMcp.Runners.Worktrees.CoderTree(Path.Combine(dir.Path, "wt"), Path.Combine(dir.Path, "main"), string.Empty), Path.Combine(dir.Path, "jobs", "job-x"), NetworkChoice.None("t"), NetworkChoice.None("t"), 100);
        var scratch = job.RefreshFolder;
        var program = new CoderProgram("/usr/bin/true", string.Empty, string.Empty);

        var properties = SignInRefresh.Properties(BoundaryHost.Current() with { RealHome = "/nonexistent/coai-real-home" }, job, scratch, program);

        properties.Should().Contain($"TemporaryFileSystem={job.JobCover}:ro").And.Contain($"BindPaths={scratch}");
        properties.Where(property => property.StartsWith("BindPaths=", StringComparison.Ordinal)).Should().Equal([$"BindPaths={scratch}"], "the scratch home is the one writable folder: no repository, worktree, main checkout or coder home");
        properties.Where(property => property.StartsWith("BindReadOnlyPaths=", StringComparison.Ordinal)).Should().NotContain(property => property.Contains(job.Tree.Root, StringComparison.Ordinal) || property.Contains(job.Tree.MainCheckout, StringComparison.Ordinal));
        properties.Should().Contain("PrivateNetwork=no").And.Contain($"TimeoutStopSec={UnitControl.UnitStopSeconds}");
    }

    [Fact]
    public async Task TheScratchHome_IsNotInTheCodersUnit_WhateverItHolds()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        var others = bed.Inner.UseJobsFolderWithAnotherJob();
        var scratch = bed.Job.RefreshFolder;
        Directory.CreateDirectory(scratch);
        await File.WriteAllTextAsync(Path.Combine(scratch, "full-copy.json"), "made-up-full-copy\n", TestContext.Current.CancellationToken);

        var run = await bed.RunLaunchAsync(bed.Shell($"if [ -e {scratch} ] || [ -e {Path.Combine(scratch, "full-copy.json")} ]; then echo sees-the-full-copy; else echo hidden-full-copy; fi"));

        run.Result.StdOut.Should().Contain("hidden-full-copy").And.NotContain("sees-the-full-copy");
        File.Exists(Path.Combine(scratch, "full-copy.json")).Should().BeTrue("a positive control: it is there on the host");
        File.Exists(others).Should().BeTrue();
    }

    // ---------- every copy's tokens are searched for in what the coder commits (K3, C2) ----------

    [Fact]
    public async Task ATokenOfTheCopy_InAFileTheCoderWrote_StopsTheCommit_NamesNoText_AndStagesNothing()
    {
        var (world, tree, git, before) = await StartAsync(CoderTestBed.JobId(31));
        await using var _ = world;
        await File.WriteAllTextAsync(Path.Combine(tree.Root, "leak.txt"), $"here: {Access}\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(tree.Root, "honest.txt"), "honest work\n", TestContext.Current.CancellationToken);

        var made = await CoderCommits.CommitAsync(git, tree, "turn", TestContext.Current.CancellationToken, tokens: Tokens(Access));

        made.Failure.Should().NotBeNull();
        made.Failure!.Stop.Should().Be("coder-boundary");
        made.Failure.Reason.Should().NotContain(Access).And.Contain("sign-in");
        (await CoderCommits.HeadAsync(git, tree, TestContext.Current.CancellationToken)).Head.Should().Be(before, "nothing was committed");
        (await git.InCoderTreeAsync(tree, ["ls-files", "--stage"], TestContext.Current.CancellationToken)).Out.Should().NotContain("leak.txt").And.NotContain("honest.txt");
    }

    [Fact]
    public async Task ATokenOfTheCopy_InAFileName_StopsTheCommit()
    {
        var (world, tree, git, before) = await StartAsync(CoderTestBed.JobId(32));
        await using var _ = world;
        await File.WriteAllTextAsync(Path.Combine(tree.Root, $"note-{Access}.txt"), "nothing inside\n", TestContext.Current.CancellationToken);

        var made = await CoderCommits.CommitAsync(git, tree, "turn", TestContext.Current.CancellationToken, tokens: Tokens(Access));

        made.Failure.Should().NotBeNull();
        made.Failure!.Stop.Should().Be("coder-boundary");
        (await CoderCommits.HeadAsync(git, tree, TestContext.Current.CancellationToken)).Head.Should().Be(before);
    }

    [Fact]
    public async Task ATokenOfTheCopy_InAFileWhoseWorkingTreeEncodingIsNotWhatGitCommits_IsFoundInTheStagedBlob_AndTheIndexIsEmptyAgain()
    {
        var (world, tree, git, before) = await StartAsync(CoderTestBed.JobId(33));
        await using var _ = world;
        await File.WriteAllTextAsync(Path.Combine(tree.Root, ".gitattributes"), "leak.txt working-tree-encoding=UTF-16LE\n", TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(tree.Root, "leak.txt"), Encoding.Unicode.GetBytes($"{Access}\n"), TestContext.Current.CancellationToken);

        var made = await CoderCommits.CommitAsync(git, tree, "turn", TestContext.Current.CancellationToken, tokens: Tokens(Access));

        made.Failure.Should().NotBeNull("the blob git commits is UTF-8 and holds the exact text, which the bytes on disk (UTF-16) do not");
        (await CoderCommits.HeadAsync(git, tree, TestContext.Current.CancellationToken)).Head.Should().Be(before);
        (await git.InCoderTreeAsync(tree, ["diff", "--cached", "--name-only"], TestContext.Current.CancellationToken)).Out.Should().BeEmpty("what was staged is unstaged again");
    }

    [Fact]
    public async Task TheTokensOfEveryCopyOfTheJob_AreSearched_OneVendorsTokenIsFound_WhileAnotherVendorRuns()
    {
        var (world, tree, git, before) = await StartAsync(CoderTestBed.JobId(34));
        await using var _ = world;
        await File.WriteAllTextAsync(Path.Combine(tree.Root, "leak.txt"), "the first vendor's token: made-up-codex-token\n", TestContext.Current.CancellationToken);

        var made = await CoderCommits.CommitAsync(git, tree, "turn", TestContext.Current.CancellationToken, tokens: Tokens("made-up-agy-token", "made-up-codex-token"));

        made.Failure.Should().NotBeNull();
        (await CoderCommits.HeadAsync(git, tree, TestContext.Current.CancellationToken)).Head.Should().Be(before);
    }

    [Fact]
    public async Task AnHonestChange_IsStillCommitted_WithTheTokensGiven()
    {
        var (world, tree, git, before) = await StartAsync(CoderTestBed.JobId(35));
        await using var _ = world;
        await File.WriteAllTextAsync(Path.Combine(tree.Root, "work.txt"), "honest work\n", TestContext.Current.CancellationToken);

        var made = await CoderCommits.CommitAsync(git, tree, "turn", TestContext.Current.CancellationToken, tokens: Tokens(Access));

        made.Failure.Should().BeNull();
        made.Head.Should().NotBe(before);
    }

    [Fact]
    public async Task TheLayer_KeepsTheTokensOfACopyItDeleted_AndOfTheFileAsTheVendorRewroteIt()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        var vendor = new CopyVendor("probe", AgyLike("set-json-field", "x", "y", "z"));
        Write(bed, AgyFile(Now.AddHours(5)));
        var layer = new CoderOsLayer(bed.Job, bed.Turns(), bed.Launcher);
        (await bed.Boundary().For(vendor).PrepareSignInAsync(bed.Job, FakeCliInvocations.Exe, TimeSpan.FromMinutes(30), layer.SignIns, Now, TestContext.Current.CancellationToken)).Should().BeEmpty();
        var copy = Path.Combine(bed.Job.CoderHomeOf("probe"), ".probe", "auth.json");
        // agy rewrites its file during a turn: a new access token, a new refresh token — a name the real file never held.
        File.WriteAllText(copy, AgyFile(Now.AddHours(5), access: "made-up-rewritten-access"));

        layer.LeaveVendor(vendor).Should().BeEmpty();

        File.Exists(copy).Should().BeFalse("the copy of the vendor the job left is deleted before the next vendor starts");
        layer.SignInTokens().Select(secret => secret.IsIn("here: made-up-rewritten-access"u8)).Should().Contain(true, "the token the vendor wrote is still searched for");
        layer.SignInTokens().Select(secret => secret.IsIn(Encoding.UTF8.GetBytes($"and {Access}"))).Should().Contain(true, "so is the one the copy was made with");
        layer.SignInTokens().Select(secret => secret.Id).Should().OnlyContain(id => id.StartsWith("sign-in-probe-", StringComparison.Ordinal), "a token is named by an id, never by its text");
    }

    [Fact]
    public async Task WhenTheJobEnds_ItsCopiesAreDeleted_AndTheRealFileRecordSaysItWasNotChanged()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        var vendor = new CopyVendor("probe", AgyLike("set-json-field", "x", "y", "z"));
        Write(bed, AgyFile(Now.AddHours(5)));
        var layer = new CoderOsLayer(bed.Job, bed.Turns(), bed.Launcher);
        await bed.Boundary().For(vendor).PrepareSignInAsync(bed.Job, FakeCliInvocations.Exe, TimeSpan.FromMinutes(30), layer.SignIns, Now, TestContext.Current.CancellationToken);

        layer.RemoveSignInCopies();

        File.Exists(Path.Combine(bed.Job.CoderHomeOf("probe"), ".probe", "auth.json")).Should().BeFalse();
        var record = layer.SignInRecords().Single();
        record.Vendor.Should().Be("probe");
        record.Changed.Should().BeFalse();
        record.BeforeSha256.Should().Be(record.AfterSha256).And.HaveLength(64);
    }

    [Fact]
    public async Task WhenSomethingElseChangesTheRealFile_TheRecordSaysChanged()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        var vendor = new CopyVendor("probe", AgyLike("set-json-field", "x", "y", "z"));
        Write(bed, AgyFile(Now.AddHours(5)));
        var layer = new CoderOsLayer(bed.Job, bed.Turns(), bed.Launcher);
        await bed.Boundary().For(vendor).PrepareSignInAsync(bed.Job, FakeCliInvocations.Exe, TimeSpan.FromMinutes(30), layer.SignIns, Now, TestContext.Current.CancellationToken);

        Write(bed, AgyFile(Now.AddHours(9)));

        layer.SignInRecords().Single().Changed.Should().BeTrue("the person's own CLI rewrote the file meanwhile; coai did not");
    }

    // ---------- the fourth security verify (M8, M10, C3) ----------

    [Fact]
    [SupportedOSPlatform("linux")]
    public void ALinkTwoNamesAbove_AFolderThatIsNotThereYet_DoesNotLeadTheCopyOutOfTheHome()
    {
        // agy's file is two names deep (.gemini/antigravity-cli/...): the link is at the FIRST name, and the second folder is not there yet, so a make of the whole path would follow the link.
        using var dir = TempDir.For("sic-");
        var real = Path.Combine(dir.Path, "real.json");
        File.WriteAllText(real, AgyFile(Now.AddHours(3)));
        var home = Path.Combine(dir.Path, "home");
        var outside = Path.Combine(dir.Path, "outside");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(home, ".gemini"), outside);

        var made = SignInCopy.Make(AgyLike(), real, Path.Combine(home, ".gemini", "antigravity-cli", "token.json"), home);

        made.Should().BeOfType<SignInCopyResult.Refused>();
        Directory.GetFileSystemEntries(outside).Should().BeEmpty("no folder and no file was made through the link");
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void AHomeThatIsALink_IsRefused_ForTheCopy_AndForTheDelete_AndNothingIsWrittenOrDeletedThroughIt()
    {
        using var dir = TempDir.For("sic-");
        var real = Path.Combine(dir.Path, "real.json");
        File.WriteAllText(real, CodexFile(Now.AddHours(3)));
        var target = Path.Combine(dir.Path, "elsewhere");
        Directory.CreateDirectory(Path.Combine(target, ".probe"));
        File.WriteAllText(Path.Combine(target, ".probe", "auth.json"), "made-up-file-of-another-folder");
        var home = Path.Combine(dir.Path, "home");
        Directory.CreateSymbolicLink(home, target);

        SignInCopy.HomeProblem(home).Should().Contain("not a real folder");
        SignInCopy.Make(CodexLike(), real, Path.Combine(home, ".probe", "auth.json"), home).Should().BeOfType<SignInCopyResult.Refused>().Which.Sentence.Should().Contain("home");
        CoderSignInCopy.Remove(home, Path.Combine(home, ".probe", "auth.json")).Should().Contain("not a real folder");

        File.ReadAllText(Path.Combine(target, ".probe", "auth.json")).Should().Be("made-up-file-of-another-folder", "the file behind the link was neither overwritten nor deleted");
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void AHomeWhoseParentIsALink_IsNotAtItsOwnRealPath_AndIsRefused()
    {
        using var dir = TempDir.For("sic-");
        var real = Path.Combine(dir.Path, "real");
        Directory.CreateDirectory(Path.Combine(real, "home"));
        var parent = Path.Combine(dir.Path, "parent");
        Directory.CreateSymbolicLink(parent, real);

        SignInCopy.HomeProblem(Path.Combine(parent, "home")).Should().Contain("real path");
        SignInCopy.HomeProblem(Path.Combine(real, "home")).Should().BeEmpty("the control: the same folder by its real path is a home");
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public async Task ACodersPreflight_OnAHomeThatIsALink_RefusesTheLaunch_AndTheFolderBehindTheLinkIsNotWritten()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        var behind = Path.Combine(bed.RealHome, "behind-the-link");
        Directory.CreateDirectory(behind);
        Directory.CreateDirectory(bed.Job.CoderVendorFolder("claude"));
        Directory.CreateSymbolicLink(bed.Job.CoderHomeOf("claude"), behind);

        var admission = await bed.Boundary().AdmitAsync(bed.Job, FakeCliInvocations.Exe, TestContext.Current.CancellationToken);

        admission.Should().BeOfType<BoundaryAdmission.Refused>("a unit is never started on a home that is a link");
        Directory.GetFileSystemEntries(behind).Select(Path.GetFileName).Should().NotContain(name => name!.StartsWith(".coai-", StringComparison.Ordinal), "nothing of the preflight was left behind the link");
    }

    [Fact]
    public void TheHomesOfTwoVendors_AreTwoFolders_AndEachUnitBindsItsOwnVendorFolderAndNotTheOthers()
    {
        using var dir = TempDir.For("sic-");
        var job = new BoundaryJob("job-x", new CoaiMcp.Runners.Worktrees.CoderTree(Path.Combine(dir.Path, "wt"), Path.Combine(dir.Path, "main"), string.Empty), Path.Combine(dir.Path, "jobs", "job-x"), NetworkChoice.None("t"), NetworkChoice.None("t"), 100);
        var host = BoundaryHost.Current() with { RealHome = "/nonexistent/coai-real-home" };
        var program = new CoderProgram("/usr/bin/true", string.Empty, string.Empty);

        job.CoderHomeOf("codex").Should().Be(Path.Combine(job.JobRoot, "coder", "codex", "home"));
        job.CoderHomeOf("agy").Should().Be(Path.Combine(job.JobRoot, "coder", "agy", "home"));
        job.CoderHomeFolder.Should().Be(Path.Combine(job.JobRoot, "coder", "claude", "home"));
        var codex = CoderUnit.Properties(host, job, program, TimeSpan.FromMinutes(5), "codex");
        var agy = CoderUnit.Properties(host, job, program, TimeSpan.FromMinutes(5), "agy");

        codex.Should().Contain($"BindPaths={job.CoderVendorFolder("codex")}").And.NotContain(property => property.Contains(job.CoderVendorFolder("agy"), StringComparison.Ordinal));
        agy.Should().Contain($"BindPaths={job.CoderVendorFolder("agy")}").And.NotContain(property => property.Contains(job.CoderVendorFolder("codex"), StringComparison.Ordinal));
        codex.Should().NotContain($"BindPaths={job.CoderFolder}", "the whole coder folder of the job is bound by no unit");
        CoderUnit.Environment(host, job, "codex").Should().Contain(new KeyValuePair<string, string>("HOME", job.CoderHomeOf("codex")));
        CoderUnit.Environment(host, job, "agy").Should().Contain(new KeyValuePair<string, string>("HOME", job.CoderHomeOf("agy")));
    }

    [Fact]
    public void TheRefreshFolder_IsInsideTheJobFolder_ByTheName_Refresh()
    {
        using var dir = TempDir.For("sic-");
        var job = new BoundaryJob("job-x", new CoaiMcp.Runners.Worktrees.CoderTree(Path.Combine(dir.Path, "wt"), Path.Combine(dir.Path, "main"), string.Empty), Path.Combine(dir.Path, "jobs", "job-x"), NetworkChoice.None("t"), NetworkChoice.None("t"), 100);

        job.RefreshFolder.Should().Be(Path.Combine(job.JobRoot, "refresh"));
    }

    [Fact]
    public async Task WhereTheJobFolderIsItsOwnCover_NoRefreshIsRun_TheLaunchIsRefusedWithASentence_AndNoUnitIsStarted()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        bed.Job.IsItsOwnCover.Should().BeTrue("the control: this layout is the own-cover one");
        var vendor = new CopyVendor("probe", AgyLike("set-json-field", "x", "y", "z"));
        Write(bed, AgyFile(DateTime.UtcNow.AddMinutes(1)));
        var requests = bed.Launcher.Requests.Count;

        var said = await bed.Boundary().For(vendor).PrepareSignInAsync(bed.Job, FakeCliInvocations.Exe, TimeSpan.FromMinutes(30), new SignInJobs(), DateTime.UtcNow, TestContext.Current.CancellationToken);

        said.Should().Contain("cannot be hidden").And.Contain("start probe once yourself").And.NotContain(Refresh).And.NotContain(Access);
        bed.Launcher.Requests.Count.Should().Be(requests, "no refresh unit was started");
        Directory.Exists(bed.Job.RefreshFolder).Should().BeFalse("no full copy was made");
    }

    [Fact]
    public async Task AFullCopyAKilledRunnerLeft_IsDeletedAtTheNextStart_BeforeAnythingElseIsAsked()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        var vendor = new CopyVendor("probe", AgyLike("set-json-field", "x", "y", "z"));
        Write(bed, AgyFile(Now.AddHours(5)));
        Directory.CreateDirectory(bed.Job.RefreshFolder);
        await File.WriteAllTextAsync(Path.Combine(bed.Job.RefreshFolder, "auth.json"), AgyFile(Now.AddHours(5)), TestContext.Current.CancellationToken);

        var said = await bed.Boundary().For(vendor).PrepareSignInAsync(bed.Job, FakeCliInvocations.Exe, TimeSpan.FromMinutes(30), new SignInJobs(), Now, TestContext.Current.CancellationToken);

        said.Should().BeEmpty();
        Directory.Exists(bed.Job.RefreshFolder).Should().BeFalse("the folder with the owner's full sign-in did not wait for the retention");
    }

    // ---------- helpers ----------

    private static string RealFile(CoderBed bed) => Path.Combine(bed.RealHome, ".probe", "auth.json");

    private static void Write(CoderBed bed, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RealFile(bed))!);
        File.WriteAllText(RealFile(bed), text);
    }

    private static IReadOnlyList<ClaudeKeySecret> Tokens(params string[] texts) => [.. texts.Select((text, index) => new ClaudeKeySecret($"sign-in-test-{index + 1}", text))];

    private static async Task<(CoderSubmoduleWorld World, CoaiMcp.Runners.Worktrees.CoderTree Tree, CoaiMcp.Runners.Collecting.GitHistory Git, string Head)> StartAsync(string job)
    {
        var world = await CoderSubmoduleWorld.CreateAsync();
        foreach (var (key, value) in new[] { ("user.email", "t@t"), ("user.name", "t"), ("commit.gpgsign", "false") })
        {
            await world.Launcher.RunAsync(new ProcessRequest("git", ["config", key, value], world.Main.Path), TestContext.Current.CancellationToken);
        }

        var tree = await world.WorktreeAsync(job);
        var git = new CoaiMcp.Runners.Collecting.GitHistory(world.Launcher);

        return (world, tree, git, (await CoderCommits.HeadAsync(git, tree, TestContext.Current.CancellationToken)).Head);
    }

    /// <summary>A vendor that stands in for codex or agy: claude's argv, probe and unit (the fake CLI answers as claude), its own id and home folder, and the recipe a test gives it.</summary>
    private sealed class CopyVendor(string id, CoderSignInRecipe recipe) : ICoderVendor
    {
        public string Id => id;

        public string PromptId => $"coder-copy-{id}";

        public string PromptResource => CoderVendors.Claude.PromptResource;

        public CoderSignInRecipe SignIn => recipe;

        public IConsultantRuntime Runtime => new ClaudeConsultant(new ClaudeRuntime(), id);

        public string FixClause => "you run no command of your own";

        public bool PointsAtInstructionBlock => true;

        public CoderTurnVerdict CheckTurn(ProcessResult process, string expectedSession) => new CoderTurnVerdict.Fine();

        public Task<CoderCapability> ProbeAsync(IProcessLauncher launcher, string executable, string workingDirectory, CancellationToken ct) => CoderVendors.Claude.ProbeAsync(launcher, executable, workingDirectory, ct);

        public IReadOnlyList<string> FreshArgv(string model, string worktree) => CoderVendors.Claude.FreshArgv(model, worktree);

        public IReadOnlyList<string> ResumedArgv(string model, string worktree, string sessionId) => CoderVendors.Claude.ResumedArgv(model, worktree, sessionId);

        public IReadOnlyList<string> UnitProperties(BoundaryHost host, BoundaryJob job, CoderProgram program, TimeSpan runtimeMax) => CoderUnit.Properties(host, job, program, runtimeMax, id);

        public string WorkingDirectory(BoundaryJob job) => CoderVendors.Claude.WorkingDirectory(job);

        public IReadOnlyList<KeyValuePair<string, string>> Environment(BoundaryHost host, BoundaryJob job) => CoderUnit.Environment(host, job, id);

        public CoderHomeLayout Home(BoundaryJob job) => new(job.CoderHomeOf(id), Path.Combine(job.CoderHomeOf(id), ".probe", "auth.json"), [.. job.CoderFoldersOf(id), Path.Combine(job.CoderHomeOf(id), ".probe")]);

        public CoderProgram Program(string executable, string path, BoundaryJob job) => CoderVendors.Claude.Program(executable, path, job);
    }
}

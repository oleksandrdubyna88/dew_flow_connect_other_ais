using System.Text.Json;
using System.Text.RegularExpressions;
using CoaiMcp.Core.Roles;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s6 t3 (V3, V4, V14, V17; ids AG-M1, AG-B1, AG-B3, AG-R1, AG-R2, U2, U3, U6, U7, U8, U9; judgement G5, G6, G7, G10): the launch of the agy coder. d1: the argv is the flag set AG1 with the mode and the agent on every turn, <c>--conversation</c> only on a fix turn, no never-flag reachable. d2: the working directory is the
/// guard folder, never the worktree, and the guard folder and the program's folder are read-only in the unit. d3: the unit HIDES the main checkout and the coder root, the job's home holds the sign-in copy of the recipe (the access token only), fresh for every turn, and a launch that cannot have the margin is refused
/// before it starts. d4: only the allow-list reaches the CLI, compared from inside a real unit, and no secret is in a recorded argv. d5: the consultants' agy argv is byte for byte what it was. d6: the prompt of the agy coder.
/// </summary>
/// <remarks>
/// The unit tests run a shell as the program, inside a REAL <c>systemd-run --user</c> unit, so what is seen is what the OS shows; without a user manager they are skipped, with the reason. The real sign-in file is never read: the stand-in for the home holds a made-up one.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AAgyCoderLaunchTests
{
    private const string Worktree = "/job/wt";

    private static readonly string[] NeverFlags = ["--dangerously-skip-permissions", "--remote-agent", "--remote-control", "--continue", "-c"];

    private static string[] Measured(string model = "") =>
    [
        "--print=", "--input-format", "stream-json", "--output-format", "stream-json", "--mode", "accept-edits", "--agent", "coai-coder", .. model.Length > 0 ? new[] { "--model", model } : [], "--add-dir", Worktree,
    ];

    // ---------- d1: the argv ----------

    [Fact]
    public void D1_TheFreshArgv_IsExactlyTheFlagSetAG1_WithTheModeAndTheAgent()
    {
        var vendor = AgyCoderBed.Vendor();

        vendor.FreshArgv(AgyCoderBed.Model, Worktree).Should().Equal(Measured(AgyCoderBed.Model));
        vendor.FreshArgv(string.Empty, Worktree).Should().Equal(Measured());
    }

    [Fact]
    public void D1_AFixTurn_AddsTheConversationOfTurnZero_AndChangesNothingElse()
    {
        var vendor = AgyCoderBed.Vendor();

        vendor.ResumedArgv(AgyCoderBed.Model, Worktree, AgyCoderBed.Conversation).Should().Equal([.. Measured(AgyCoderBed.Model), "--conversation", AgyCoderBed.Conversation]);
        vendor.ResumedArgv(AgyCoderBed.Model, Worktree, AgyCoderBed.Conversation).Should().Contain(["--mode", "accept-edits", "--agent", "coai-coder"], "a conversation that agy starts in silence is still the coder agent behind the guard");
    }

    private static ConsultantLaunch CoderLaunch(string workFolder, bool resumed)
    {
        var worktree = Path.Combine(Path.GetFullPath("roots"), "coai-coder-r20261126101010-0000000a");
        var plain = new LaunchConfinement.Coder("r20261126101010-0000000a", worktree) { Vendor = "agy" };
        var coder = plain with { WorkFolder = workFolder, Resume = resumed ? CoderHandle.Bind(plain, AgyCoderBed.Conversation) : CoderHandle.None };

        return new ConsultantLaunch(Path.GetFullPath("main"), "do it", string.Empty, Path.GetFullPath("answers"), new ReviewerSettings("agy") { Model = AgyCoderBed.Model, ExecutablePath = "/bin/agy" }) { Confinement = coder };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void D1_NoNeverFlag_IsReachable_ThroughTheConsultantsBuild_FreshOrResumed(bool resumed)
    {
        var launch = CoderLaunch(Path.GetFullPath("guard"), resumed);
        var worktree = ((LaunchConfinement.Coder)launch.Confinement).Worktree;

        var invocation = new AntigravityConsultant(new AntigravityRuntime("agy"), "agy").Build(launch);

        invocation.Request.Arguments.Should().NotContain(NeverFlags).And.Contain(["--mode", "accept-edits", "--agent", "coai-coder"]);
        invocation.Request.Arguments.Should().Equal(resumed ? AgyCoderBed.Vendor().ResumedArgv(AgyCoderBed.Model, worktree, AgyCoderBed.Conversation) : AgyCoderBed.Vendor().FreshArgv(AgyCoderBed.Model, worktree));
        invocation.Request.WorkingDirectory.Should().Be(Path.GetFullPath("guard"), "the launch starts where the layer says: the guard folder");
        invocation.Request.Environment.Should().BeEmpty("the unit sets the environment, and agy needs no variable for its sign-in");
        invocation.Request.StdIn.Should().Contain("do it");
    }

    [Fact]
    public void TheMarkLaunchable_IsOnInTheProductsTable_AfterTheFlipCommitOfS8T6()
    {
        // s8 t6 d4 turned the mark on, after the must-changes (the list in AAgyCoderFixTurnsTests.D4_...) were built and green.
        CoderBoundaryTable.Shipped.Row("agy")!.Launchable.Should().BeTrue();
        CoderVendors.Shipped.Resolve("agy").Should().BeOfType<CoderVendorResolution.Launchable>();
        CoderVendors.Shipped.Implemented.Select(vendor => vendor.Id).Should().Contain("agy");
    }

    [Fact]
    public void D2_ACoderLaunch_WithNoGuardFolder_OrWithTheWorktreeAsItsFolder_IsNeverBuilt()
    {
        var worktree = Path.Combine(Path.GetFullPath("roots"), "coai-coder-r20261126101010-0000000a");
        foreach (var folder in new[] { string.Empty, worktree })
        {
            var build = () => new AntigravityConsultant(new AntigravityRuntime("agy"), "agy").Build(CoderLaunch(folder, resumed: false));

            build.Should().Throw<ArgumentException>().WithMessage("*guard folder*never in the worktree*");
        }
    }

    [Fact]
    public async Task D5_TheConsultantsOwnLaunches_AreByteForByteWhatTheyWere()
    {
        var launch = new ConsultantLaunch(Path.GetFullPath("main"), "ask", AgyCoderBed.Conversation, Path.GetFullPath("answers"), new ReviewerSettings("agy") { Model = AgyCoderBed.Model, ExecutablePath = "/bin/agy" });
        var consultant = new AntigravityConsultant(new AntigravityRuntime("agy"), "agy", prepare: () => new AntigravityReadOnly.Prepared.Ready(Path.GetFullPath("ro")));

        var invocation = consultant.Build(launch);

        invocation.Request.Arguments.Should().Equal("--print=", "--input-format", "stream-json", "--output-format", "stream-json", "--mode", "plan", "--agent", "coai-reader", "--conversation", AgyCoderBed.Conversation, "--model", AgyCoderBed.Model, "--add-dir", Path.GetFullPath("main"));
        invocation.Request.WorkingDirectory.Should().Be(AntigravityReadOnly.Home().Length > 0 ? invocation.Request.WorkingDirectory : string.Empty);
        (await consultant.PrepareAsync(launch, new ProcessLauncher(), TestContext.Current.CancellationToken)).Should().BeOfType<ConsultantPreparation.Ready>();
    }

    // ---------- d2, d3, d4: the unit, on a real unit ----------

    private static string Out(CoderTurnOutcome outcome) =>
        outcome.Should().BeOfType<CoderTurnOutcome.Finished>(outcome is CoderTurnOutcome.Refused refused ? refused.Sentence : outcome.ToString()).Subject.Run.Result.StdOut;

    private static string Said(string stdout, string key) =>
        stdout.Split('\n').Select(line => line.TrimEnd('\r')).FirstOrDefault(line => line.StartsWith(key + ":", StringComparison.Ordinal))?[(key.Length + 1)..] ?? "(no line)";

    [Fact]
    public async Task D2_TheUnitStartsInTheGuardFolder_TheGuardFolderIsReadOnly_TheMainCheckoutAndTheCoderRootAreHidden_ThatIsWhatTheOsShows()
    {
        await using var unit = await AgyUnit.CreateAsync();
        var bed = unit.Bed;
        var mainFile = Directory.EnumerateFiles(bed.Main).Select(Path.GetFileName).First(name => !name!.StartsWith('.'))!;

        var stdout = Out(await unit.RunAsync($$"""
            echo "cwd:$(pwd -P)"
            echo "main:$(ls -A '{{bed.Main}}' 2>/dev/null | tr '\n' ' ')"
            echo "mainfile:$(cat '{{bed.Main}}/{{mainFile}}' 2>/dev/null | head -c 20)"
            echo "root:$(ls -A '{{bed.CoderRoot}}' 2>/dev/null | tr '\n' ' ')"
            echo "beside:$( { true > '{{bed.CoderRoot}}/beside-probe'; } 2>/dev/null && echo landed || echo refused)"
            echo "guard:$( { true > '{{unit.Guard}}/probe'; } 2>/dev/null && echo writable || echo readonly)"
            echo "agents:$( { true > '{{unit.Guard}}/.agents/probe'; } 2>/dev/null && echo writable || echo readonly)"
            echo "worktree:$( { true > '{{bed.Worktree}}/probe'; } 2>/dev/null && echo writable || echo readonly)"
            rm -f '{{bed.Worktree}}/probe'
            """));

        Said(stdout, "cwd").Should().Be(unit.Guard, "AG-B1: the working directory of the unit is the guard folder, never the worktree");
        Said(stdout, "main").Trim().Should().Be(".git", "U2: only <main>/.git is there");
        Said(stdout, "mainfile").Should().BeEmpty("U2: no file of the main checkout can be read");
        Said(stdout, "root").Trim().Should().Be(Path.GetFileName(bed.Worktree), "U3: nothing beside the worktree is there");
        Said(stdout, "beside").Should().Be("landed", "the cover is a tmpfs of the unit: the write lands in memory ...");
        File.Exists(Path.Combine(bed.CoderRoot, "beside-probe")).Should().BeFalse("... and does not reach the disk");
        Said(stdout, "guard").Should().Be("readonly", "AG-B3");
        Said(stdout, "agents").Should().Be("readonly", "AG-B3");
        Said(stdout, "worktree").Should().Be("writable");
    }

    [Fact]
    public async Task D2_ALaunchWhoseFolderIsTheWorktree_IsRefusedBeforeAnythingStarts()
    {
        await using var unit = await AgyUnit.CreateAsync();

        var outcome = await unit.Turns.RunAsync(unit.Bed.Job, unit.Bed.Shell("echo started"), "c1", unit.Vendor, TestContext.Current.CancellationToken, unit.SignIns);

        var refused = outcome.Should().BeOfType<CoderTurnOutcome.Refused>().Subject;
        refused.Sentence.Should().Contain("not where the coder").And.Contain(unit.Guard);
    }

    [Fact]
    public void D2_TheProgramFolderAndTheGuardFolder_AreBoundReadOnly_AndTheHandlersFolderToo()
    {
        var job = AgyJob();
        var vendor = AgyCoderBed.Vendor();
        var program = CoderProgram.Resolve(RoleJobWorld.FakeCli, string.Empty, job);
        var properties = vendor.UnitProperties(NoHome(), job, program, TimeSpan.FromMinutes(5));

        properties.Should().Contain($"BindReadOnlyPaths={AgyCoderUnit.GuardFolder(job)}");
        properties.Should().Contain($"BindReadOnlyPaths={Path.GetDirectoryName(RoleJobWorld.FakeCli)}", "U9: the program's folder cannot be written from inside, and the hook handler lies in it here");
        properties.Should().Contain($"TemporaryFileSystem={job.Tree.MainCheckout}").And.Contain($"TemporaryFileSystem={job.CoderRoot}");
        properties.Should().NotContain($"BindReadOnlyPaths={job.Tree.MainCheckout}", "the main checkout is HIDDEN, not read-only");
        properties.Should().Contain($"BindPaths={job.CoderVendorFolder("agy")}").And.NotContain($"BindPaths={job.CoderFolder}", "only this vendor's folder of the job is bound");
        vendor.WorkingDirectory(job).Should().Be(AgyCoderUnit.GuardFolder(job)).And.NotBe(job.Tree.Root);
    }

    [Fact]
    public void D3_TheRealAgySignInFile_IsHiddenAtItsRealPathFromEveryUnit()
    {
        // A home that is not under a hidden folder (/home) would show the real file: the property hides it from the check units, the claude coder's unit and the agy unit (the agy unit holds the job's copy at another path).
        var host = NoHome() with { RealHome = Path.Combine(Path.GetFullPath("srv"), "person") };
        var hide = $"InaccessiblePaths=-{Path.Combine(host.RealHome, ".gemini", "antigravity-cli", "antigravity-oauth-token")}";
        var job = AgyJob();
        var program = CoderProgram.Resolve(RoleJobWorld.FakeCli, string.Empty, job);

        CoderVendors.Claude.UnitProperties(host, job, program, TimeSpan.FromMinutes(5)).Should().Contain(hide);
        AgyCoderBed.Vendor().UnitProperties(host, job, program, TimeSpan.FromMinutes(5)).Should().Contain(hide).And.Contain($"InaccessiblePaths=-{Path.Combine(host.RealHome, ".claude", ".credentials.json")}");
        BoundaryUnit.Properties(host, job, CheckNetwork.Private, TimeSpan.FromMinutes(5)).Should().Contain(hide);
    }

    private static BoundaryHost NoHome() => BoundaryHost.Current() with { RealHome = string.Empty, NuGetPackages = string.Empty, Path = string.Empty, ResolverFile = string.Empty };

    private static BoundaryJob AgyJob()
    {
        var root = Path.Combine(Path.GetFullPath("jobs"), "r20261126101010-0000000a");
        var tree = new CoaiMcp.Runners.Worktrees.CoderTree(Path.Combine(Path.GetFullPath("roots"), "coai-coder-r20261126101010-0000000a"), Path.GetFullPath("main"), "gitdir: x");

        return BoundaryJob.ForCoder("r20261126101010-0000000a", tree, root);
    }

    [Fact]
    public async Task D3_TheHomeHoldsTheJobsSignInCopy_TheAccessTokenOnly_Mode0600_AndNoOtherSignInFile()
    {
        await using var unit = await AgyUnit.CreateAsync();
        var home = AgyCoderUnit.HomeFolder(unit.Bed.Job);
        var file = AgyCoderUnit.SignInFile(unit.Bed.Job);

        var stdout = Out(await unit.RunAsync($"""
            echo "home:$HOME"
            echo "mode:$(stat -c %a '{file}')"
            echo "refresh:$(grep -c refresh '{file}')"
            echo "access:$(grep -c '{AgyCoderBed.Access}' '{file}')"
            echo "email:$(grep -c someone '{file}')"
            echo "gemini:$(find "$HOME/.gemini" -type f | grep -Eic 'token|oauth|credential')"
            echo "real:$( [ -e '{unit.Real}' ] && echo visible || echo hidden)"
            """));

        Said(stdout, "home").Should().Be(home);
        Said(stdout, "mode").Should().Be("600", "AG-R1");
        Said(stdout, "refresh").Should().Be("0", "AG-R2: the copy holds no refresh token");
        Said(stdout, "access").Should().Be("1");
        Said(stdout, "email").Should().Be("0", "only the fields of the recipe are kept");
        Said(stdout, "gemini").Should().Be("1", "the only sign-in file there");
        Said(stdout, "real").Should().Be("hidden", "the real file is not in the unit");
        File.ReadAllText(unit.Real).Should().Contain(AgyCoderBed.Refresh, "the real file is read, never written: it still holds its refresh token");
    }

    [Fact]
    public async Task D3_AFreshCopyIsMadeForEveryTurn()
    {
        await using var unit = await AgyUnit.CreateAsync();
        var file = AgyCoderUnit.SignInFile(unit.Bed.Job);
        _ = Out(await unit.RunAsync("true"));
        File.WriteAllText(unit.Real, AgyCoderBed.SignInFile(DateTime.UtcNow.AddHours(2), "made-up-agy-access-token-two"));

        var second = Out(await unit.RunAsync($"grep -o 'made-up-agy-access-token-[a-z]*' '{file}'", "c2"));

        second.Trim().Should().Be("made-up-agy-access-token-two", "turn 2 got a copy of the file as it is now, not the copy of turn 1");
    }

    [Fact]
    public async Task D3_ALaunchThatCannotHaveTheMargin_IsRefusedAndNeverStartedOnLess()
    {
        // The access token ends in three minutes and a turn may take a minute: the real file has no margin, so a refresh would be tried — and the program here cannot refresh — and the launch is refused.
        await using var unit = await AgyUnit.CreateAsync(realExpiry: DateTime.UtcNow.AddMinutes(3));

        var outcome = await unit.RunAsync("echo started");

        var refused = outcome.Should().BeOfType<CoderTurnOutcome.Refused>().Subject;
        refused.Sentence.Should().MatchRegex("no launch is started|launch was not started");
        unit.Bed.Launcher.Requests.SelectMany(request => request.Arguments).Should().NotContain(argument => argument.Contains($"coai-coder-{unit.Bed.JobId}-c1", StringComparison.Ordinal), "nothing of the coder started");
        File.Exists(AgyCoderUnit.SignInFile(unit.Bed.Job)).Should().BeFalse("no copy that cannot be used is left in the job's home");
    }

    [Fact]
    public async Task D3_TheDeadlineOfAnAgyTurn_FitsUnderTheLifeOfAToken_WhateverTheJobsAttemptTimeoutIs()
    {
        await using var rig = await AgyCoderBed.RigAsync();
        rig.AttemptTimeout = TimeSpan.FromMinutes(60);
        rig.SaysEntries(AgyCoderBed.Model, AgyCoderBed.Launch([AgyCoderBed.Writes(CoderRig.Green)], conversation: AgyCoderBed.Conversation));
        var job = rig.NewJob(route: [$"agy/{AgyCoderBed.Model}"], timeoutMinutes: 120);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var record = JsonDocument.Parse(File.ReadAllText(Directory.EnumerateFiles(rig.World.Store.FolderOf(job.JobId)!.Location, "unit-c1.json", SearchOption.AllDirectories).First())).RootElement;
        var seconds = record.GetProperty("properties").EnumerateArray().Single(item => item.GetProperty("name").GetString() == "RuntimeMaxSec").GetProperty("value").GetString();
        // 45 minutes of turn and a minute of backstop: the unit's end is under the hour a token lives, with the five minutes of margin the gate asks beyond it.
        int.Parse(seconds!, System.Globalization.CultureInfo.InvariantCulture).Should().Be((45 + 1) * 60);
        AgyCoderBed.Vendor().TurnDeadline.Should().Be(TimeSpan.FromMinutes(45));
        CoderVendors.Claude.TurnDeadline.Should().BeNull("only a vendor with a sign-in that runs out caps its turns");
    }

    [Fact]
    public async Task M14_ARealTokenThatIsNotExpired_IsRefreshedThroughAScratchCopyMadeStale_TheRealFileIsNeverTouched()
    {
        // Three minutes left: less than the unit's end and the margin (a minute of turn, a minute of backstop, five of margin). agy refreshes only a token that is EXPIRED (measured), so coai makes the token of its OWN scratch copy stale before the unit runs.
        await using var unit = await AgyUnit.CreateAsync(realExpiry: DateTime.UtcNow.AddMinutes(3));
        unit.Bed.Inner.UseJobsFolderWithAnotherJob();
        var realBytes = File.ReadAllBytes(unit.Real);
        var realTime = File.GetLastWriteTimeUtc(unit.Real);

        var outcome = await unit.RunAsync("unused", asked: unit.Bed.Request(RoleJobWorld.FakeCli, "emit", "started"));

        outcome.Should().BeOfType<CoderTurnOutcome.Finished>(outcome is CoderTurnOutcome.Refused refused ? refused.Sentence : string.Empty);
        var copy = File.ReadAllText(AgyCoderUnit.SignInFile(unit.Bed.Job));
        copy.Should().Contain("made-up-agy-access-token-refreshed", "the coder's copy was cut from the file the refresh unit rewrote").And.NotContain("refresh_token");
        File.ReadAllBytes(unit.Real).Should().Equal(realBytes, "the real file is read, never written");
        File.GetLastWriteTimeUtc(unit.Real).Should().Be(realTime);
        unit.SignIns.All().Single().Refreshes.Should().Be(1, "the refresh is counted");
        Directory.Exists(unit.Bed.Job.RefreshFolder).Should().BeFalse("the scratch copy, with the full file in it, is deleted before the coder starts");
    }

    [Fact]
    public async Task M14_ARealTokenWithTheMarginInIt_NeedsNoRefresh_AndNoneIsRun()
    {
        await using var unit = await AgyUnit.CreateAsync(realExpiry: DateTime.UtcNow.AddMinutes(58));
        unit.Bed.Inner.UseJobsFolderWithAnotherJob();

        var outcome = await unit.RunAsync("unused", asked: unit.Bed.Request(RoleJobWorld.FakeCli, "emit", "started"));

        outcome.Should().BeOfType<CoderTurnOutcome.Finished>(outcome is CoderTurnOutcome.Refused refused ? refused.Sentence : string.Empty);
        unit.SignIns.All().Single().Refreshes.Should().Be(0);
    }

    [Fact]
    public async Task M14_TheEnvelopeOfAJob_CountsTheRefreshes()
    {
        await using var rig = await AgyCoderBed.RigAsync(expires: DateTime.UtcNow.AddMinutes(3));
        rig.SaysEntries(AgyCoderBed.Model, AgyCoderBed.Launch([AgyCoderBed.Writes(CoderRig.Green)], conversation: AgyCoderBed.Conversation));
        var job = rig.NewJob(route: [$"agy/{AgyCoderBed.Model}"]);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var record = rig.Envelope(job).GetProperty("signIn").EnumerateArray().Single(item => item.GetProperty("vendor").GetString() == "agy");
        record.GetProperty("refreshes").GetInt32().Should().Be(1);
        record.GetProperty("changed").GetBoolean().Should().BeFalse("the real file is the same file");
    }

    [Fact]
    public async Task D3_AnExpiredSignIn_IsRefusedBeforeAnyStart_NeverALoginPromptOnTheChannelOfThePrompt()
    {
        await using var unit = await AgyUnit.CreateAsync(realExpiry: DateTime.UtcNow.AddHours(-2));

        var outcome = await unit.RunAsync("cat > /dev/null; echo started");

        outcome.Should().BeOfType<CoderTurnOutcome.Refused>();
        unit.Bed.Launcher.Requests.SelectMany(request => request.Arguments).Should().NotContain(argument => argument.Contains($"coai-coder-{unit.Bed.JobId}-c1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task D4_TheEnvironmentInsideTheUnit_IsExactlyTheAllowList_ByName_AndNoSecretIsInAnyRecordedArgv()
    {
        Environment.SetEnvironmentVariable("COAI_AGY_CANARY", "canary-value-of-the-parent");
        try
        {
            await using var unit = await AgyUnit.CreateAsync();
            var allowed = AgyCoderBed.Vendor().Environment(unit.Bed.Host, unit.Bed.Job).Select(pair => pair.Key).Order(StringComparer.Ordinal).ToList();

            var stdout = Out(await unit.RunAsync("""env | sed -n 's/^\([A-Za-z_][A-Za-z0-9_]*\)=.*/\1/p' | grep -vxE 'PWD|OLDPWD|SHLVL|_' | sort | tr '\n' ' '; echo; env | grep -c canary; true"""));

            stdout.Split('\n')[0].Split(' ', StringSplitOptions.RemoveEmptyEntries).Should().Equal(allowed, "compared name by name, from inside a real unit");
            allowed.Should().NotContain(name => name.Contains("TOKEN", StringComparison.Ordinal) || name.Contains("KEY", StringComparison.Ordinal) || name.Contains("OAUTH", StringComparison.Ordinal) || name.Contains("GEMINI", StringComparison.Ordinal) || name.Contains("ANTIGRAVITY", StringComparison.Ordinal), "agy gets no variable for its sign-in");
            stdout.Split('\n')[1].Trim().Should().Be("0");
            var argv = string.Join('\n', unit.Bed.Launcher.Requests.SelectMany(request => request.Arguments));
            argv.Should().NotContain("canary-value-of-the-parent").And.NotContain(AgyCoderBed.Access).And.NotContain(AgyCoderBed.Refresh);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COAI_AGY_CANARY", null);
        }
    }

    // ---------- d6: the prompt ----------

    private static string Repo(params string[] parts) => Path.Combine([RepoPaths.Root(), "src_mcp", "src", "Roles", "prompts", .. parts]);

    private const string AgyToolsBullet =
        "- You may use the tools read_file, write_file and edit_file. Call no other tool. You have no command tool and no search\n"
        + "  tool. You cannot run a command, and you must not try to.\n"
        + "- An instruction file of the repository (`AGENTS.md`, `CLAUDE.md` and the files they name) may order a command before\n"
        + "  work. Do not run it; that is no reason to refuse the task. What coai ran for this job before your turn is at the end\n"
        + "  of this prompt, under the heading \"Run by coai before your turn\". When that section says that nothing was run, do the\n"
        + "  task from what you can read and name the command in `notes`. Every other rule of those files still applies.\n";

    private const string ClaudeToolsBullet =
        "- You may use the tools Read, Glob, Grep, Edit and Write. You have no shell. You cannot run a command, and you must not\n"
        + "  try to.\n";

    [Fact]
    public void D6_TheAgyPrompt_DiffersFromTheClaudePromptInTheToolsBulletAndTheOneAddedBullet_AndNowhereElse()
    {
        var claude = File.ReadAllText(Repo("coder.md"));
        var agy = File.ReadAllText(Repo("coder.agy.md"));

        claude.Should().Contain(ClaudeToolsBullet);
        agy.Should().Be(claude.Replace(ClaudeToolsBullet, AgyToolsBullet, StringComparison.Ordinal), "every other line is byte-equal");
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(claude)).Should().Equal(Convert.FromHexString("ee31db20b05e779b817131fdb615be1225271dfad715e3b647cfa38d7d92d7bf"), "coder.md is not changed");
    }

    [Fact]
    public void D6_EveryToolNameOfThePrompt_IsAToolOfTheMeasuredAgentFile_AndNoCommandToolIsNamed()
    {
        var named = Regex.Match(AgyToolsBullet, @"tools (?<list>[a-z_, ]+?)\. Call no other").Groups["list"].Value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries).Where(word => word != "and").ToList();
        // The agent file lists the INIT names of its tools; the hook and the prompt use the names the model sees.
        var initNameOf = new Dictionary<string, string[]> { ["read_file"] = ["view_file"], ["write_file"] = ["write_to_file"], ["edit_file"] = ["replace_file_content", "multi_replace_file_content"] };

        named.Should().Equal("read_file", "write_file", "edit_file");
        named.Should().OnlyContain(name => AntigravityCoderGuard.AllowedTools.Contains(name));
        named.Should().OnlyContain(name => initNameOf[name].All(init => AntigravityCoderGuard.AgentFile.Contains($"  - {init}\n", StringComparison.Ordinal)));
        AntigravityCoderGuard.AgentFile.Should().Contain("commandExecutionPolicy: \"off\"").And.NotContain("run_command");
        File.ReadAllText(Repo("coder.agy.md")).Should().NotContain("run_command");
    }

    [Fact]
    public void D6_TheVendorNamesItsOwnPrompt_AndTheServerEmbedsIt()
    {
        var vendor = AgyCoderBed.Vendor();

        vendor.PromptId.Should().Be("coder-agy").And.NotBe(CoderVendors.Claude.PromptId);
        vendor.PointsAtInstructionBlock.Should().BeTrue("a job with no instruction step is still sent the block that says nothing was run");
        PromptTemplate.Embedded(vendor.PromptResource).Should().Be(File.ReadAllText(Repo("coder.agy.md")));
    }

    [Fact]
    public async Task D6_TheFakeAsAgy_IsLaunchedAsTheCoder_AndRecordsThePromptOnTurnZero_InTheUnitOfTheSeam()
    {
        await using var rig = await AgyCoderBed.RigAsync();
        rig.SaysEntries(AgyCoderBed.Model, AgyCoderBed.Launch([AgyCoderBed.Writes(CoderRig.Green)], conversation: AgyCoderBed.Conversation));
        var job = rig.NewJob(route: [$"agy/{AgyCoderBed.Model}"]);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var launch = rig.Launches(job).Should().ContainSingle().Subject;
        launch.Should().Contain(["--mode", "accept-edits", "--agent", "coai-coder", "--add-dir", rig.WorktreeOf(job)]).And.NotContain("--conversation");
        var prompt = launch[^1];
        prompt.Should().Contain("You may use the tools read_file, write_file and edit_file").And.Contain("Run by coai before your turn").And.Contain("Nothing was run");
        prompt.Should().NotContain("Read, Glob, Grep", "the claude tool names are never sent to agy");
        var envelope = rig.Envelope(job);
        envelope.GetProperty("green").GetBoolean().Should().BeTrue();
        var record = JsonDocument.Parse(File.ReadAllText(Directory.EnumerateFiles(rig.World.Store.FolderOf(job.JobId)!.Location, "unit-c1.json", SearchOption.AllDirectories).First())).RootElement;
        var values = record.GetProperty("properties").EnumerateArray().Select(item => item.GetProperty("name").GetString() + "=" + item.GetProperty("value").GetString()).ToList();
        values.Should().Contain($"BindReadOnlyPaths={AgyCoderUnit.GuardFolder(JobOf(rig, job))}");
    }

    private static BoundaryJob JobOf(CoderRig rig, RoleJobRecord job)
    {
        var tree = new CoaiMcp.Runners.Worktrees.CoderTree(rig.WorktreeOf(job), rig.Repo, string.Empty);

        return BoundaryJob.ForCoder(job.JobId, tree, rig.JobFolder(job));
    }

    [Fact]
    public async Task TheSignInInTheShapeOfTheRealFile_NineFractionDigitsAndAPlusOffset_PassesTheCopyRowAndTheLaunchIsAdmitted()
    {
        // The first real run found it: the copy is written as JSON, which spells the plus of the offset as \u002B, and the row inside the unit read the time with date(1).
        await using var unit = await AgyUnit.CreateAsync(realExpiry: DateTime.UtcNow.AddHours(2), realShape: true);

        var outcome = await unit.RunAsync("echo ran");

        outcome.Should().BeOfType<CoderTurnOutcome.Finished>(outcome.ToString());
    }

    /// <summary>
    /// The list the flip of agy (s8 t6) rests on: each must-change of the passes of the security verify, with the smallest proof that it is in place; the full tests are in their own classes (M11 <c>AAgyCoderFixTurnsTests</c>, M12 <c>AAgyCoderPreflightTests</c>, M13 and R-M15 <c>AVendorsHaveTheirOwnTempFoldersTests</c>, M14 here,
    /// M18 <c>AAgyTurnCheckDoesNotWaitTests</c>, M19 and S55 <c>AUnitWrittenEntriesTests</c>, the expiry row <c>TheSignInInTheShapeOfTheRealFile_…</c> above).
    /// </summary>
    [Fact]
    public void D4_EveryMustChangeOfTheAgyJudgement_IsBuilt_ThisIsTheListTheFlipOfAgyRestsOn()
    {
        var job = BoundaryJob.ForCoder("r20261127101010-0000000c", new CoaiMcp.Runners.Worktrees.CoderTree(Path.Combine(Path.GetFullPath("roots"), "coai-coder-r20261127101010-0000000c"), Path.GetFullPath("main"), "gitdir: x"), Path.Combine(Path.GetFullPath("jobs"), "r20261127101010-0000000c"));
        var init = "{\"event\":\"init\",\"conversation_id\":\"c\",\"init\":{\"permission_mode\":\"request-review\"}}\n";
        var other = "{\"event\":\"step_update\",\"step_update\":{\"conversation_id\":\"c\",\"step_index\":2,\"state\":\"ACTIVE\",\"step_type\":\"tool\",\"tool_name\":\"run_command\"}}\n";

        // M11: the check runs for a cut turn, and an event of a tool outside the three is a boundary in every state.
        AgyTurnCheck.Check(new ProcessResult(1, init + other, string.Empty, false), string.Empty, string.Empty).Should().BeOfType<CoderTurnVerdict.Boundary>("M11, leg b");
        // M12: the rows of the keys, the sign-in and the other vendors' folders.
        AgyCoderPreflight.ExtraItems.Should().Contain(["keys-folder-hidden", "sign-in-not-in-unit", "other-vendor-folder-hidden"], "M12");
        // M13 and R-M15: a vendor's own temp sources, outside every folder a unit binds writable.
        job.CoderTempOf("agy").Should().StartWith(job.CoderTempRoot).And.NotStartWith(job.CoderFolder + Path.DirectorySeparatorChar, "M13, R-M15");
        // M14: the recipe refreshes through `models` on a copy made stale.
        ((CoderSignInRecipe.CopyForJob)AgyCoderBed.Vendor().SignIn).RefreshArgv.Should().Equal("models");
        // M18: the stream is judged before the records are read.
        var read = false;
        AgyTurnCheck.Check(new ProcessResult(0, init + other, string.Empty, false), string.Empty, () => { read = true; return string.Empty; }).Should().BeOfType<CoderTurnVerdict.Boundary>("M18");
        read.Should().BeFalse("M18, part 1");
        // M20: the worktree's toolchain file is asked in every round by the one road; a FIFO there ends the job `checks-changed-tree` (AToolchainFileOfTheWorktreeTests, APassTenShouldsTests).
        Assert.SkipWhen(OperatingSystem.IsWindows(), "a FIFO needs a Unix");
        using var temp = TempDir.For("coai-d4-m20-");
        var fifo = Path.Combine(temp.Path, "toolchain.yaml");
        using (var made = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("mkfifo", [fifo]) { UseShellExecute = false })!)
        {
            made.WaitForExit();
        }

        new CoaiMcp.Work.DiskWorkFolder(temp.Path).Read("toolchain.yaml", 1024).Should().BeOfType<CoaiMcp.Core.Work.WorkFile.NotRegular>("M20: a FIFO is never waited on");
        // S26, R-S7: the refresh unit hides the key pool and every real sign-in file (APassTenShouldsTests).
        // M19: the limit of a check is one fixed 30 seconds, and the sign-in copy is read by the road that never waits.
        CoderTurnCheck.CheckBudget.Should().Be(TimeSpan.FromSeconds(30));
        AgyCoderBed.Vendor().HostReadEntries(job).Select(entry => entry.Path).Should().Contain(AgyCoderUnit.SignInFile(job), "M19");
    }
}

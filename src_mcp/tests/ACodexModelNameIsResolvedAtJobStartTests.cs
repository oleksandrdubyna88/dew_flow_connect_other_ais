using System.Text.Json;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.CodexScript;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s2 t5 (V30, V32, V34; <c>SPEC_CHANGES.md</c> section Q; ledger entry 2.1): a codex name with no version is resolved to the newest model the installed CLI lists when the job reaches the entry, once; a name that cannot be used ends the entry <c>model-unavailable</c> with nothing launched, and the failover goes on.
/// FakeCli is codex: its <c>debug models</c> answers from the recording of the real command, or from a list a test names (<c>slugs:…</c>); every call is a sidecar of the record folder, and no call of it is a launch of a model. No paid call, no real home.
/// </summary>
[Collection("fakecli-env")]
public sealed class ACodexModelNameIsResolvedAtJobStartTests
{
    private static IReadOnlyList<string[]> ModelCalls(string folder) =>
        Directory.Exists(Path.Combine(folder, "sidecars")) ? [.. Directory.EnumerateFiles(Path.Combine(folder, "sidecars"), "*.models").Select(File.ReadAllLines)] : [];

    private static async Task WithList(string? list, Func<Task> body)
    {
        Environment.SetEnvironmentVariable("FAKECLI_CODEX_MODELS", list);
        try
        {
            await body();
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_CODEX_MODELS", null);
        }
    }

    // ---------- V30: the name, a read-only role ----------

    [Fact]
    public async Task V30_AFamilyIsResolvedOnce_WhenTheJobReachesTheEntry_AndTheLaunchUsesTheResolvedName_NotTheFamily()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        rig.Setting("COAI_ROLE_CODEX_CLI", RoleJobWorld.FakeCli).Says("gpt-6.1-terra", FakeRoleAnswers.CodexSays(FakeRoleAnswers.Ok));
        var job = rig.NewJob(["codex/terra"]);

        var (_, after) = await rig.RunAsync(job);

        after.State.Should().Be(RoleJobStates.Succeeded, after.Reason);
        rig.Models().Should().Equal("gpt-6.1-terra");
        after.Attempts.Should().ContainSingle().Which.Should().Match<RoleAttempt>(attempt => attempt.Model == "terra" && attempt.ResolvedModel == "gpt-6.1-terra");
        var calls = ModelCalls(rig.RecordFolder);
        calls.Should().ContainSingle("the list is asked once, for the entry");
        calls[0][0].Should().Be(rig.Repo, "the list is asked from the folder the launch that follows runs in");
        rig.LedgerLines().Should().ContainSingle().Which.GetProperty("model").GetString().Should().Be("gpt-6.1-terra", "the usage line names the resolved name");
    }

    [Fact]
    public async Task V30_ARouteWhoseFirstModelAnswers_MakesNoCodexCall()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        rig.Setting("COAI_ROLE_CODEX_CLI", RoleJobWorld.FakeCli).Says("sonnet", FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok));
        var job = rig.NewJob(["claude/sonnet", "codex/terra"]);

        var (_, after) = await rig.RunAsync(job);

        after.State.Should().Be(RoleJobStates.Succeeded);
        ModelCalls(rig.RecordFolder).Should().BeEmpty("the entry was never reached");
    }

    [Fact]
    public async Task V30_ANameGivenInFull_IsUsedAsGiven_EvenWhenTheListHoldsANewerModel()
    {
        await WithList("slugs:gpt-6.9-terra=list,gpt-6.1-terra=list", async () =>
        {
            await using var rig = await RoleFailoverRig.CreateAsync();
            rig.Setting("COAI_ROLE_CODEX_CLI", RoleJobWorld.FakeCli).Says("gpt-6.1-terra", FakeRoleAnswers.CodexSays(FakeRoleAnswers.Ok));
            var job = rig.NewJob(["codex/gpt-6.1-terra"]);

            var (_, after) = await rig.RunAsync(job);

            after.State.Should().Be(RoleJobStates.Succeeded, after.Reason);
            rig.Models().Should().Equal("gpt-6.1-terra");
            after.Attempts.Single().ResolvedModel.Should().BeEmpty("a name given in full is not replaced");
        });
    }

    [Fact]
    public async Task V30_ANewModelThatComesOut_IsTakenByTheNextJobWithNoEdit()
    {
        await WithList("slugs:gpt-7-terra=list,gpt-6.2-terra=list,gpt-6.1-terra=list,gpt-9-terra=hide", async () =>
        {
            await using var rig = await RoleFailoverRig.CreateAsync();
            rig.Setting("COAI_ROLE_CODEX_CLI", RoleJobWorld.FakeCli).Says("gpt-7-terra", FakeRoleAnswers.CodexSays(FakeRoleAnswers.Ok));

            var (_, after) = await rig.RunAsync(rig.NewJob(["codex/terra"]));

            after.State.Should().Be(RoleJobStates.Succeeded, after.Reason);
            rig.Models().Should().Equal("gpt-7-terra");
        });
    }

    [Fact]
    public async Task V30_TheLineOfAModelSwitch_AndTheNotice_NameTheNameAsAskedAndAsResolved()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        rig.Setting("COAI_ROLE_CODEX_CLI", RoleJobWorld.FakeCli).Says("gpt-6.1-terra", FakeRoleAnswers.CodexSays(FakeRoleAnswers.Refusal)).Says("sonnet", FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok));
        var job = rig.NewJob(["codex/terra", "claude/sonnet"]);

        var (_, after) = await rig.RunAsync(job);

        after.State.Should().Be(RoleJobStates.Succeeded, after.Reason);
        rig.World.LogText.Should().Contain("codex/terra (resolved gpt-6.1-terra)");
        rig.World.Notices.Single(notice => notice.Code == "role-model-switched").Title.Should().Contain("codex/terra (resolved gpt-6.1-terra)");
    }

    // ---------- V32: model-unavailable, a read-only role ----------

    [Theory]
    [InlineData("codex/terra", "slugs:gpt-6.2-astra=list,gpt-9-terra=hide,gpt-terra-latest=list", "a family with no model in the list")]
    [InlineData("codex/gpt-6.3-terra", "slugs:gpt-6.2-terra=list,gpt-6.1-terra=list", "a full name of the shape that the list does not hold")]
    public async Task V32_AnEntryThatCannotBeUsed_FailsOverAtOnce_NothingIsLaunched_AndNoLedgerLineIsMadeForIt(string entry, string list, string why)
    {
        await WithList(list, async () =>
        {
            await using var rig = await RoleFailoverRig.CreateAsync();
            rig.Setting("COAI_ROLE_CODEX_CLI", RoleJobWorld.FakeCli).Says("sonnet", FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok));
            var job = rig.NewJob([entry, "claude/sonnet"]);
            var clock = System.Diagnostics.Stopwatch.StartNew();

            var (_, after) = await rig.RunAsync(job, ladder: [TimeSpan.FromMinutes(5)]);

            clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(60), "no wait: the next entry starts at once");
            after.State.Should().Be(RoleJobStates.Succeeded, $"{why}: {after.Reason}");
            after.Attempts.Select(attempt => (attempt.Vendor, attempt.Stop)).Should().Equal(("codex", "model-unavailable"), ("claude", string.Empty));
            after.Attempts[0].Reason.Should().Contain("codex CLI");
            rig.Launches().Should().HaveCount(1, "FakeCli counts zero launches of the codex model: only the next entry ran");
            rig.LedgerLines().Should().ContainSingle("the usage ledger has no line for the entry that was never launched").Which.GetProperty("model").GetString().Should().Be("sonnet");
        });
    }

    [Theory]
    [InlineData("exit1")]
    [InlineData("garbage")]
    [InlineData("huge")]
    public async Task V32_AListThatCannotBeRead_IsModelUnavailable_AndTheOtherModelsOfTheVendorAreSkipped(string how)
    {
        await WithList(how, async () =>
        {
            await using var rig = await RoleFailoverRig.CreateAsync();
            rig.Setting("COAI_ROLE_CODEX_CLI", RoleJobWorld.FakeCli).Says("sonnet", FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok));
            var job = rig.NewJob(["codex/terra", "codex/astra", "claude/sonnet"]);

            var (_, after) = await rig.RunAsync(job);

            after.State.Should().Be(RoleJobStates.Succeeded, after.Reason);
            after.Attempts.Select(attempt => (attempt.Model, attempt.Stop)).Should().Equal(("terra", "model-unavailable"), ("sonnet", string.Empty));
            ModelCalls(rig.RecordFolder).Should().ContainSingle("the other models of the vendor would stop the same way: they are skipped");
        });
    }

    [Fact]
    public async Task V32_AListThatComesAfterTheDeadline_IsModelUnavailable()
    {
        var before = CodexModelList.Deadline;
        CodexModelList.Deadline = TimeSpan.FromSeconds(2);
        try
        {
            await WithList("slow", async () =>
            {
                await using var rig = await RoleFailoverRig.CreateAsync();
                rig.Setting("COAI_ROLE_CODEX_CLI", RoleJobWorld.FakeCli).Says("sonnet", FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok));

                var (_, after) = await rig.RunAsync(rig.NewJob(["codex/terra", "claude/sonnet"]));

                after.Attempts.Select(attempt => attempt.Stop).Should().Equal("model-unavailable", string.Empty);
            });
        }
        finally
        {
            CodexModelList.Deadline = before;
        }
    }

    [Fact]
    public async Task V32_ALostSignIn_AndAMissingCli_KeepTheirOwnWordsAndSentences_AndSkipTheVendor()
    {
        await WithList("sign-in-lost", async () =>
        {
            await using var rig = await RoleFailoverRig.CreateAsync();
            rig.Setting("COAI_ROLE_CODEX_CLI", RoleJobWorld.FakeCli).Says("sonnet", FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok));

            var (_, after) = await rig.RunAsync(rig.NewJob(["codex/terra", "codex/astra", "claude/sonnet"]));

            after.Attempts.Select(attempt => (attempt.Model, attempt.Stop)).Should().Equal(("terra", "crash"), ("sonnet", string.Empty));
            after.Attempts[0].Reason.Should().Contain("the sign-in is lost");
        });

        await using var missing = await RoleFailoverRig.CreateAsync();
        missing.Setting("COAI_ROLE_CODEX_CLI", Path.Combine(missing.Repo, "no-such-codex-cli")).Says("sonnet", FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok));

        var (_, gone) = await missing.RunAsync(missing.NewJob(["codex/terra", "codex/astra", "claude/sonnet"]));

        gone.Attempts.Select(attempt => (attempt.Model, attempt.Stop)).Should().Equal(("terra", "crash"), ("sonnet", string.Empty));
    }

    [Fact]
    public async Task V32_OnTheLastEntry_TheJobEndsFailedWithTheWord_AndDoesNotWait_AndTheWordIsNeverABoundaryOrARouteWord()
    {
        await WithList("exit1", async () =>
        {
            await using var rig = await RoleFailoverRig.CreateAsync();
            rig.Setting("COAI_ROLE_CODEX_CLI", RoleJobWorld.FakeCli);
            var job = rig.NewJob(["codex/terra"]);
            var clock = System.Diagnostics.Stopwatch.StartNew();

            var (_, after) = await rig.RunAsync(job, ladder: [TimeSpan.FromMinutes(5)]);

            clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(60));
            after.State.Should().Be(RoleJobStates.Failed);
            after.Attempts.Should().ContainSingle().Which.Stop.Should().Be("model-unavailable");
            after.Stop.Should().Be("model-unavailable", "the last entry ends the job with the word");
            (after.Reason + string.Join(' ', after.Attempts.Select(attempt => attempt.Stop))).Should().NotContain("coder-boundary").And.NotContain("route-invalid");
            rig.Launches().Should().BeEmpty();
            rig.LedgerLines().Should().BeEmpty();
        });
    }

    [Fact]
    public async Task V32_AHiddenModelNamedInFull_IsAllowed_AndLaunchedAsGiven_OnlyTheFamilyRuleReadsVisibility()
    {
        await WithList("slugs:gpt-6.1-terra=hide,gpt-6.0-terra=list", async () =>
        {
            await using var rig = await RoleFailoverRig.CreateAsync();
            rig.Setting("COAI_ROLE_CODEX_CLI", RoleJobWorld.FakeCli).Says("gpt-6.1-terra", FakeRoleAnswers.CodexSays(FakeRoleAnswers.Ok));

            var (_, after) = await rig.RunAsync(rig.NewJob(["codex/gpt-6.1-terra"]));

            after.State.Should().Be(RoleJobStates.Succeeded, after.Reason);
            rig.Models().Should().Equal("gpt-6.1-terra");
            ModelCalls(rig.RecordFolder).Should().ContainSingle("a full name of the exact shape is asked of the list (Q2) — and a hidden one passes");
        });
    }

    [Fact]
    public async Task V32_AFullNameWithNoFamily_IsNeverLookedUpInTheList_TheCliAnswersIt()
    {
        await WithList("slugs:gpt-6.1-terra=list", async () =>
        {
            await using var rig = await RoleFailoverRig.CreateAsync();
            rig.Setting("COAI_ROLE_CODEX_CLI", RoleJobWorld.FakeCli).Says("gpt-5.5", FakeRoleAnswers.CodexSays(FakeRoleAnswers.Ok));

            var (_, after) = await rig.RunAsync(rig.NewJob(["codex/gpt-5.5"]));

            after.State.Should().Be(RoleJobStates.Succeeded, after.Reason);
            rig.Models().Should().Equal("gpt-5.5");
            ModelCalls(rig.RecordFolder).Should().BeEmpty("gpt-5.5 has no family and the list does not hold it: no list read is made for it");
        });
    }

    // ---------- V30, V32, V34: a coder ----------

    private static readonly string[] Two = ["codex/terra", "codex/astra"];

    private static string ModelOf(string[] launch) => launch[Array.IndexOf(launch, "-m") + 1];

    [Fact]
    public async Task V30_ACoder_ResolvesTheDefaultOnce_InTheUnitOfTheLaunch_AndEveryTurnOfTheSessionUsesTheRecordedName()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig;
        rig.SaysRaw("gpt-6.1-terra", Turn(Writes(CoderRig.Red + " a"), Report()), Turn(Writes(CoderRig.Green), Report()));
        var job = rig.NewJob(route: ["codex/terra"]);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var launches = rig.Launches(job);
        launches.Should().HaveCount(2);
        launches.Select(ModelOf).Should().OnlyContain(name => name == "gpt-6.1-terra", "the fresh turn and the resumed fix turn launch with the recorded name, never with the family");
        var calls = ModelCalls(rig.LaunchFolder(job));
        calls.Should().ContainSingle("a fix turn never resolves again");
        calls[0][0].Should().EndWith(Path.Combine("coder", "codex", "models-cwd"), "in the unit of the launch, in a folder of the job's own that holds no text of the repository");
        calls[0][2].Should().EndWith(Path.Combine("coder", "codex", "home", ".codex"), "the job's own CODEX_HOME, never the real home");
        var model = rig.Envelope(job).GetProperty("model");
        model.GetProperty("asked").GetString().Should().Be("terra");
        model.GetProperty("resolved").GetString().Should().Be("gpt-6.1-terra");
        model.GetProperty("toolsAbsent").GetArrayLength().Should().Be(0);
        ended.Attempts.Single().ResolvedModel.Should().Be("gpt-6.1-terra");
        rig.World.Store.FolderOf(job.JobId)!.ReadResult().Should().NotContain("\"model\":\"terra\"");
    }

    [Fact]
    public async Task V30_TheListOfACoder_IsReadInAFolderWithNoTextOfTheRepository_NeverInTheWorktree()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig;
        rig.SaysRaw("gpt-6.1-terra", Turn(Writes(CoderRig.Green), Report()));
        var job = rig.NewJob(route: ["codex/terra"]);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var call = ModelCalls(rig.LaunchFolder(job)).Should().ContainSingle().Subject;
        Directory.EnumerateFileSystemEntries(rig.WorktreeOf(job)).Should().Contain(path => Path.GetFileName(path) == "plan", "a control: the worktree holds the text of the fixture repository");
        call[0].Should().NotBe(rig.WorktreeOf(job)).And.NotStartWith(rig.WorktreeOf(job) + Path.DirectorySeparatorChar);
        call[4].Should().BeEmpty("the folder the list was read in held nothing: no AGENTS.md, no .codex, no plan of the repository");
    }

    [Fact]
    public async Task V32_AUnitRefusedDuringTheListRead_IsCoderBoundary_TheJobEnds_AndTheNextEntryIsNotTried()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig;
        rig.OnHost = host => host with { SystemdRun = "coai-no-such-systemd-run" };
        var job = rig.NewJob(route: Two);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "coder-boundary"), ended.Reason);
        ended.Attempts.Should().ContainSingle("the next entry is not tried: a boundary stop never fails over");
        rig.Launches(job).Should().BeEmpty();
    }

    [Fact]
    public async Task V32_AWriteTheOsLayerStoppedDuringTheListRead_IsCoderBoundary_NeverModelUnavailable()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig.Steering(("FAKECLI_CODEX_MODELS", "write-stopped"));
        var job = rig.NewJob(route: Two);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "coder-boundary"), ended.Reason);
        ended.Attempts.Should().ContainSingle().Which.Stop.Should().Be("coder-boundary");
        ended.Reason.Should().Contain("Read-only file system");
        rig.Launches(job).Should().BeEmpty();
    }

    private static IReadOnlyList<string> WithoutStopTimeout(IReadOnlyList<string> arguments)
    {
        var list = arguments.ToList();
        var at = list.FindIndex(argument => argument.StartsWith("TimeoutStopSec=", StringComparison.Ordinal));
        if (at > 0)
        {
            list.RemoveRange(at - 1, 2);
        }

        return list;
    }

    [Fact]
    public async Task V32_AUnitThatDoesNotStopDuringTheListRead_IsCoderBoundary_AndNoSecondUnitStartsBesideIt()
    {
        var before = CodexModelList.Deadline;
        CodexModelList.Deadline = TimeSpan.FromSeconds(3);
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig.Steering(("FAKECLI_CODEX_MODELS", "ignores-term"));
        rig.Degrade = arguments => arguments.Any(argument => argument.StartsWith("coai-coder-", StringComparison.Ordinal)) ? WithoutStopTimeout(arguments) : arguments;
        var job = rig.NewJob(route: Two);
        try
        {
            var (_, ended) = await rig.RunAsync(job);

            (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "coder-boundary"), ended.Reason);
            ended.Attempts.Should().ContainSingle();
            ended.Reason.Should().Contain("could not be stopped");
            rig.Launches(job).Should().BeEmpty();
        }
        finally
        {
            CodexModelList.Deadline = before;
            foreach (var unit in await rig.UnitsOfAsync(job))
            {
                await new CoaiMcp.Runners.Processes.ProcessLauncher().RunAsync(new CoaiMcp.Runners.Processes.ProcessRequest("systemctl", ["--user", "kill", "--signal=SIGKILL", "--kill-whom=all", unit.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]], rig.Repo) { Timeout = TimeSpan.FromSeconds(20) });
            }
        }
    }

    [Fact]
    public async Task V33_ACoderWhoseModelHasFewerTools_IsAdmitted_AndTheEnvelopeNamesTheKnownToolsItHasNot()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig.Steering(("FAKECLI_CODEX_TOOLS", "nine"));
        rig.SaysRaw("gpt-6.1-terra", Turn(Writes(CoderRig.Green), Report()));
        var job = rig.NewJob(route: ["codex/terra"]);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        rig.Envelope(job).GetProperty("model").GetProperty("toolsAbsent").EnumerateArray().Select(tool => tool.GetString()).Should().BeEquivalentTo("write_stdin", "read_mcp_resource");
    }

    [Fact]
    public async Task V32_ACoderFamilyWithNoModel_FailsOverToTheNextEntry_AtOnce_AndIsNeverLaunched()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig.Steering(("FAKECLI_CODEX_MODELS", "slugs:gpt-6.2-astra=list,gpt-6.1-sol=list"));
        rig.SaysRaw("gpt-6.2-astra", Turn(Writes(CoderRig.Green), Report()));
        var job = rig.NewJob(route: Two);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        ended.Attempts.Select(attempt => (attempt.Model, attempt.Stop)).Should().Equal(("terra", "model-unavailable"), ("astra", string.Empty));
        rig.Launches(job).Select(ModelOf).Should().Equal("gpt-6.2-astra");
        rig.Envelope(job).GetProperty("model").GetProperty("resolved").GetString().Should().Be("gpt-6.2-astra");
    }

    [Fact]
    public async Task V32_AToolListThatCannotBeRead_InBothReads_IsModelUnavailable_AndTheOtherModelsOfTheVendorAreStillTried()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig.Steering(("FAKECLI_CODEX_TOOLS", "unreadable"));
        var job = rig.NewJob(route: Two);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Failed);
        ended.Attempts.Select(attempt => (attempt.Model, attempt.Stop)).Should().Equal(("terra", "model-unavailable"), ("astra", "model-unavailable"));
        ended.Attempts[0].Reason.Should().Contain("tool-list").And.Contain("cannot be used on this machine now");
        rig.Launches(job).Should().BeEmpty("no model was launched");
    }

    [Fact]
    public async Task V33_AnUnknownTool_AtTheFirstLaunch_IsModelUnavailable_TheNextEntryIsLaunched_AndOneNoticeNamesTheTool()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig.Steering(("FAKECLI_CODEX_TOOLS", "new-tool-in:gpt-6.1-terra"));
        rig.SaysRaw("gpt-6.2-astra", Turn(Writes(CoderRig.Green), Report()));
        var job = rig.NewJob(route: Two);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        ended.Attempts.Select(attempt => (attempt.Model, attempt.Stop)).Should().Equal(("terra", "model-unavailable"), ("astra", string.Empty));
        ended.Attempts[0].Reason.Should().Contain("brand_new_tool").And.Contain("outside the known set");
        rig.Launches(job).Select(ModelOf).Should().Equal(["gpt-6.2-astra"], "FakeCli counts zero launches of the refused model");
        rig.Rig.LedgerLines().Should().OnlyContain(line => line.GetProperty("model").GetString() == "gpt-6.2-astra", "no line for the entry that was never launched");
        var notice = rig.World.Notices.Single(item => item.Code == "coder-tool-unknown");
        (notice.Title + notice.Detail).Should().Contain("terra").And.Contain("gpt-6.1-terra").And.Contain("0.171.0").And.Contain("brand_new_tool").And.Contain("outside the known set");
    }

    [Fact]
    public async Task V33_WhenEveryCodexEntryIsRefusedForItsTool_EachHasItsNotice_AndTheJobEndsFailedWithModelUnavailable_WithoutWaiting()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig.Steering(("FAKECLI_CODEX_TOOLS", "new-tool"));
        var job = rig.NewJob(route: Two);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "model-unavailable"));
        ended.Attempts.Select(attempt => (attempt.Model, attempt.Stop)).Should().Equal(("terra", "model-unavailable"), ("astra", "model-unavailable"));
        rig.World.Notices.Where(item => item.Code == "coder-tool-unknown").Should().HaveCount(2, "one notice for each entry that is refused for its tool list");
        rig.Launches(job).Should().BeEmpty();
    }

    [Fact]
    public async Task V36_AnInventoryNotReadWhole_InBothReads_IsModelUnavailable_WithANoticeThatShowsNoName()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig.Steering(("FAKECLI_CODEX_TOOLS", "other-form"));
        var job = rig.NewJob(route: ["codex/terra"]);

        var (_, ended) = await rig.RunAsync(job);

        ended.Attempts.Should().ContainSingle().Which.Stop.Should().Be("model-unavailable");
        var notice = rig.World.Notices.Single(item => item.Code == "coder-tool-unknown");
        (notice.Title + notice.Detail).Should().Contain("could not be read whole").And.NotContain("brand_new_tool").And.NotContain("apply_patch");
        rig.Launches(job).Should().BeEmpty();
    }

    [Fact]
    public async Task V33_ANewToolOnAFixTurn_EndsTheJobCoderBoundary_AndTheNextEntryIsNeverLaunched()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig.Steering(("FAKECLI_CODEX_TOOLS", "late-tool"));
        // The model map holds the family names too: this test is a guard that holds on the code before the family rule, where the CLI is asked for the name as written.
        rig.SaysRaw("gpt-6.1-terra", Turn(Writes(CoderRig.Red + " a"), Report()), Turn(Writes(CoderRig.Green), Report())).SaysRaw("gpt-6.2-astra", Turn(Writes(CoderRig.Green), Report(), OtherThread, rollout: Rollout(OtherThread)))
            .SaysRaw("terra", Turn(Writes(CoderRig.Red + " a"), Report()), Turn(Writes(CoderRig.Green), Report())).SaysRaw("astra", Turn(Writes(CoderRig.Green), Report(), OtherThread, rollout: Rollout(OtherThread)));
        var job = rig.NewJob(route: Two);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "coder-boundary"), ended.Reason);
        rig.Launches(job).Should().ContainSingle("the fix turn was refused before it was launched, and the next entry never started: after the first launch every boundary stop is terminal");
        ended.Attempts.Should().ContainSingle();
    }

    [Fact]
    public async Task V33_ANewToolOnTheRepairTurn_EndsTheJobCoderBoundary_AndTheNextEntryIsNeverLaunched()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig.Steering(("FAKECLI_CODEX_TOOLS", "late-tool"));
        rig.SaysRaw("gpt-6.1-terra", Turn(Writes(CoderRig.Green), "this is not a report at all"), Turn(null, Report())).SaysRaw("gpt-6.2-astra", Turn(Writes(CoderRig.Green), Report(), OtherThread, rollout: Rollout(OtherThread)));
        var job = rig.NewJob(route: Two);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "coder-boundary"), ended.Reason);
        rig.Launches(job).Should().ContainSingle();
    }

    [Fact]
    public async Task V36_AReportWithASecondToolsItem_InBothReads_IsNotWhole_AndTheEntryIsModelUnavailable_WithZeroLaunches()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig.Steering(("FAKECLI_CODEX_TOOLS", "second-item"));
        var job = rig.NewJob(route: ["codex/terra"]);

        var (_, ended) = await rig.RunAsync(job);

        ended.Attempts.Should().ContainSingle().Which.Stop.Should().Be("model-unavailable");
        var notice = rig.World.Notices.Single(item => item.Code == "coder-tool-unknown");
        (notice.Title + notice.Detail).Should().Contain("could not be read whole");
        rig.Launches(job).Should().BeEmpty();
    }

    [Fact]
    public async Task V34_ANameThatOnlyTheWorktreeReadHolds_IsInNoSentence_NoEnvelope_NoNotice_NoRowRecord_AndNoLogLine_OnlyACount()
    {
        const string Name = "worktree_only_tool";
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig.Steering(("FAKECLI_CODEX_TOOLS", "worktree-only"));
        var job = rig.NewJob(route: Two);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "coder-boundary"), "the two reads differ: the boundary, never a failover");
        ended.Attempts.Should().ContainSingle();
        var folder = rig.World.Store.FolderOf(job.JobId)!.Location;
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(File.ReadAllText);
        var everything = string.Join('\n', [ended.Reason, JsonSerializer.Serialize(ended.Attempts), rig.World.LogText, .. files, .. rig.World.Notices.Select(item => JsonSerializer.Serialize(new[] { item.Title, item.Detail, item.Cure }))]);
        everything.Should().NotContain(Name, "a name that one read only holds can be text of the repository: it is counted and never shown");
        rig.World.Notices.Single(item => item.Code == "coder-tool-unknown").Detail.Should().Contain("none is shown");
    }

    // ---------- A4-M3: the run that reads the list of models ----------

    [Fact]
    public async Task V30_TheListRead_HasTheArgvDebugModelsOnly_AnEmptyFolderMadeByCoai_AndNoRepositoryFolderAboveIt()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig;
        rig.SaysRaw("gpt-6.1-terra", Turn(Writes(CoderRig.Green), Report()));
        var job = rig.NewJob(route: ["codex/terra"]);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var call = ModelCalls(rig.LaunchFolder(job)).Should().ContainSingle().Subject;
        call[3].Should().Be("debug models", "the argv is debug models and nothing else");
        call[4].Should().BeEmpty("the folder is empty");
        for (var above = Path.GetDirectoryName(call[0]); !string.IsNullOrEmpty(above); above = Path.GetDirectoryName(above))
        {
            Directory.Exists(Path.Combine(above, ".git")).Should().BeFalse($"no folder above the working folder is a folder of a repository ({above})");
            File.Exists(Path.Combine(above, ".git")).Should().BeFalse(above);
        }

        call[0].Should().NotContain(Path.GetFileName(rig.WorktreeOf(job)), "never a folder inside the worktree, the main checkout or a mount");
    }

    [Fact]
    public async Task V32_AnEnvironmentsFileInTheJobsHome_IsCoderBoundary_BeforeTheListIsRead()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig;
        var job = rig.NewJob(route: Two);
        var home = Path.Combine(rig.JobFolder(job), "coder", "codex", "home", ".codex");
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "environments.toml"), "[environments]\n");

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "coder-boundary"), ended.Reason);
        ended.Reason.Should().Contain("environments.toml");
        ended.Attempts.Should().ContainSingle("a boundary stop: the next entry is not tried");
        rig.Launches(job).Should().BeEmpty();
        File.Exists(Path.Combine(rig.LaunchFolder(job), "sidecars")).Should().BeFalse();
        Directory.Exists(Path.Combine(rig.LaunchFolder(job), "sidecars")).Should().BeFalse("the list was never read: the CLI was not started");
    }

    public static TheoryData<string> StreamBoundaries => ["web_search", "collab_tool_call", "an-item-type-nobody-measured"];

    [Theory]
    [MemberData(nameof(StreamBoundaries))]
    public async Task V32_AnItemOfTheStreamThatNobodyMeasured_AfterTheFirstLaunch_EndsTheJobCoderBoundary_AndTheNextEntryIsNeverLaunched(string type)
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig;
        // The model map holds the family names too: a guard that also holds on the code before the family rule.
        rig.SaysRaw("gpt-6.1-terra", Turn(Writes(CoderRig.Green), Report(), items: [Message(), OfType(type)])).SaysRaw("gpt-6.2-astra", Turn(Writes(CoderRig.Green), Report(), OtherThread, rollout: Rollout(OtherThread)))
            .SaysRaw("terra", Turn(Writes(CoderRig.Green), Report(), items: [Message(), OfType(type)])).SaysRaw("astra", Turn(Writes(CoderRig.Green), Report(), OtherThread, rollout: Rollout(OtherThread)));
        var job = rig.NewJob(route: Two);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "coder-boundary"), ended.Reason);
        ended.Attempts.Should().ContainSingle("a turn that was run and showed something outside the measure ends the job, never the entry");
        rig.Launches(job).Should().ContainSingle("zero launches of the next entry");
    }

    [Fact]
    public async Task V32_TheRolloutCheck_AfterTheFirstLaunch_EndsTheJobCoderBoundary_AndTheNextEntryIsNeverLaunched()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig;
        var spawn = Rollout(ThreadId, "{\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"name\":\"collaboration.spawn_agent\"}}");
        rig.SaysRaw("gpt-6.1-terra", Turn(Writes(CoderRig.Green), Report(), rollout: spawn)).SaysRaw("gpt-6.2-astra", Turn(Writes(CoderRig.Green), Report(), OtherThread, rollout: Rollout(OtherThread)))
            .SaysRaw("terra", Turn(Writes(CoderRig.Green), Report(), rollout: spawn)).SaysRaw("astra", Turn(Writes(CoderRig.Green), Report(), OtherThread, rollout: Rollout(OtherThread)));
        var job = rig.NewJob(route: Two);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "coder-boundary"), ended.Reason);
        rig.Launches(job).Should().ContainSingle();
    }

    [Fact]
    public async Task V34_AHostileToolName_IsInNoSentence_NoEnvelope_NoNotice_AndNoLogLine()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig.Steering(("FAKECLI_CODEX_TOOLS", "hostile-names"));
        var job = rig.NewJob(route: ["codex/terra"]);

        var (_, ended) = await rig.RunAsync(job);

        ended.Stop.Should().Be("model-unavailable");
        var everything = ended.Reason + JsonSerializer.Serialize(ended.Attempts) + rig.World.LogText + rig.World.Store.FolderOf(job.JobId)!.ReadResult() + string.Join('\n', rig.World.Notices.Select(item => JsonSerializer.Serialize(new[] { item.Title, item.Detail, item.Cure })));
        foreach (var hostile in new[] { "bad\nname", "control\u0001char", "sk-ant-oat01", "abcdefghij/klmnop", "abcdefghijklmnopqrstuvwxyz0123456789", new string('a', 300) })
        {
            everything.Should().NotContain(hostile);
        }

        rig.World.Notices.Single(item => item.Code == "coder-tool-unknown").Detail.Should().Contain("a name that cannot be shown (300 characters)");
    }
}

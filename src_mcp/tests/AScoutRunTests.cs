using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Notices;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s2 t1 — the scout run (spec A, scenarios S1 to S5, S11, S12). The real runner, launcher, classifier, tree check and evidence check; FakeCli says the scout's answer for each model, so no model is called and
/// nothing is paid: valid at turn 0 with the read-only argv (S1); ONE repair turn in the SAME session (S2); wrong three times ends <c>output-invalid</c> with no result (S3); evidence that is not true is repaired, then kept with
/// <c>verified: false</c> and counted (S4); a path that may not be opened is an error and the file is never opened (S5); a blank evidence text is an error before any line is read (S12); an empty brief is refused before any folder (S11).
/// </summary>
[Collection("fakecli-env")]
public sealed class AScoutRunTests
{
    private static readonly string[] Route = ["claude/sonnet", "claude/haiku"];

    private static async Task<RoleFailoverRig> RigAsync()
    {
        var rig = await RoleFailoverRig.CreateAsync();
        ReadOnlyScenario.Lay(rig.Repo);
        await rig.Git.CommitAsync("the files");

        return rig;
    }

    private static object Says(JsonObject answer) => FakeRoleAnswers.ClaudeSays(answer.ToJsonString());

    private static Task<(int Exit, RoleJobRecord Job)> RunAsync(RoleFailoverRig rig, IReadOnlyList<string>? route = null, string taskFile = "") =>
        rig.RunAsync(rig.NewBriefJob(RoleDescriptors.Scout, route ?? Route, ReadOnlyScenario.Brief, taskFile), [ReadOnlyScenario.ScoutDescriptor()]);

    private static JsonElement Result(RoleFailoverRig rig, RoleJobRecord job) => JsonDocument.Parse(rig.World.Store.FolderOf(job.JobId)!.ReadResult()).RootElement.Clone();

    private static string Resumed(string[] argv) => argv.SkipWhile(arg => arg != "--resume").Skip(1).FirstOrDefault() ?? string.Empty;

    private static string LastPrompt(RoleFailoverRig rig, int launch) => rig.Launches()[launch][^1];

    private static string AttemptFolder(RoleFailoverRig rig, RoleJobRecord job) => Path.Combine(rig.World.Store.FolderOf(job.JobId)!.Location, "attempts", "1");

    [Fact]
    public void TheScoutRole_IsRegistered_RoutedAsScout_AsksForABrief_AndHasItsOwnLookAtTheRequest()
    {
        var scout = RoleDescriptors.Find(RoleDescriptors.Shipped, "scout");

        scout.Should().NotBeNull();
        scout!.RouteAs.Should().Be("scout");
        scout.NeedsBrief.Should().BeTrue();
        scout.Check.Should().NotBeNull();
        RoleDescriptors.Shipped.Select(descriptor => descriptor.Role).Should().Contain(["probe", "ba", "coder", "scout"]);
    }

    // ---------- S1: valid at turn 0, the read-only argv ----------

    [Fact]
    public async Task S1_ValidAtTurnZero_Succeeds_WithTheReadOnlyArgv_AndTheEnvelopeCoaiBuilt()
    {
        await using var rig = await RigAsync();
        rig.Says("sonnet", Says(ReadOnlyScenario.Scout()));
        var before = BaScenario.Tree(rig.Repo);

        var (exit, job) = await RunAsync(rig, taskFile: ReadOnlyScenario.Router);

        exit.Should().Be(RoleRunner.Done);
        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        job.RepairTurns.Should().Be(0);
        rig.Models().Should().Equal("sonnet");
        var argv = rig.Launches().Should().ContainSingle().Subject;
        argv.Should().Contain("--permission-mode").And.Contain("plan");
        argv[Array.IndexOf(argv, "--tools") + 1].Should().Be("Read,Glob,Grep", "the scout reads and searches: no shell, no write tool");
        argv.Should().NotContain("acceptEdits").And.NotContain("--restricted", "the scout is never the coder's write launch");
        LastPrompt(rig, 0).Should().Contain(ReadOnlyScenario.Brief).And.Contain($"`{ReadOnlyScenario.Router}`");
        BaScenario.Tree(rig.Repo).Should().BeEquivalentTo(before, "the scout writes nothing");
        var head = (await new CoaiMcp.Runners.Collecting.GitHistory(new CoaiMcp.Runners.Processes.ProcessLauncher()).RootAndHeadAsync(rig.Repo, TestContext.Current.CancellationToken)).Lines[1];
        var result = Result(rig, job);
        result.GetProperty("status").GetString().Should().Be("ok");
        result.GetProperty("commit").GetString().Should().Be(head, "the HEAD of the checkout when the job began");
        result.GetProperty("repairTurns").GetInt32().Should().Be(0);
        result.GetProperty("facts").GetArrayLength().Should().Be(2);
        var first = result.GetProperty("facts")[0].GetProperty("evidence")[0];
        first.GetProperty("verified").GetBoolean().Should().BeTrue();
        first.GetProperty("why").GetString().Should().Contain("holds the text");
        result.GetProperty("files").EnumerateArray().Select(file => (file.GetProperty("path").GetString(), file.GetProperty("exists").GetBoolean())).Should()
            .Equal((ReadOnlyScenario.Router, true), ("README.md", true), ("src/Missing.cs", false));
        var counts = result.GetProperty("counts");
        (counts.GetProperty("facts").GetInt32(), counts.GetProperty("evidence").GetInt32(), counts.GetProperty("verified").GetInt32(), counts.GetProperty("filesMissing").GetInt32()).Should().Be((2, 3, 3, 1));
        result.GetProperty("unknown")[0].GetString().Should().Contain("bands");
    }

    [Fact]
    public async Task S1_AnAnswerInACodeFence_IsAccepted()
    {
        await using var rig = await RigAsync();
        rig.Says("sonnet", FakeRoleAnswers.ClaudeSays("```json\n" + ReadOnlyScenario.Scout().ToJsonString() + "\n```"));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        job.RepairTurns.Should().Be(0);
    }

    [Fact]
    public async Task S1_AModelsOwnVerifiedAndExistsAreNeverRead_AndTheEnvelopeIsNotTheAnswerAsSent()
    {
        await using var rig = await RigAsync();
        var sent = ReadOnlyScenario.Scout();
        sent["facts"]![0]!["evidence"]![0]!["verified"] = true;
        rig.Says("sonnet", Says(sent), Says(ReadOnlyScenario.Scout()));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        job.RepairTurns.Should().Be(1, "a model's own verified is an unknown key and an error of the answer");
        LastPrompt(rig, 1).Should().Contain("facts[0].evidence[0].verified").And.Contain("unknown key");
    }

    // ---------- S2: one repair turn, same session ----------

    [Fact]
    public async Task S2_AShapeErrorOnce_IsOneRepairTurn_InTheSameSession_OfTheSameModel()
    {
        await using var rig = await RigAsync();
        var wrong = ReadOnlyScenario.Scout();
        wrong["facts"]![0]!["claim"] = new string('c', 401);
        wrong["extra"] = 1;
        rig.Says("sonnet", Says(wrong), Says(ReadOnlyScenario.Scout()));
        rig.Says("haiku", Says(ReadOnlyScenario.Scout()));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        job.RepairTurns.Should().Be(1);
        job.Attempts.Should().ContainSingle("two turns are ONE attempt").Which.Model.Should().Be("sonnet");
        rig.Models().Should().Equal("sonnet", "sonnet");
        Resumed(rig.Launches()[0]).Should().BeEmpty("turn 0 is a fresh session");
        Resumed(rig.Launches()[1]).Should().Be("00000000-0000-0000-0000-000000000000", "the repair turn resumes the session the first turn reported");
        LastPrompt(rig, 1).Should().Contain("facts[0].claim").And.Contain("401 characters, at most 400").And.Contain("extra: unknown key").And.Contain("WHOLE JSON object");
        rig.LedgerLines().Should().HaveCount(2, "every launch is one ledger line");
        Result(rig, job).GetProperty("repairTurns").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task S2_ANonJsonAnswer_IsRepaired_Too()
    {
        await using var rig = await RigAsync();
        rig.Says("sonnet", FakeRoleAnswers.ClaudeSays("I looked and found the router in src/Router.cs."), Says(ReadOnlyScenario.Scout()));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        job.RepairTurns.Should().Be(1);
        LastPrompt(rig, 1).Should().Contain("not ONE JSON object");
    }

    // ---------- S3: wrong three times ----------

    [Fact]
    public async Task S3_WrongThreeTimes_EndsOutputInvalid_WithNoResult_NoFailover_AndEveryAnswerKept()
    {
        await using var rig = await RigAsync();
        var wrong = ReadOnlyScenario.Scout();
        wrong["facts"]![0]!["evidence"] = new JsonArray();
        rig.Says("sonnet", Says(wrong));
        rig.Says("haiku", Says(ReadOnlyScenario.Scout()));

        var (exit, job) = await RunAsync(rig);

        exit.Should().Be(RoleRunner.Done);
        job.State.Should().Be(RoleJobStates.Failed);
        job.Stop.Should().Be("output-invalid");
        job.Reason.Should().Contain("still wrong after 2 repair turns");
        job.RepairTurns.Should().Be(2);
        rig.Models().Should().Equal("sonnet", "sonnet", "sonnet");
        rig.World.Store.FolderOf(job.JobId)!.ReadResult().Should().BeEmpty("no result");
        Directory.GetFiles(AttemptFolder(rig, job)).Select(Path.GetFileName).Should().Contain(["turn-0.txt", "turn-1.txt", "turn-2.txt", "evidence.txt"], "the output of every turn is kept in the job folder");
    }

    // ---------- S4: evidence that is not true ----------

    public static TheoryData<string> BadItems =>
    [
        "line-out-of-range", "text-not-on-the-line", "file-not-there",
    ];

    private static JsonObject BadItem(string kind) => kind switch
    {
        "line-out-of-range" => ReadOnlyScenario.Item(ReadOnlyScenario.Router, 99, "namespace Demo;"),
        "text-not-on-the-line" => ReadOnlyScenario.Item(ReadOnlyScenario.Router, 1, "namespace Other;"),
        _ => ReadOnlyScenario.Item("src/Gone.cs", 1, "namespace Demo;"),
    };

    [Theory]
    [MemberData(nameof(BadItems))]
    public async Task S4_AnItemThatIsNotTrue_GoesBackOnce_AndIsVerifiedAfterTheRepair(string kind)
    {
        await using var rig = await RigAsync();
        rig.Says("sonnet", Says(ReadOnlyScenario.ScoutWith(BadItem(kind))), Says(ReadOnlyScenario.Scout()));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        job.RepairTurns.Should().Be(1);
        LastPrompt(rig, 1).Should().Contain("facts[0].evidence[0]: not verified");
        var result = Result(rig, job);
        result.GetProperty("counts").GetProperty("verified").GetInt32().Should().Be(3);
        result.GetProperty("repairTurns").GetInt32().Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BadItems))]
    public async Task S4_AnItemThatStaysNotTrue_AfterTheLastTurn_IsInTheEnvelopeAsNotVerified_NeverDropped_AndCounted(string kind)
    {
        await using var rig = await RigAsync();
        rig.Says("sonnet", Says(ReadOnlyScenario.ScoutWith(BadItem(kind), ReadOnlyScenario.Item(ReadOnlyScenario.Router, 2, ReadOnlyScenario.RouterLine2))));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        job.RepairTurns.Should().Be(2, "the item went back twice");
        rig.Models().Should().Equal("sonnet", "sonnet", "sonnet");
        var result = Result(rig, job);
        var items = result.GetProperty("facts")[0].GetProperty("evidence").EnumerateArray().ToList();
        items.Should().HaveCount(2, "an item that is not verified is never dropped");
        items[0].GetProperty("verified").GetBoolean().Should().BeFalse("and never shown as verified");
        items[0].GetProperty("why").GetString().Should().NotBeEmpty();
        items[1].GetProperty("verified").GetBoolean().Should().BeTrue();
        var counts = result.GetProperty("counts");
        (counts.GetProperty("evidence").GetInt32(), counts.GetProperty("verified").GetInt32()).Should().Be((3, 2));
        result.GetProperty("repairTurns").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task S4_AShapeErrorOnTheLastTurn_StillEndsOutputInvalid_EvenWithTrueEvidence()
    {
        await using var rig = await RigAsync();
        var shape = ReadOnlyScenario.Scout();
        shape["unknown"] = "nothing";
        rig.Says("sonnet", Says(ReadOnlyScenario.ScoutWith(BadItem("line-out-of-range"))), Says(ReadOnlyScenario.ScoutWith(BadItem("line-out-of-range"))), Says(shape));

        var (_, job) = await RunAsync(rig);

        job.Stop.Should().Be("output-invalid");
        rig.World.Store.FolderOf(job.JobId)!.ReadResult().Should().BeEmpty();
    }

    // ---------- S5: a path that may not be opened ----------

    public static TheoryData<string, string> ForbiddenPaths => new()
    {
        { "../outside.txt", "leaves the repository" },
        { "/etc/passwd", "inside the repository" },
        { "C:/Windows/win.ini", "inside the repository" },
        { ".env", "credential file" },
        { ".git/config", "leaves the repository" },
    };

    [Theory]
    [MemberData(nameof(ForbiddenPaths))]
    public async Task S5_APathThatMayNotBeOpened_IsAnErrorOfTheAnswer_AndIsRepaired(string path, string word)
    {
        await using var rig = await RigAsync();
        rig.Says("sonnet", Says(ReadOnlyScenario.ScoutWith(ReadOnlyScenario.Item(path, 1, "KEY=x"))), Says(ReadOnlyScenario.Scout()));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        job.RepairTurns.Should().Be(1);
        LastPrompt(rig, 1).Should().Contain("facts[0].evidence[0].file:").And.Contain(word);
    }

    [Fact]
    public async Task S5_ACredentialFile_IsNeverOpened_ItsContentIsInNoFileOfTheJob_WhateverTheModelAnswers()
    {
        await using var rig = await RigAsync();
        rig.Says("sonnet", Says(ReadOnlyScenario.ScoutWith(ReadOnlyScenario.Item(".env", 1, $"KEY={ReadOnlyScenario.Secret}"))));

        var (_, job) = await RunAsync(rig);

        job.Stop.Should().Be("output-invalid");
        foreach (var file in Directory.EnumerateFiles(rig.World.Store.FolderOf(job.JobId)!.Location, "*", SearchOption.AllDirectories).Where(file => !file.Contains("attempts", StringComparison.Ordinal)))
        {
            File.ReadAllText(file).Should().NotContain(ReadOnlyScenario.Secret, $"{Path.GetFileName(file)}: the file was never opened, so nothing of it can be in the job folder");
        }
    }

    [Fact]
    public async Task S5_APathThroughALink_IsAnError_AndTheLinkedFileIsNeverOpened()
    {
        await using var rig = await RigAsync();
        using var outside = TempDir.For("coai-scout-outside-");
        File.WriteAllText(outside.At("secret.txt"), "OUTSIDE\n");
        File.CreateSymbolicLink(Path.Combine(rig.Repo, "linked.txt"), outside.At("secret.txt"));
        var before = BaScenario.Tree(rig.Repo);
        rig.Says("sonnet", Says(ReadOnlyScenario.ScoutWith(ReadOnlyScenario.Item("linked.txt", 1, "OUTSIDE"))), Says(ReadOnlyScenario.Scout()));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        LastPrompt(rig, 1).Should().Contain("facts[0].evidence[0].file:").And.Contain("runs through a link");
        BaScenario.Tree(rig.Repo).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task S5_AListedFileThatLeavesTheRepository_IsAnError_Too()
    {
        await using var rig = await RigAsync();
        var answer = ReadOnlyScenario.Scout();
        answer["files"] = new JsonArray(ReadOnlyScenario.Router, "../secrets.txt");
        rig.Says("sonnet", Says(answer), Says(ReadOnlyScenario.Scout()));

        var (_, job) = await RunAsync(rig);

        job.RepairTurns.Should().Be(1);
        LastPrompt(rig, 1).Should().Contain("files[1]:").And.Contain("leaves the repository");
    }

    // ---------- S12: a blank evidence text ----------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\\t")]
    [InlineData("\\u00A0\\u2003")]
    public async Task S12_ABlankEvidenceText_IsAnErrorBeforeAnyLineIsRead_ForALineThatIsBlankToo(string escaped)
    {
        await using var rig = await RigAsync();
        BaScenario.Write(rig.Repo, "docs/blank.txt", "first\n\n   \nfourth\n");
        await rig.Git.CommitAsync("a file with blank lines");
        var blank = JsonSerializer.Deserialize<string>($"\"{escaped}\"")!;
        rig.Says("sonnet", Says(ReadOnlyScenario.ScoutWith(ReadOnlyScenario.Item("docs/blank.txt", 2, blank))), Says(ReadOnlyScenario.ScoutWith(ReadOnlyScenario.Item("docs/blank.txt", 3, blank))), Says(ReadOnlyScenario.Scout()));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        job.RepairTurns.Should().Be(2, "a blank text is never verified, not against a blank line and not against a line of spaces");
        LastPrompt(rig, 1).Should().Contain("facts[0].evidence[0].text:").And.Contain("never empty or only white space");
        LastPrompt(rig, 2).Should().Contain("facts[0].evidence[0].text:");
        Result(rig, job).GetProperty("counts").GetProperty("verified").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task S12_ABlankTextThatIsNeverFixed_NeverEndsAsVerified()
    {
        await using var rig = await RigAsync();
        rig.Says("sonnet", Says(ReadOnlyScenario.ScoutWith(ReadOnlyScenario.Item(ReadOnlyScenario.Router, 99, " "))));

        var (_, job) = await RunAsync(rig);

        job.Stop.Should().Be("output-invalid");
        rig.World.Store.FolderOf(job.JobId)!.ReadResult().Should().BeEmpty("a blank text is an error, so it never reaches the envelope — not as verified, and not as unverified");
    }

    // ---------- every stored text passes the redaction rule ----------

    [Fact]
    public async Task EveryStoredText_PassesTheRedactionRule_AndVerifiedIsDecidedBeforeIt()
    {
        const string line = "Authorization: Bearer abcdefghijklmnop0123456789ABCDEF";
        Redaction.SafeText(line, Redaction.DetailLimit).Should().NotContain("abcdefghijklmnop0123456789ABCDEF", "positive control: the rule does redact this line");
        await using var rig = await RigAsync();
        BaScenario.Write(rig.Repo, "docs/api.txt", line + "\n");
        await rig.Git.CommitAsync("a file with a header");
        var answer = ReadOnlyScenario.Scout();
        answer["facts"]![0]!["claim"] = line;
        answer["facts"]![0]!["evidence"] = new JsonArray(ReadOnlyScenario.Item("docs/api.txt", 1, line));
        answer["unknown"] = new JsonArray(line);
        rig.Says("sonnet", Says(answer));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        var text = rig.World.Store.FolderOf(job.JobId)!.ReadResult();
        text.Should().NotContain("abcdefghijklmnop0123456789ABCDEF");
        Result(rig, job).GetProperty("facts")[0].GetProperty("evidence")[0].GetProperty("verified").GetBoolean().Should().BeTrue("the comparison was made on the model's text, before redaction");
    }

    // ---------- S11: the request ----------

    private sealed class NothingStarted : CoaiMcp.Runners.Processes.IProcessLauncher
    {
        public Task<CoaiMcp.Runners.Processes.ProcessResult> RunAsync(CoaiMcp.Runners.Processes.ProcessRequest request, CancellationToken ct = default) =>
            Task.FromResult(new CoaiMcp.Runners.Processes.ProcessResult(0, string.Empty, string.Empty, TimedOut: false));
    }

    private static RoleJobService ServiceFor(RoleJobWorld world, Dictionary<string, string?>? settings = null)
    {
        var values = settings ?? [];
        values.TryAdd("COAI_ROLE_LAUNCHER", "setsid");
        // Role jobs do not use the person's own sign-in unless the device says so (epoch 3 E2 s7, the key pool): these starts name claude models with no pool, so the switch is set, as in RoleFailoverRig.
        values.TryAdd("COAI_ROLE_CLAUDE_PERSON_SIGN_IN", "1");

        return new RoleJobService(world.DataDir, name => values.GetValueOrDefault(name), world.Log, notice => { world.Notices.Enqueue(notice); return true; })
        {
            Clock = world.Clock,
            Launcher = new CoaiMcp.Runners.Detach.DetachedLauncher(new NothingStarted(), NoRunnerStarted.Succeeds),
        };
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t")]
    public async Task S11_AnEmptyBrief_IsRefusedBeforeAnyFolder_EvenWithATaskFile(string? brief)
    {
        using var world = new RoleJobWorld();
        using var repo = TempDir.For("coai-scout-repo-");
        File.WriteAllText(repo.At("a.txt"), "a\n");

        foreach (var taskFile in new[] { null, "a.txt" })
        {
            var answer = JsonDocument.Parse(await ServiceFor(world).StartAsync("scout", repo, taskFile, brief, null, null, null, TestContext.Current.CancellationToken)).RootElement;

            answer.GetProperty("error").GetString().Should().Contain("scout answers a question").And.Contain("brief");
        }

        world.Store.JobIds().Should().BeEmpty();
        Directory.Exists(world.Store.JobsRoot).Should().BeFalse("no folder, no runner");
    }

    [Fact]
    public async Task S11_ABriefOverTheCap_IsRefused_AndABriefAtTheCapIsNot()
    {
        using var world = new RoleJobWorld();
        using var repo = TempDir.For("coai-scout-repo-");

        var over = JsonDocument.Parse(await ServiceFor(world).StartAsync("scout", repo, null, new string('q', RoleRequestChecks.MaxBriefLength + 1), null, null, null, TestContext.Current.CancellationToken)).RootElement;
        var at = JsonDocument.Parse(await ServiceFor(world).StartAsync("scout", repo, null, new string('q', RoleRequestChecks.MaxBriefLength), null, null, null, TestContext.Current.CancellationToken)).RootElement;

        over.GetProperty("error").GetString().Should().Contain("65536");
        at.TryGetProperty("jobId", out _).Should().BeTrue(at.ToString());
    }

    [Theory]
    [InlineData("missing.txt", "it is not there")]
    [InlineData("folder", "it is not a file")]
    [InlineData(".env", "credential file")]
    [InlineData(".git/config", "leaves the repository")]
    public async Task S11_ATaskFileThatMayNotBeRead_IsRefusedBeforeAnyFolder(string taskFile, string word)
    {
        using var world = new RoleJobWorld();
        using var repo = TempDir.For("coai-scout-repo-");
        Directory.CreateDirectory(repo.At("folder"));
        File.WriteAllText(repo.At(".env"), "KEY=1\n");

        var answer = JsonDocument.Parse(await ServiceFor(world).StartAsync("scout", repo, taskFile, "what is here?", null, null, null, TestContext.Current.CancellationToken)).RootElement;

        answer.GetProperty("error").GetString().Should().Contain("taskFile").And.Contain(word);
        world.Store.JobIds().Should().BeEmpty();
    }

    [Fact]
    public async Task S11_ABriefWithATaskFile_IsStarted_AndItsRouteIsTheScoutListOfTheRolesTable()
    {
        using var world = new RoleJobWorld();
        using var repo = TempDir.For("coai-scout-repo-");
        File.WriteAllText(repo.At("a.txt"), "a\n");

        var answer = JsonDocument.Parse(await ServiceFor(world).StartAsync("scout", repo, "a.txt", "what is here?", null, null, null, TestContext.Current.CancellationToken)).RootElement;

        answer.GetProperty("route").EnumerateArray().Select(entry => entry.GetString()).Should().Equal("claude/sonnet");
        var request = world.Store.ReadRequest(answer.GetProperty("jobId").GetString()!)!;
        (request.Role, request.Brief, request.TaskFile).Should().Be(("scout", "what is here?", "a.txt"));
    }

    [Fact]
    public async Task S11_ThePersonsOwnScoutList_IsTheRoute()
    {
        using var world = new RoleJobWorld();
        using var repo = TempDir.For("coai-scout-repo-");
        var service = ServiceFor(world, new Dictionary<string, string?> { ["COAI_ROLES_MODELS"] = """{"scout":[{"model":"haiku","vendor":"claude"},{"model":"gpt-5.5","vendor":"codex"}]}""" });

        var answer = JsonDocument.Parse(await service.StartAsync("scout", repo, null, "what is here?", null, null, null, TestContext.Current.CancellationToken)).RootElement;

        answer.GetProperty("route").EnumerateArray().Select(entry => entry.GetString()).Should().Equal("claude/haiku", "codex/gpt-5.5");
    }
}

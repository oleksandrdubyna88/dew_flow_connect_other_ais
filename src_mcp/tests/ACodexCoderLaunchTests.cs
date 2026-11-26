using System.Text.Json;
using CoaiMcp.Core.Coder;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Runners.Worktrees;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s5 t2 (V1, V2, CX-C1, CX-C2, U2 to U8, CX-R2): the codex coder's launch — the argv fresh and resumed with the SAME flags (judgement section 13.4, including <c>-c agents.enabled=false</c> on every turn), the job's <c>config.toml</c> written and read back before every launch, the refusal of an
/// <c>environments.toml</c>, the flat output schema held to coai's reader (and the one null the reader accepts), the unit that hides the main checkout and the coder root, and the environment of exactly the allow-list, <c>CODEX_HOME</c> and the refresh override.
/// </summary>
public sealed class ACodexCoderLaunchTests
{
    private static readonly CodexCoderVendor Vendor = CodexCoderVendor.Instance;

    private const string Model = "terra";

    private const string Job = "job-a";

    private static readonly string Root = Path.GetFullPath("coder-root");

    private static LaunchConfinement.Coder Coder(CoderHandle? resume = null) =>
        new(Job, Path.Combine(Root, CoderWorktree.FolderOf(Job))) { Resume = resume ?? CoderHandle.None, Vendor = "codex" };

    private static ConsultantLaunch Launch(LaunchConfinement.Coder coder) =>
        new(Path.GetFullPath("main-checkout"), "make the change", string.Empty, Path.GetFullPath("answers"), new ReviewerSettings("codex") { Model = Model })
        {
            Confinement = coder,
        };

    private static BoundaryJob TheJob(string root) =>
        new("job-x", new CoderTree(Path.Combine(root, "wt", "coai-coder-job-x"), Path.Combine(root, "main"), string.Empty), Path.Combine(root, "jobs", "job-x"), NetworkChoice.None("t"), NetworkChoice.None("t"), 100);

    // ---------- d1: the argv ----------

    [Fact]
    public void TheFreshArgv_IsTheSetOfJudgementSection13_4_WithTheAgentSwitch()
    {
        var argv = Vendor.FreshArgv(Model, Root);

        argv.Should().Equal(
            ["exec", "--json", "-m", Model, "-c", "model_reasoning_effort=medium",
                .. CodexCoderVendor.DisabledFeatures.SelectMany(feature => new[] { "--disable", feature }),
                "-c", "approval_policy=\"never\"", "-c", "shell_environment_policy.inherit=\"core\"", "-c", "web_search=\"disabled\"", "-c", "agents.enabled=false", "-"]);
        CodexCoderVendor.DisabledFeatures.Should().HaveCount(19).And.Contain("hooks").And.Contain("multi_agent");
    }

    [Fact]
    public void AResumedTurn_CarriesEveryFlagOfTheFreshArgv_AndTheThreadOfTurn0()
    {
        const string Thread = "019aa111-2222-7333-8444-555566667777";
        var fresh = Vendor.FreshArgv(Model, Root, new CoderLaunchFiles("out.txt"));
        var resumed = Vendor.ResumedArgv(Model, Root, Thread, new CoderLaunchFiles("out.txt"));

        resumed.Take(2).Should().Equal("exec", "resume");
        resumed.Should().Contain(Thread).And.EndWith("-");
        var freshFlags = fresh.Skip(1).Where(argument => argument != "-").ToList();
        var resumedFlags = resumed.Skip(2).Where(argument => argument != "-" && argument != Thread).ToList();
        resumedFlags.Should().Equal(freshFlags, "no flag of the set is dropped on a resumed turn, and none is added");
        resumed.Should().Contain("agents.enabled=false").And.Contain("--json").And.Contain("-o");
    }

    [Fact]
    public void NoNeverFlag_IsOnAnyArgv_AndEveryFlagTheCapabilityAsksFor_IsThere()
    {
        var argvs = new[] { Vendor.FreshArgv(Model, Root, new CoderLaunchFiles("out.txt")), Vendor.ResumedArgv(Model, Root, "019aa111-2222-7333-8444-555566667777", new CoderLaunchFiles("out.txt")) };

        foreach (var argv in argvs)
        {
            foreach (var never in CodexCoderVendor.NeverFlags)
            {
                argv.Should().NotContain(never, $"{never} is a never-flag of SPEC C3");
            }

            argv.Should().NotContain(argument => argument.Contains("danger", StringComparison.Ordinal) || argument.Contains("sandbox_mode", StringComparison.Ordinal));
        }

        foreach (var flag in new[] { "--json", "-m", "-c", "--disable", "-o" })
        {
            argvs[0].Should().Contain(flag);
        }
    }

    [Fact]
    public void TheConsultantBuildsTheCoderLaunch_ThroughTheSeam_InTheWorktree_WithThePromptOnStdin()
    {
        const string Thread = "019aa111-2222-7333-8444-555566667777";
        var consultant = new CodexConsultant(new CodexRuntime());

        var fresh = consultant.Build(Launch(Coder()));
        var coder = Coder();
        var handle = CoderHandle.Bind(coder, Thread);
        var resumed = consultant.Build(Launch(Coder(handle)));

        fresh.Request.WorkingDirectory.Should().Be(coder.Worktree, "the cwd is the worktree");
        fresh.Request.StdIn.Should().Be("make the change");
        fresh.Request.Arguments.Take(4).Should().Equal("exec", "--json", "-m", Model);
        fresh.OutputFile.Should().NotBeEmpty();
        fresh.Request.Arguments.Should().Contain("-o").And.Contain(fresh.OutputFile);
        resumed.Request.Arguments.Take(2).Should().Equal("exec", "resume");
        resumed.Request.Arguments.Should().Contain(Thread);
    }

    // ---------- d2: config.toml, environments.toml, the schema ----------

    [Fact]
    public void TheConfigText_IsTheProfileOfResultsSection2_NoNetworkTable()
    {
        var text = CodexCoderVendor.ConfigText(Model, "/opt/codex/bin");

        text.Should().Be("model = \"terra\"\nmodel_reasoning_effort = \"medium\"\ndefault_permissions = \"coai\"\n\n[permissions.coai.filesystem]\n\":minimal\" = \"read\"\n\"/opt/codex/bin\" = \"read\"\n\":project_roots\" = { \".\" = \"write\" }\n");
        text.Should().NotContain("network");
    }

    [Fact]
    public void BeforeEveryLaunch_TheConfigIsWrittenAndReadBack_AndAFileNamedEnvironmentsRefusesTheLaunch()
    {
        using var dir = TempDir.For("cxl-");
        var job = TheJob(dir.Path);
        var home = CodexCoderVendor.CodexHome(job);
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(job.Tree.Root);
        var host = BoundaryHost.Current() with { RealHome = "/nonexistent/coai-real-home", Path = "/usr/bin:/bin" };
        var request = new ProcessRequest("/bin/true", Vendor.FreshArgv(Model, job.Tree.Root, new CoderLaunchFiles(Path.Combine(dir.Path, "out.txt"))), job.Tree.Root);

        var turn0 = Vendor.PrepareLaunch(host, job, request);

        turn0.Problem.Should().BeEmpty();
        File.ReadAllText(Path.Combine(home, "config.toml")).Should().StartWith("model = \"terra\"").And.Contain("default_permissions = \"coai\"");
        turn0.Request.Arguments.Should().Contain("--output-schema").And.Contain(CodexCoderVendor.SchemaFile(job));
        turn0.Request.Arguments[turn0.Request.Arguments.ToList().IndexOf("-o") + 1].Should().Be(CodexCoderVendor.ReportFile(job), "the unit writes the report into the vendor's own folder");
        turn0.Request.Arguments.Should().EndWith("-");
        File.ReadAllText(CodexCoderVendor.SchemaFile(job)).Should().Contain("\"required\"");

        // A hand-edited config is replaced, and a file named environments.toml refuses the next launch (a fix turn).
        File.WriteAllText(Path.Combine(home, "config.toml"), "default_permissions = \"danger\"\n");
        Vendor.PrepareLaunch(host, job, request).Problem.Should().BeEmpty();
        File.ReadAllText(Path.Combine(home, "config.toml")).Should().NotContain("danger");
        File.WriteAllText(Path.Combine(home, CodexCoderVendor.EnvironmentsFile), "[environment]\n");

        var refused = Vendor.PrepareLaunch(host, job, request);

        refused.Problem.Should().Contain("environments.toml").And.Contain("was not launched");
    }

    [Fact]
    public async Task AnEnvironmentsFile_PlantedBeforeTurn0_RefusesTheLaunch_AndNoUnitIsStarted()
    {
        Assert.SkipUnless(SystemdUser.Works(out var why), why);
        await using var bed = await CoderBed.CreateAsync();
        Directory.CreateDirectory(CodexCoderVendor.CodexHome(bed.Job));
        File.WriteAllText(Path.Combine(CodexCoderVendor.CodexHome(bed.Job), CodexCoderVendor.EnvironmentsFile), "[environment]\n");
        var requests = bed.Launcher.Requests.Count;
        var request = bed.Fake(Vendor.FreshArgv(Model, bed.Worktree).ToArray());

        var outcome = await bed.Turns().RunAsync(bed.Job, request, "c1", Vendor, TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<CoderTurnOutcome.Refused>().Which.Sentence.Should().Contain("environments.toml");
        bed.Launcher.Requests.Skip(requests).Should().NotContain(started => started.Arguments.Any(argument => argument.StartsWith("coai-coder-", StringComparison.Ordinal)), "no unit of the coder was started");
    }

    [Fact]
    public void TheOutputSchema_HasExactlyTheMembersOfCoaisReader_AllRequired()
    {
        using var document = JsonDocument.Parse(CodexSchemaText());
        var root = document.RootElement;
        var required = root.GetProperty("required").EnumerateArray().Select(item => item.GetString()!).ToList();
        var properties = root.GetProperty("properties").EnumerateObject().Select(item => item.Name).ToList();

        required.Should().BeEquivalentTo(CoderReportReader.OkKeys.Concat(CoderReportReader.RefusedKeys).Distinct());
        properties.Should().BeEquivalentTo(required);
        root.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        var dod = root.GetProperty("properties").GetProperty("dod").GetProperty("items");
        dod.GetProperty("required").EnumerateArray().Select(item => item.GetString()).Should().BeEquivalentTo(CoderReportReader.DodKeys);
        dod.GetProperty("properties").EnumerateObject().Select(item => item.Name).Should().BeEquivalentTo(CoderReportReader.DodKeys);
        CodexSchemaText().Should().NotContain("allOf").And.NotContain("\"if\"", "the flat variant has no combinators the API refuses");
    }

    [Fact]
    public void TheEmbeddedSchema_IsTheFileOfTheRepository()
    {
        var repo = File.ReadAllText(Path.Combine(RepoPaths.Root(), "shared", "coder-report.codex.schema.json"));

        CodexSchemaText().Should().Be(repo);
    }

    private static string CodexSchemaText() => typeof(CodexCoderVendor).Assembly.GetManifestResourceStream("CoaiMcp.Runners.coder-report.codex.schema.json") is { } stream ? new StreamReader(stream).ReadToEnd() : string.Empty;

    private static CoderRead Read(string json, params string[] ids)
    {
        using var document = JsonDocument.Parse(json);

        return CoderReportReader.Read(document.RootElement, ids);
    }

    [Fact]
    public void AnOkReportInTheFlatShape_ReasonAndNotesNull_IsReadAsAReport()
    {
        Read("""{"status":"ok","reason":null,"dod":[{"id":"d1","met":true,"evidence":"done"}],"notes":null}""", "d1").Should().BeOfType<CoderRead.Report>();
        Read("""{"status":"ok","reason":null,"dod":[{"id":"d1","met":true,"evidence":"done"}],"notes":"a note"}""", "d1").Should().BeOfType<CoderRead.Report>();
    }

    [Fact]
    public void ARefusedReportInTheFlatShape_DodAndNotesNull_IsReadAsARefusal()
    {
        Read("""{"status":"refused","reason":"cannot","dod":null,"notes":null}""").Should().BeOfType<CoderRead.Refused>().Which.Reason.Should().Be("cannot");
    }

    [Theory]
    [InlineData("""{"status":null,"reason":null,"dod":[{"id":"d1","met":true,"evidence":"x"}],"notes":null}""")]
    [InlineData("""{"status":"ok","reason":null,"dod":null,"notes":null}""")]
    [InlineData("""{"status":"ok","reason":null,"dod":[{"id":"d1","met":null,"evidence":"x"}],"notes":null}""")]
    [InlineData("""{"status":"ok","reason":null,"dod":[{"id":"d1","met":true,"evidence":null}],"notes":null}""")]
    [InlineData("""{"status":"ok","reason":null,"dod":[{"id":null,"met":true,"evidence":"x"}],"notes":null}""")]
    [InlineData("""{"status":"ok","reason":null,"dod":[{"id":"d1","met":true,"evidence":"x"}],"notes":null,"extra":null}""")]
    [InlineData("""{"status":"ok","reason":"a reason an ok report does not carry","dod":[{"id":"d1","met":true,"evidence":"x"}],"notes":null}""")]
    [InlineData("""{"status":"refused","reason":null,"dod":null,"notes":null}""")]
    [InlineData("""{"status":"refused","reason":"r","dod":[{"id":"d1","met":true,"evidence":"x"}],"notes":null}""")]
    public void ANullOfAnyOtherMember_AndAnUnknownMember_StayErrors(string json)
    {
        Read(json, "d1").Should().BeOfType<CoderRead.Wrong>();
    }

    // ---------- d3: the unit ----------

    [Fact]
    public void TheUnit_HidesTheMainCheckoutAndTheCoderRoot_NotReadOnly_AndBindsBackOnlyTheGitFoldersAndTheWorktree()
    {
        using var dir = TempDir.For("cxl-");
        var job = TheJob(dir.Path);
        var host = BoundaryHost.Current() with { RealHome = "/nonexistent/coai-real-home" };
        var program = new CoderProgram("/opt/codex/bin/codex", "/opt/codex/bin", string.Empty);

        var properties = Vendor.UnitProperties(host, job, program, TimeSpan.FromMinutes(5));

        properties.Should().Contain($"TemporaryFileSystem={job.Tree.MainCheckout}");
        properties.Should().Contain($"BindReadOnlyPaths={Path.Combine(job.Tree.MainCheckout, ".git")}");
        properties.Should().Contain($"TemporaryFileSystem={job.CoderRoot}");
        properties.Should().Contain($"BindPaths={job.Tree.Root}").And.Contain($"BindReadOnlyPaths={job.Tree.GitEntry}");
        properties.Should().NotContain($"BindReadOnlyPaths={job.Tree.MainCheckout}", "the main checkout is HIDDEN, not read-only");
        properties.Should().NotContain($"BindReadOnlyPaths={job.CoderRoot}");
        properties.Should().Contain($"TemporaryFileSystem={job.JobCover}:ro").And.Contain($"BindPaths={job.CoderVendorFolder("codex")}");
        properties.Should().Contain($"TemporaryFileSystem={BoundaryUnit.UnitTemp}", "the shape under which codex sandbox starts").And.Contain("BindReadOnlyPaths=/opt/codex/bin");
        properties.Should().NotContain(property => property.StartsWith("RestrictNamespaces", StringComparison.Ordinal), "the vendor sandbox needs namespaces and refuses to run without them");
        properties.Should().NotContain(property => property.StartsWith("ReadWritePaths=", StringComparison.Ordinal), "never ReadWritePaths=/mnt, which brings the host's /mnt/c back");
        properties.Should().Contain("PrivateNetwork=no").And.Contain("NoNewPrivileges=yes");
    }

    [Fact]
    public void TheRealCodexSignInFile_IsHiddenAtItsRealPath_InTheCodexUnit_AndInTheClaudeCodersUnit()
    {
        using var dir = TempDir.For("cxl-");
        var job = TheJob(dir.Path);
        var host = BoundaryHost.Current() with { RealHome = Path.Combine(dir.Path, "real-home") };
        var program = new CoderProgram("/opt/codex/bin/codex", "/opt/codex/bin", string.Empty);
        var realCodex = $"InaccessiblePaths=-{Path.Combine(dir.Path, "real-home", ".codex", "auth.json")}";

        Vendor.UnitProperties(host, job, program, TimeSpan.FromMinutes(5)).Should().Contain(realCodex);
        CoderUnit.Properties(host, job, program, TimeSpan.FromMinutes(5)).Should().Contain(realCodex, "C3 item 2: the real file of codex is in no unit of any job");
        BoundaryUnit.Properties(host, job, CheckNetwork.Host, TimeSpan.FromMinutes(5)).Should().Contain(realCodex);
    }

    // ---------- d4: the environment ----------

    [Fact]
    public void TheEnvironment_IsExactlyTheAllowList_CodexHomeAndTheRefreshOverride_AndNothingElse()
    {
        using var dir = TempDir.For("cxl-");
        var job = TheJob(dir.Path);

        var names = Vendor.Environment(BoundaryHost.Current(), job).ToDictionary(pair => pair.Key, pair => pair.Value);

        names.Keys.Should().Equal("PATH", "HOME", "CODEX_HOME", "CODEX_REFRESH_TOKEN_URL_OVERRIDE", "TMPDIR", "LANG", "TERM", "NO_COLOR", "GIT_TERMINAL_PROMPT");
        names["HOME"].Should().Be(job.CoderHomeOf("codex"));
        names["CODEX_HOME"].Should().Be(Path.Combine(job.CoderHomeOf("codex"), ".codex"));
        names["CODEX_REFRESH_TOKEN_URL_OVERRIDE"].Should().Be("http://127.0.0.1:9/oauth/token");
        names.Keys.Should().NotContain(name => name.Contains("KEY", StringComparison.Ordinal) || name.Contains("TOKEN", StringComparison.Ordinal) && name != "CODEX_REFRESH_TOKEN_URL_OVERRIDE");
    }

    [Fact]
    public void ACanaryValueOfTheRequestsEnvironment_IsInNoArgvOfTheUnit()
    {
        const string Canary = "made-up-canary-value-7731";
        using var dir = TempDir.For("cxl-");
        var job = TheJob(dir.Path);
        var host = BoundaryHost.Current() with { RealHome = "/nonexistent/coai-real-home" };
        var program = new CoderProgram("/opt/codex/bin/codex", "/opt/codex/bin", string.Empty);
        var request = new ProcessRequest("/opt/codex/bin/codex", Vendor.FreshArgv(Model, job.Tree.Root), job.Tree.Root)
        {
            Environment = new Dictionary<string, string?> { ["OPENAI_API_KEY"] = Canary },
        };

        var arguments = CoderUnit.Wrapped("coai-coder-x-c1", job.Tree.Root, Vendor.UnitProperties(host, job, program, TimeSpan.FromMinutes(5)), Vendor.Environment(host, job), program, request.Arguments, CoderUnit.Cross(request, _ => null), null);

        arguments.Should().NotContain(argument => argument.Contains(Canary, StringComparison.Ordinal));
    }

    [Fact]
    public void TheHomeLayout_IsTheVendorsOwn_AndTheCopyLiesWhereCodexReadsIt()
    {
        using var dir = TempDir.For("cxl-");
        var job = TheJob(dir.Path);

        var home = Vendor.Home(job);

        home.Home.Should().Be(Path.Combine(job.JobRoot, "coder", "codex", "home"));
        home.SignInFile.Should().Be(Path.Combine(home.Home, ".codex", "auth.json"));
        home.Folders.Should().Contain(Path.Combine(home.Home, ".codex"));
    }

    [Fact]
    public void TheCodexRow_IsNotLaunchableByData_SoTheProductsSetRefusesIt_UntilTheMarkIsTurnedOn()
    {
        var resolution = CoderVendors.Shipped.Resolve("codex");

        resolution.Should().BeOfType<CoderVendorResolution.Refused>().Which.Sentence.Should().Contain("not launchable yet");
    }
}

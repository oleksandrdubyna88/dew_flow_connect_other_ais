using CoaiMcp.Core.Toolchain;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What the head changed in the definitions of the checks is REPORTED, never refused (owner decision 2026-11-26, which replaced the interim gate "frozen build definitions" of E4 s3 passes 2 and 3): the head's own definitions run — a project or a test project the coder adds is restored, built and tested —
/// and the envelope says <c>definitionsChanged</c>, the one-line summary, the counts, the steps the base had and the head lost, the changed test commands and the test files modified or deleted; the job's record lists the files. Each case of <c>Exploits</c> is a change the security verify MEASURED
/// as a way to a green check without the base's tests: the report shows it, so the person can see it. The real coder rig: real git, the real check boundary. The tree is proven equal to the commit before each round (M6), a step's home and temp are emptied (M16), and a check unit that cannot be stopped ends the job (S17).
/// </summary>
[Collection("fakecli-env")]
public sealed class AHeadsChangesAreReportedTests
{
    private const string Library = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup></Project>";

    private const string Ignore = "bin/\nobj/\nnode_modules/\n";

    private const string NodePackage = "{ \"name\": \"p\", \"version\": \"1.0.0\", \"scripts\": { \"test\": \"jest\" }, \"devDependencies\": { \"jest\": \"29.7.0\" } }\n";

    private const string NodeLock = "{ \"name\": \"p\", \"lockfileVersion\": 3, \"packages\": { \"\": { \"name\": \"p\" }, \"node_modules/jest\": { \"version\": \"29.7.0\", \"resolved\": \"https://registry.npmjs.org/jest/-/jest-29.7.0.tgz\" } } }\n";

    private static Dictionary<string, string> DotnetBase() => new()
    {
        ["App.csproj"] = Library, ["Class1.cs"] = "public class Class1 { }\n", [".gitignore"] = Ignore,
        ["src/App.slnx"] = "<Solution><Project Path=\"App/App.csproj\" /></Solution>", ["src/App/App.csproj"] = Library,
        ["build/common.targets"] = "<Project />", ["t#1/a.targets"] = "<Project />", ["te,st/T.csproj"] = Library,
    };

    private static Dictionary<string, string> NodeBase() => new()
    {
        ["package.json"] = NodePackage, ["package-lock.json"] = NodeLock, [".gitignore"] = Ignore, ["index.js"] = "module.exports = 1;\n",
        ["tsconfig.json"] = "{ \"compilerOptions\": { } }\n", ["packages/m/package.json"] = "{ \"name\": \"m\", \"scripts\": { } }\n",
    };

    public static TheoryData<string, bool, string, string> Exploits => new()
    {
        // .NET: the head changes a definition the base has, or adds one.
        { "M7 a property in the test project: --ignore-exit-code 2", false, "App.csproj", Library.Replace("</Project>", "<PropertyGroup><TestingPlatformCommandLineArguments>--ignore-exit-code 2</TestingPlatformCommandLineArguments></PropertyGroup></Project>") },
        { "M7 D4 a NEW nearer Directory.Build.props that holds only a property", false, "tests/Directory.Build.props", "<Project><PropertyGroup><VSTestTestCaseFilter>Name=none</VSTestTestCaseFilter></PropertyGroup></Project>" },
        { "M9 a project file the head adds, with an InitialTargets Exec", false, "tools/N.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" InitialTargets=\"HeadCode\"><Target Name=\"HeadCode\"><Exec Command=\"id\" /></Target></Project>" },
        { "M10 D5 a plain-path import outside the unit's folder changes", false, "build/common.targets", "<Project><Target Name=\"VSTest\" /></Project>" },
        { "M11 D6 the same length of path: a new t!1/a.targets", false, "t!1/a.targets", "<Project />" },
        { "M11 D7 a test project in a folder with a comma", false, "te,st/T.csproj", Library.Replace("</Project>", "<Target Name=\"VSTest\" /></Project>") },
        { "M11 D6 the base's t#1/a.targets gets a VSTest target", false, "t#1/a.targets", "<Project><Target Name=\"VSTest\" /></Project>" },
        { "a solution file", false, "src/App.slnx", "<Solution />" },
        { "global.json", false, "global.json", "{ \"sdk\": { \"version\": \"10.0.100\" } }" },
        { "nuget.config in any case", false, "NuGet.Config", "<configuration />" },
        // pass 3, M15: files a tool reads by itself that no list of names knew
        { "M15 tests/T.csproj.user with a TestingPlatform property", false, "tests/T.csproj.user", "<Project><PropertyGroup><TestingPlatformCommandLineArguments>--ignore-exit-code 2</TestingPlatformCommandLineArguments></PropertyGroup></Project>" },
        { "M15 tests/T.csproj.user with InitialTargets and an Exec", false, "tests/T.csproj.user", "<Project InitialTargets=\"HeadCode\"><Target Name=\"HeadCode\"><Exec Command=\"id\" /></Target></Project>" },
        { "M15 Properties/launchSettings.json", false, "tests/Properties/launchSettings.json", "{ \"profiles\": { \"x\": { \"commandLineArgs\": \"--ignore-exit-code 2\" } } }" },
        { "M15 Shared.projitems", false, "src/Shared.projitems", "<Project />" },
        { "M15 build/common.proj", false, "build/common.proj", "<Project />" },
        { "M15 build/Tasks.tasks", false, "build/Tasks.tasks", "<Project />" },
        { "M15 .editorconfig", false, ".editorconfig", "root = true\n[*.cs]\ndotnet_diagnostic.CS8600.severity = none\n" },
        { "M15 .globalconfig", false, ".globalconfig", "is_global = true\n" },
        { "M15 stylecop.json", false, "stylecop.json", "{ }" },
        { "M15 T.xunit.runner.json", false, "tests/T.xunit.runner.json", "{ }" },
        { "M15 a local package source", false, "packages/Foo.1.0.0.nupkg", "PK" },
        { "M15 MSBuild.rsp", false, "MSBuild.rsp", "-p:X=1\n" },
        { "a shell script a definition could invoke", false, "scripts/test.sh", "exit 0\n" },
        // node
        { "M15 vite.config.ts", true, "vite.config.ts", "export default { test: { passWithNoTests: true } };\n" },
        { "M15 jest.unit.config.js", true, "jest.unit.config.js", "module.exports = { passWithNoTests: true };\n" },
        { "M15 test/jest-e2e.json", true, "test/jest-e2e.json", "{ \"passWithNoTests\": true }" },
        { "M15 .env", true, ".env", "NODE_OPTIONS=--require ./x.js\n" },
        { "M15 playwright.config.ts", true, "playwright.config.ts", "export default {};\n" },
        { "M15 .swcrc", true, ".swcrc", "{ }" },
        { "M15 spec/support/jasmine.json", true, "spec/support/jasmine.json", "{ }" },
        { "M15 a tarball", true, "vendor/x.tgz", "x" },
        { "M15 a source file named like a setup file", true, "setup.ts", "export {};\n" },
        // node
        { "M8 a second scripts.test key", true, "package.json", "{ \"name\": \"p\", \"version\": \"1.0.0\", \"scripts\": { \"test\": \"jest\", \"test\": \"echo passed\" }, \"devDependencies\": { \"jest\": \"29.7.0\" } }\n" },
        { "M9 the lock file's resolved address", true, "package-lock.json", NodeLock.Replace("https://registry.npmjs.org/jest/-/jest-29.7.0.tgz", "http://127.0.0.1:9/jest-29.7.0.tgz") },
        { "M12 N2 white space inside a string of a runner configuration", true, "package.json", NodePackage.Replace("\"scripts\"", "\"jest\": { \"roots\": [\"s rc\"] }, \"scripts\"") },
        { "M12 N3 a workspace member gets a jest member", true, "packages/m/package.json", "{ \"name\": \"m\", \"scripts\": { }, \"jest\": { \"passWithNoTests\": true } }\n" },
        { "M12 N4 the first word of a script becomes a file: package", true, "package.json", NodePackage.Replace("\"jest\": \"29.7.0\"", "\"jest\": \"file:./tools/fake\"") },
        { "M12 N7 a NEW dependency whose bin is jest", true, "package.json", NodePackage.Replace("\"devDependencies\": {", "\"devDependencies\": { \"aaa\": \"file:./tools/aaa\",") },
        { "M12 N6 tsconfig.json gets noCheck", true, "tsconfig.json", "{ \"compilerOptions\": { \"noCheck\": true } }\n" },
        { "a jest configuration file", true, "jest.config.js", "module.exports = { passWithNoTests: true };\n" },
        { ".npmrc in any case", true, ".NPMRC", "script-shell=/usr/bin/true\n" },
        // coai and git
        { "a toolchain file the head adds", false, ".coai/toolchain.yaml", "version: 1\nprojects: []\n" },
        // .gitmodules is refused one step earlier, by the commit layer of E2 (coder-commit): it is never a coder's to change.
        { ".gitattributes", false, ".gitattributes", "*.cs filter=x\n" },
    };

    [Theory]
    [MemberData(nameof(Exploits))]
    public void EveryMeasuredExploit_IsShownByTheReport_AsAChangedDefinitionOrConfigurationFile(string exploit, bool node, string path, string text)
    {
        _ = (node, text);
        var changes = FrozenDefinitions.ParseRaw($":100644 100644 {new string('1', 40)} {new string('2', 40)} M\0{path}\0")!;

        var classified = FrozenDefinitions.Classify(changes, ToolchainDefaults.Current);

        classified.Paths.Should().Equal([path], exploit + ": a head that does this is not refused, and the report names the file");
        DefinitionReport.Of(DefinitionChanges.None, classified, ToolchainChange.Unchanged, null).Changed.Should().BeTrue();
    }

    [Fact]
    public async Task AHeadThatChangesAProjectProperty_IsNotRefused_ItsOwnDefinitionsRun_AndTheReportSaysWhatChanged()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: string.Empty, extra: DotnetBase());
        var edited = Library.Replace("</Project>", "<PropertyGroup><TestingPlatformCommandLineArguments>--ignore-exit-code 2</TestingPlatformCommandLineArguments></PropertyGroup></Project>");
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { ["App.csproj"] = edited, [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report()));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.Stop.Should().NotBe("toolchain-refused", ended.Reason);
        var envelope = rig.Envelope(job);
        envelope.GetProperty("checks").GetArrayLength().Should().BeGreaterThan(0, "the head's definitions ran");
        envelope.GetProperty("definitionsChanged").GetBoolean().Should().BeTrue();
        envelope.GetProperty("definitionSummary").GetString().Should().Contain("1 definition or configuration file(s)").And.Contain("the base's tests may have been changed");
        envelope.GetProperty("toolchain").GetProperty("changedDefinitionFiles").GetInt32().Should().Be(1);
        File.ReadAllText(Path.Combine(rig.World.Store.FolderOf(job.JobId)!.Location, "checks", "round-0", "changed-definitions.txt")).Should().Contain("file: 'App.csproj'");
    }

    [Fact]
    public async Task AProjectTheCoderAdds_IsRestoredBuiltAndTested_AndMarkedDefinedByHead()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: string.Empty, extra: DotnetBase());
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { ["Other.csproj"] = Library, ["Class2.cs"] = "public class Class2 { }\n", [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report()));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.Stop.Should().NotBe("toolchain-refused", ended.Reason);
        var envelope = rig.Envelope(job);
        envelope.GetProperty("checks").EnumerateArray().Select(step => step.GetProperty("project").GetString()).Should().Contain("Other.csproj", "a project the coder added is part of the head's definitions");
        envelope.GetProperty("toolchain").GetProperty("projects").EnumerateArray().Single(project => project.GetProperty("path").GetString() == "Other.csproj").GetProperty("definedByHead").GetBoolean().Should().BeTrue();
        envelope.GetProperty("definitionsChanged").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ATestCommandTheHeadChangesInTheToolchainFile_IsReported_AndTheHeadsFileRuns()
    {
        await using var rig = await CoderRig.CreateAsync();
        var changed = ToolchainFixture.Shell("echo built", "echo head-test-ran");
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { [".coai/toolchain.yaml"] = changed, [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report()));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var envelope = rig.Envelope(job);
        envelope.GetProperty("toolchain").GetProperty("changedTestCommands").EnumerateArray().Select(line => line.GetString()).Should().Contain(line => line!.Contains("the test command"));
        envelope.GetProperty("definitionsChanged").GetBoolean().Should().BeTrue();
        File.ReadAllText(Path.Combine(rig.World.Store.FolderOf(job.JobId)!.Location, envelope.GetProperty("checks").EnumerateArray().First(step => step.GetProperty("step").GetString() == "test").GetProperty("logRef").GetString()!)).Should().Contain("head-test-ran");
    }

    [Fact]
    public async Task AStepTheBaseHadAndTheHeadLost_IsReportedAsLost_NotRefused()
    {
        await using var rig = await CoderRig.CreateAsync();
        var withoutTest = "version: 1\nprojects:\n  - path: \"app\"\n    adapter: node\n    build:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"echo built\"\n";
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { [".coai/toolchain.yaml"] = withoutTest, [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report()));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var toolchain = rig.Envelope(job).GetProperty("toolchain");
        toolchain.GetProperty("lostSteps").EnumerateArray().Select(line => line.GetString()).Should().Contain("the worktree removed test of app");
        toolchain.GetProperty("definitionsChanged").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task AHeadThatChangesNothingButSource_ReportsNothingChanged()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: string.Empty, extra: DotnetBase());
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { ["Class1.cs"] = "public class Class1 { public int X; }\n", ["notes/readme.md"] = "x\n", ["src/web/app.ts"] = "export {};\n" }, CoderRig.Report()));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.Stop.Should().NotBe("toolchain-refused", ended.Reason);
        var envelope = rig.Envelope(job);
        envelope.GetProperty("checks").GetArrayLength().Should().BeGreaterThan(0);
        envelope.GetProperty("definitionsChanged").GetBoolean().Should().BeFalse();
        envelope.GetProperty("definitionSummary").GetString().Should().BeEmpty();
    }

    // ---- M6: the tree equals the commit, ignored files included, before each round ----

    [Fact]
    public async Task M6_AnIgnoredFileTheCoderLeftInTheTree_IsGoneBeforeTheChecksRun()
    {
        var toolchain = "version: 1\nprojects:\n  - path: \"app\"\n    adapter: node\n    build:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"test ! -e tests/obj/x.targets && echo built\"\n    test:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"echo tested\"\n";
        await using var rig = await CoderRig.CreateAsync(toolchain: toolchain, extra: new Dictionary<string, string> { [".gitignore"] = Ignore });
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { ["tests/obj/x.targets"] = "<Project />\n", [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report()));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        rig.Envelope(job).GetProperty("green").GetBoolean().Should().BeTrue("the ignored file is not in the commit, so it must not be in the tree the checks run in");
    }

    [Fact]
    public async Task M6_AnIgnoredNpmrcTheCoderLeftInTheTree_CannotChangeWhatNpmRuns()
    {
        var toolchain = "version: 1\nprojects:\n  - path: \"app\"\n    adapter: node\n    build:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"test ! -e .npmrc && echo built\"\n    test:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"echo tested\"\n";
        await using var rig = await CoderRig.CreateAsync(toolchain: toolchain, extra: new Dictionary<string, string> { [".gitignore"] = Ignore + ".npmrc\n" });
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { [".npmrc"] = "script-shell=/usr/bin/true\n", [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report()));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
    }

    // ---- M13: the selection is information only, every detected unit runs ----

    [Fact]
    public async Task M13_EveryDetectedUnitRuns_WhateverTheSelectionWouldHaveLeftOut_AndTheEnvelopeSaysItWasNotApplied()
    {
        var files = new Dictionary<string, string>
        {
            ["u1/App.csproj"] = Library, ["u1/Class1.cs"] = "public class Class1 { }\n", ["u2/Other.csproj"] = Library, ["u2/Class2.cs"] = "public class Class2 { }\n", [".gitignore"] = Ignore,
        };
        await using var rig = await CoderRig.CreateAsync(toolchain: string.Empty, extra: files);
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { ["u1/Class1.cs"] = "public class Class1 { public int X; }\n" }, CoderRig.Report()));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.Stop.Should().NotBe("toolchain-refused", ended.Reason);
        var envelope = rig.Envelope(job);
        envelope.GetProperty("checks").EnumerateArray().Select(step => step.GetProperty("project").GetString()).Distinct().Should().BeEquivalentTo("u1/App.csproj", "u2/Other.csproj");
        envelope.GetProperty("toolchain").GetProperty("selection").GetProperty("applied").GetBoolean().Should().BeFalse();
    }

    // ---- S17 of the verify: a check unit that could not be stopped ends the job as coder-boundary ----

    [Fact]
    public async Task S17_ACheckUnitThatCannotBeStopped_EndsTheJobCoderBoundary_NoFixTurnStartsBesideIt()
    {
        var toolchain = "version: 1\ntimeouts:\n  build: 3s\nprojects:\n  - path: \"app\"\n    adapter: node\n    build:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"trap '' TERM; sleep 300\"\n    test:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"echo tested\"\n";
        await using var rig = await CoderRig.CreateAsync(toolchain: toolchain);
        rig.Degrade = arguments => arguments.Any(argument => argument.StartsWith("coai-check-", StringComparison.Ordinal)) ? WithoutStopTimeout(arguments) : arguments;
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report()));
        var job = rig.NewJob();
        try
        {
            var (_, ended) = await rig.RunAsync(job);

            (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), ended.Reason);
            rig.Launches(job).Should().ContainSingle("no fix turn starts while a process of a check unit may live");
        }
        finally
        {
            foreach (var unit in await rig.UnitsOfAsync(job))
            {
                await new CoaiMcp.Runners.Processes.ProcessLauncher().RunAsync(new CoaiMcp.Runners.Processes.ProcessRequest("systemctl", ["--user", "kill", "--signal=SIGKILL", "--kill-whom=all", unit.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]], rig.Repo) { Timeout = TimeSpan.FromSeconds(20) });
            }
        }
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

    // ---- pass 3, M16: the job's home, temp and caches ----

    [Fact]
    public async Task M16_WhatAStepWritesInTheJobsHomeAndTemp_IsGoneForTheNextStepAndTheNextRound()
    {
        var absent = "test ! -e \"$HOME/.npmrc\" && test ! -e \"$HOME/.local\" && test ! -e \"$TMPDIR/leftover\"";
        var build = $"{absent} && mkdir -p \"$HOME/.local/share/Microsoft/MSBuild/Current/Microsoft.Common.targets/ImportAfter\" && echo x > \"$HOME/.local/share/Microsoft/MSBuild/Current/Microsoft.Common.targets/ImportAfter/x.targets\" && echo y > \"$HOME/.npmrc\" && echo y > \"$TMPDIR/leftover\" && echo built";
        var test = $"{absent} || exit 3; if grep -rq '{CoderRig.Red}' src 2>/dev/null; then echo 'FAILED thing: the state is red'; exit 1; fi; echo tested";
        await using var rig = await CoderRig.CreateAsync(toolchain: ToolchainFixture.Shell(build, test));
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { [CoderRig.StateFile] = CoderRig.Red + "\n" }, CoderRig.Report()), new CoderRig.Launch(new Dictionary<string, string> { [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report()));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var envelope = rig.Envelope(job);
        envelope.GetProperty("green").GetBoolean().Should().BeTrue("the build of the second round found no home, no temp file and no config left by the first, and the test step none left by the build of its own round");
        envelope.GetProperty("rounds").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task M16_OnlyARestoreStepCanWriteTheCaches_EveryOtherStepSeesThemReadOnly()
    {
        var restore = "for v in NUGET_PACKAGES npm_config_cache DOTNET_CLI_HOME; do eval \"d=\\$$v\"; echo x > \"$d/from-restore\" || exit 4; done; echo restored";
        var build = "for v in NUGET_PACKAGES npm_config_cache DOTNET_CLI_HOME; do eval \"d=\\$$v\"; test -e \"$d/from-restore\" || exit 5; if echo y > \"$d/from-build\" 2>/dev/null; then exit 7; fi; done; echo built";
        var toolchain = "version: 1\nprojects:\n  - path: \"app\"\n    adapter: node\n    restore:\n      - \"/bin/sh\"\n      - \"-c\"\n      - '" + restore + "'\n    build:\n      - \"/bin/sh\"\n      - \"-c\"\n      - '" + build + "'\n    test:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"if echo y > \\\"$NUGET_PACKAGES/from-test\\\" 2>/dev/null; then exit 8; fi; echo tested\"\n";
        await using var rig = await CoderRig.CreateAsync(toolchain: toolchain);
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report()));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        rig.Envelope(job).GetProperty("checks").EnumerateArray().Select(step => (step.GetProperty("step").GetString(), step.GetProperty("exitCode").GetInt32())).Should().Equal(("restore", 0), ("build", 0), ("test", 0));
    }

    // ---- S22: a .git entry below the root, which the clean and status --ignored do not see ----

    [Fact]
    public async Task S22_ANestedGitFolderInATrackedFolder_RefusesTheRound()
    {
        await using var rig = await CoderRig.CreateAsync(extra: new Dictionary<string, string> { ["tests/Existing.cs"] = "public class Existing { }\n" });
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { [CoderRig.StateFile] = CoderRig.Green + "\n", ["tests/.git/Hidden.cs"] = "public class Hidden { }\n" }, CoderRig.Report()));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Failed, ended.Reason);
        ended.Stop.Should().BeOneOf("checks-changed-tree", "coder-commit");
        rig.Envelope(job).GetProperty("green").GetBoolean().Should().BeFalse();
    }

    // ---- S24: a person can tell which files the head changed ----

    [Fact]
    public async Task S24_TheChangedDefinitionPathsAreInTheJobsRecord_NotInTheSentence()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: string.Empty, extra: DotnetBase());
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { ["tests/T.csproj.user"] = "<Project />\n", [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report()));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.Reason.Should().NotContain("T.csproj.user");
        File.ReadAllText(Path.Combine(rig.World.Store.FolderOf(job.JobId)!.Location, "checks", "round-0", "changed-definitions.txt")).Should().Contain("tests/T.csproj.user");
    }

    // ---- S21: the envelope counts the tests the head changed or deleted ----

    [Fact]
    public async Task S21_TheEnvelopeCountsTheTestFilesTheHeadChanged()
    {
        var files = DotnetBase();
        files["tests/FooTests.cs"] = "public class FooTests { }\n";
        await using var rig = await CoderRig.CreateAsync(toolchain: string.Empty, extra: files);
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { ["tests/FooTests.cs"] = "public class FooTests { public int X; }\n", ["tests/NewTests.cs"] = "public class NewTests { }\n", ["Class1.cs"] = "public class Class1 { public int X; }\n" }, CoderRig.Report()));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.Stop.Should().NotBe("toolchain-refused", ended.Reason);
        var tests = rig.Envelope(job).GetProperty("headTestFiles");
        (tests.GetProperty("modified").GetInt32(), tests.GetProperty("added").GetInt32(), tests.GetProperty("deleted").GetInt32()).Should().Be((1, 1, 0));
    }

}

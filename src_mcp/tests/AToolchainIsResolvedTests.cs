using CoaiMcp.Core.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E4 s2 t5 (security): <see cref="ToolchainResolve"/> — the file at the base wins; else detection at the base and at the head; a check the base had is never lost at the head; a stack that is known and not built is refused by name; neither
/// <c>not_configured</c> nor <c>toolchain_refused</c> plans a step. Scenarios D1 to D7, D11 of the SPEC that need no git (the golden trees, with real git, are story s2 t6).
/// </summary>
public sealed class AToolchainIsResolvedTests
{
    private static readonly string VsTestProject = "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"1\" /></ItemGroup></Project>";

    private static readonly string LibProject = "<Project Sdk=\"Microsoft.NET.Sdk\"/>";

    private static string Package(string scripts = "\"test\": \"jest\"") => "{ \"name\": \"p\", \"scripts\": { " + scripts + " }, \"devDependencies\": { \"jest\": \"1\" } }";

    private static ManifestSet Set(params (string Path, string Text)[] files)
    {
        var table = ToolchainDefaults.Current;
        var list = new List<ManifestFile>();
        foreach (var (path, text) in files)
        {
            var kind = ManifestSearch.Classify(path, table) ?? throw new InvalidOperationException($"{path} is not a name the search looks for");
            list.Add(new ManifestFile(path, kind.Stack, kind.Kind, new string('a', 40), text.Length, kind.Kind == ManifestKind.Present ? string.Empty : text));
        }

        return new ManifestSet(new string('c', 40), [.. list.OrderBy(file => file.Path, StringComparer.Ordinal)], []);
    }

    private static readonly (string, string)[] Node = [("package.json", Package()), ("package-lock.json", "{}")];

    private static readonly (string, string)[] Solution = [("App.slnx", "x"), ("src/App.csproj", LibProject), ("tests/T.csproj", VsTestProject)];

    private sealed class Reads(ManifestRead atBase, ManifestRead atHead)
    {
        public int Base { get; private set; }

        public int Head { get; private set; }

        public ManifestRead AtBase() { Base++; return atBase; }

        public ManifestRead AtHead() { Head++; return atHead; }
    }

    private static Reads Tree(IEnumerable<(string, string)> atBase, IEnumerable<(string, string)>? atHead = null) =>
        new(new ManifestRead.Found(Set([.. atBase])), new ManifestRead.Found(Set([.. atHead ?? atBase])));

    private static ToolchainResolution Resolve(ToolchainRead file, Reads reads, string? setting = null) =>
        ToolchainResolve.Resolve(file, reads.AtBase, reads.AtHead, setting, ToolchainDefaults.Current);

    private static ToolchainRead NoFile => new ToolchainRead.NotConfigured();

    private static ToolchainRead File(params ToolchainProject[] projects) => new ToolchainRead.Configured(new RepoToolchain(projects, [], ToolchainTimeouts.Default));

    private static ToolchainCommand Cmd(params string[] argv) => new(true, argv);

    private static ResolvedToolchain Resolved(ToolchainResolution resolution) => resolution.Should().BeOfType<ToolchainResolution.Resolved>(resolution.ToString()).Subject.Toolchain;

    private static string Refused(ToolchainResolution resolution) => resolution.Should().BeOfType<ToolchainResolution.Refused>(resolution.ToString()).Subject.Sentence;

    // ---- d1: the file wins ----

    [Fact]
    public void AFileAtTheBase_GivesTheSourceFile_AndDetectionIsNeverAsked()
    {
        var reads = Tree(Solution);
        var file = File(new ToolchainProject("src", "dotnet", Cmd("dotnet", "build"), Cmd("./t"), null));

        var toolchain = Resolved(Resolve(file, reads));

        toolchain.Source.Should().Be(ToolchainSource.File);
        toolchain.Projects.Should().ContainSingle().Which.Origin.Should().Be("file");
        (reads.Base, reads.Head).Should().Be((0, 0), "detection decides nothing for a project that states a command");
    }

    [Fact]
    public void AProjectOfTheFileWithNoCommand_GetsTheDefaultsOfItsOwnFolder_AtTheBase()
    {
        var reads = Tree(Solution.Select(file => ($"app/{file.Item1}", file.Item2)).Append(("global.json", "{}")));
        var file = File(new ToolchainProject("app", "dotnet", ToolchainCommand.NotStated, ToolchainCommand.NotStated, null), new ToolchainProject("other", "node", Cmd("npm", "run", "build"), ToolchainCommand.NotStated, null));

        var toolchain = Resolved(Resolve(file, reads));

        toolchain.Source.Should().Be(ToolchainSource.FileAndDefaults);
        toolchain.Projects.Select(project => (project.Id, project.Origin)).Should().Equal(("app/App.slnx", "defaults"), ("other", "file"));
        toolchain.Projects[0].Detected!.Steps.Select(step => step.Name).Should().Equal("restore", "build", "test");
    }

    [Fact]
    public void ARefusalOfTheDetector_InTheFolderOfAProjectWithNoCommand_RefusesTheFile()
    {
        var reads = Tree([("app/package.json", Package())]);
        var file = File(new ToolchainProject("app", "node", ToolchainCommand.NotStated, ToolchainCommand.NotStated, null));

        var sentence = Refused(Resolve(file, reads));

        sentence.Should().Contain("app").And.Contain("lock file");
    }

    [Fact]
    public void AProjectWithNoCommandAndNoManifestInItsFolder_RefusesTheFile()
    {
        var reads = Tree([("other/package.json", Package()), ("other/package-lock.json", "{}")]);

        Refused(Resolve(File(new ToolchainProject("app", "node", ToolchainCommand.NotStated, ToolchainCommand.NotStated, null)), reads)).Should().Contain("no node manifest was found in 'app'");
    }

    [Fact]
    public void AProjectWithNoCommandWhoseAdapterHasNoDefaults_RefusesTheFile()
    {
        var reads = Tree(Node);

        Refused(Resolve(File(new ToolchainProject("lib", "rust", ToolchainCommand.NotStated, ToolchainCommand.NotStated, null)), reads)).Should().Contain("rust").And.Contain("no defaults");
    }

    [Fact]
    public void AFileThatIsRefused_IsRefused_AndNothingIsRead()
    {
        var reads = Tree(Solution);

        var resolution = Resolve(new ToolchainRead.Refused(["build of project 'x' is not a list"]), reads);

        Refused(resolution).Should().Contain("not a list");
        (reads.Base, reads.Head).Should().Be((0, 0));
    }

    // ---- d2: no file; the head ----

    [Fact]
    public void NoFile_DetectionAtTheBaseAndTheHead_GivesTheDetectedSource()
    {
        var toolchain = Resolved(Resolve(NoFile, Tree(Node)));

        toolchain.Source.Should().Be(ToolchainSource.Detected);
        toolchain.Projects.Should().ContainSingle().Which.Should().Match<ResolvedProject>(project => project.Origin == "detected" && !project.DefinedByHead && project.Id == "package.json");
        toolchain.Differences.Should().BeEmpty();
    }

    [Fact]
    public void AProjectOnlyTheHeadHas_IsAddedWithTheDefaults_AndMarkedDefinedByHead()
    {
        var head = Node.Concat([("tool/package.json", Package()), ("tool/package-lock.json", "{}")]);

        var toolchain = Resolved(Resolve(NoFile, Tree(Node, head)));

        toolchain.Projects.Select(project => (project.Id, project.DefinedByHead)).Should().Equal(("package.json", false), ("tool/package.json", true));
    }

    [Fact]
    public void AStepTheBaseHadAndTheHeadHasNot_IsRefused_WithTheSentenceOfTheSpec()
    {
        var noTest = new[] { ("package.json", Package("\"build\": \"tsc\"")), ("package-lock.json", "{}") };
        var sentence = Refused(Resolve(NoFile, Tree(Node, noTest)));

        sentence.Should().StartWith("the worktree removed test of package.json");
    }

    [Fact]
    public void ATestScriptReplacedByNothing_ALockFileRemoved_ATestProjectRemoved_AreAllRefusals()
    {
        Refused(Resolve(NoFile, Tree(Node, [("package.json", Package("\"build\": \"x\"")), ("package-lock.json", "{}")]))).Should().Contain("removed test");
        Refused(Resolve(NoFile, Tree(Node, [("package.json", Package())]))).Should().Contain("at the head").And.Contain("no lock file");
        Refused(Resolve(NoFile, Tree(Solution, Solution.Where(file => file.Item1 != "tests/T.csproj")))).Should().Contain("removed test (tests/T.csproj) of App.slnx");
    }

    [Fact]
    public void AWholeProjectRemovedAtTheHead_IsRefused()
    {
        Refused(Resolve(NoFile, Tree(Node, [("Other.csproj", LibProject)]))).Should().Contain("removed");
    }

    [Fact]
    public void ADifferenceOfAFactThatIsNotADefinition_IsListed_NotJudged()
    {
        var head = Node.Append(("tsconfig.json", "{}"));

        var toolchain = Resolved(Resolve(NoFile, Tree(Node, head)));

        toolchain.Differences.Should().Contain(line => line.Contains("tsconfig was 'false' at the base and is 'true' at the head"));
    }

    [Fact]
    public void AScriptAddedAtTheHead_IsAChangeOfTheDefinition_AndIsRefused()
    {
        var head = new[] { ("package.json", Package("\"test\": \"jest\", \"build\": \"tsc\"")), ("package-lock.json", "{}") };

        Refused(Resolve(NoFile, Tree(Node, head))).Should().Contain("the worktree changed scripts.build of package.json");
    }

    // ---- d3: known and not built; nothing found ----

    [Theory]
    [InlineData("Cargo.toml", "rust")]
    [InlineData("composer.json", "php")]
    [InlineData("pyproject.toml", "python")]
    [InlineData("pom.xml", "java")]
    public void AManifestOfAKnownStackWithNoAdapter_AtTheBase_IsRefused_NamingTheStackThePathAndTheCure(string manifest, string stack)
    {
        var sentence = Refused(Resolve(NoFile, Tree(Node.Append(($"svc/{manifest}", "x")))));

        sentence.Should().Contain(stack).And.Contain($"svc/{manifest}").And.Contain("base").And.Contain(".coai/toolchain.yaml");
    }

    [Fact]
    public void ACargoTomlThatAppearsAtTheHead_RefusesTheJob_AsWell()
    {
        var sentence = Refused(Resolve(NoFile, Tree(Node, Node.Append(("Cargo.toml", "[package]")))));

        sentence.Should().Contain("rust").And.Contain("Cargo.toml").And.Contain("head");
    }

    [Fact]
    public void OneRefusedProject_RefusesTheRepository_EvenWhenAnotherCouldBeChecked()
    {
        var files = Node.Concat([("broken/package.json", "{ nope")]);

        Refused(Resolve(NoFile, Tree(files))).Should().Contain("broken/package.json");
    }

    [Fact]
    public void NoManifestOfAnyKnownStack_IsNotConfigured_ADocumentsOnlyTreeToo()
    {
        var resolution = Resolve(NoFile, Tree([("global.json", "{}")]));

        resolution.Should().BeOfType<ToolchainResolution.NotConfigured>();
    }

    [Fact]
    public void WhenEveryProjectIsUnchecked_TheAnswerIsNotConfigured()
    {
        var resolution = Resolve(NoFile, Tree([("package.json", "{ \"name\": \"docs\" }"), ("package-lock.json", "{}")]));

        resolution.Should().BeOfType<ToolchainResolution.NotConfigured>().Which.Note.Should().Contain("unchecked");
    }

    [Fact]
    public void NeitherNotConfiguredNorRefused_PlansAStep()
    {
        var results = new[] { Resolve(NoFile, Tree([("global.json", "{}")])), Resolve(NoFile, Tree([("Cargo.toml", "x")])) };

        results.Should().NotContain(result => result is ToolchainResolution.Resolved);
    }

    [Fact]
    public void AManifestReadThatIsRefused_IsARefusal_NeverAnEmptyAnswer()
    {
        var reads = new Reads(new ManifestRead.Refused("git did not answer"), new ManifestRead.Found(Set()));

        Refused(Resolve(NoFile, reads)).Should().Contain("git did not answer");
        Refused(Resolve(NoFile, new Reads(new ManifestRead.Found(Set(Node)), new ManifestRead.Refused("the head is over a limit")))).Should().Contain("head").And.Contain("over a limit");
    }

    // ---- d4: the setting ----

    [Fact]
    public void DetectionOff_GivesTheAnswerOfE2_NoFileIsNotConfigured_AndNothingIsRead()
    {
        var reads = Tree(Node);

        var resolution = Resolve(NoFile, reads, "off");

        resolution.Should().BeOfType<ToolchainResolution.NotConfigured>().Which.Note.Should().Contain("detection is off");
        (reads.Base, reads.Head).Should().Be((0, 0));
    }

    [Fact]
    public void DetectionOff_LeavesAProjectOfTheFileWithNoCommand_RunningNothing_AsInE2()
    {
        var reads = Tree(Node);

        var toolchain = Resolved(Resolve(File(new ToolchainProject("app", "node", ToolchainCommand.NotStated, ToolchainCommand.NotStated, null)), reads, "OFF"));

        toolchain.Projects.Single().Origin.Should().Be("file");
        (reads.Base, reads.Head).Should().Be((0, 0));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("on", true)]
    [InlineData("ON", true)]
    [InlineData("off", false)]
    [InlineData("Off", false)]
    public void TheSetting_OnOffOrNothing(string? setting, bool expected)
    {
        var notes = new List<string>();

        ToolchainResolve.DetectOn(setting, notes).Should().Be(expected);
        notes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("disabled")]
    [InlineData("offf")]
    public void AnyOtherValue_IsOn_WithANote(string setting)
    {
        var notes = new List<string>();

        ToolchainResolve.DetectOn(setting, notes).Should().BeTrue("a typo must not turn detection off");
        notes.Should().ContainSingle().Which.Should().Contain("COAI_TOOLCHAIN_DETECT").And.Contain(setting);
    }

    [Fact]
    public void TheAnswer_CarriesTheNotesOfTheReaders()
    {
        var withSubmodule = new ManifestSet(new string('c', 40), Set([.. Node]).Files, ["submodule 'vendor/x' was not entered"]);
        var reads = new Reads(new ManifestRead.Found(withSubmodule), new ManifestRead.Found(withSubmodule));

        var toolchain = Resolved(Resolve(NoFile, reads, "maybe"));

        toolchain.Notes.Should().Contain(note => note.Contains("vendor/x")).And.Contain(note => note.Contains("neither on nor off"));
    }
}

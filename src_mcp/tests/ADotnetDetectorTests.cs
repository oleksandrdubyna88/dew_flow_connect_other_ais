using System.Net;
using System.Net.Sockets;
using CoaiMcp.Core.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E4 s2 t3: the .NET detector, pure, from the manifests of a commit. Units from solution files and unreferenced project files, the ProjectReference graph, the test projects and their runner as story s1 measured them; what is ambiguous is refused with the cure.
/// </summary>
public sealed class ADotnetDetectorTests
{
    private const string Sdk = "{ \"sdk\": { \"version\": \"10.0.100\" } }";

    private const string SdkMtp = "{ \"sdk\": { \"version\": \"10.0.100\" }, \"test\": { \"runner\": \"Microsoft.Testing.Platform\" } }";

    private static string Project(string body = "", string references = "", string packages = "") =>
        $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework>{body}</PropertyGroup><ItemGroup>{references}{packages}</ItemGroup></Project>";

    private static string Ref(string include) => $"<ProjectReference Include=\"{include}\" />";

    private static string Pkg(string name) => $"<PackageReference Include=\"{name}\" Version=\"1.0.0\" />";

    private static readonly string VsTest = Project(packages: Pkg("Microsoft.NET.Test.Sdk") + Pkg("xunit"));

    private static readonly string Mtp = Project("<OutputType>Exe</OutputType><UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>", packages: Pkg("xunit.v3"));

    private static readonly string Lib = Project();

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

    private static IReadOnlyList<DetectedProject> Detected(Detection detection) => detection.Should().BeOfType<Detection.Detected>(detection.ToString()).Subject.Projects;

    private static string Refused(Detection detection) => detection.Should().BeOfType<Detection.Refused>(detection.ToString()).Subject.Sentence;

    private static Detection Detect(params (string Path, string Text)[] files) => DotnetDetector.Detect(Set(files), ToolchainDefaults.Current);

    // ---- d1: the units ----

    [Fact]
    public void ASolutionFolder_IsOneUnit_AndOwnsEveryProjectBelowIt()
    {
        var projects = Detected(Detect(("App.slnx", "x"), ("src/App/App.csproj", Lib), ("tests/App.Tests/App.Tests.csproj", VsTest), ("global.json", Sdk)));

        var unit = projects.Should().ContainSingle().Subject;
        unit.Id.Should().Be("App.slnx");
        unit.Folder.Should().Be(".");
        unit.Fact("projects").Should().Be("src/App/App.csproj,tests/App.Tests/App.Tests.csproj");
        unit.Fact("kind").Should().Be("solution");
    }

    [Fact]
    public void TwoSolutionFolders_AreTwoUnits_EachOwningItsOwnProjects()
    {
        var projects = Detected(Detect(("a/A.sln", "x"), ("a/src/A.csproj", Lib), ("b/B.slnx", "x"), ("b/src/B.csproj", Lib)));

        projects.Select(project => project.Id).Should().Equal("a/A.sln", "b/B.slnx");
        projects[0].Fact("projects").Should().Be("a/src/A.csproj");
        projects[1].Fact("projects").Should().Be("b/src/B.csproj");
    }

    [Fact]
    public void AProjectFileOutsideEveryUnit_ThatNothingReferences_IsAUnitOfItsOwn_AReferencedOneIsNot()
    {
        var projects = Detected(Detect(("tool/Tool.csproj", Project(references: Ref("..\\shared\\Shared.csproj"))), ("shared/Shared.csproj", Lib), ("other/Other.csproj", Lib)));

        projects.Select(project => project.Id).Should().Equal("other/Other.csproj", "tool/Tool.csproj");
        projects.Should().OnlyContain(project => project.Fact("kind") == "project");
    }

    [Fact]
    public void TwoSolutionFilesInOneFolder_AreARefusal_ThatNamesBoth()
    {
        var sentence = Refused(Detect(("A.sln", "x"), ("B.slnx", "x"), ("src/A.csproj", Lib)));

        sentence.Should().Contain("A.sln").And.Contain("B.slnx").And.Contain(".coai/toolchain.yaml");
    }

    [Fact]
    public void ASolutionInsideAnotherUnitsFolder_IsARefusal_ThatNamesBoth()
    {
        var sentence = Refused(Detect(("All.slnx", "x"), ("tools/Tools.sln", "x")));

        sentence.Should().Contain("All.slnx").And.Contain("tools/Tools.sln");
    }

    [Fact]
    public void NoDotnetManifest_IsAnEmptyAnswer_NotARefusal()
    {
        Detected(Detect(("package.json", "{}"))).Should().BeEmpty();
    }

    // ---- d2: the XML is untrusted ----

    [Fact]
    public async Task AnExternalEntity_ReadsNoFileAndAsksForNoAddress()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var hostile = $"<?xml version=\"1.0\"?><!DOCTYPE Project [ <!ENTITY pwn SYSTEM \"http://127.0.0.1:{port}/xxe\"> <!ENTITY file SYSTEM \"file:///etc/hostname\"> ]><Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><Description>&pwn;&file;</Description></PropertyGroup></Project>";

        var sentence = Refused(Detect(("src/Evil.csproj", hostile)));
        await Task.Delay(300);

        sentence.Should().Contain("src/Evil.csproj").And.Contain("DOCTYPE").And.Contain(".coai/toolchain.yaml");
        listener.Pending().Should().BeFalse("no connection may be made for an external entity of a manifest");
    }

    [Fact]
    public void AnEntityBomb_IsARefusal_NotAnExpansion()
    {
        var bomb = "<?xml version=\"1.0\"?><!DOCTYPE Project [ <!ENTITY a \"aaaaaaaaaa\"> <!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;\"> <!ENTITY c \"&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;\"> ]><Project><PropertyGroup><X>&c;</X></PropertyGroup></Project>";

        Refused(Detect(("Bomb.csproj", bomb))).Should().Contain("Bomb.csproj");
    }

    [Fact]
    public void TextThatIsNotXml_AndAFileThatIsNotAProject_AreRefusalsThatNameTheFile()
    {
        Refused(Detect(("A.csproj", "not xml at all"))).Should().Contain("A.csproj");
        Refused(Detect(("B.csproj", "<Other/>"))).Should().Contain("B.csproj").And.Contain("not an MSBuild project");
    }

    [Fact]
    public void AProjectReferenceThatLeavesTheRepository_IsANote_AndIsNeverRead()
    {
        var projects = Detected(Detect(("src/App.csproj", Project(references: Ref("..\\..\\..\\elsewhere\\X.csproj") + Ref("$(Root)\\Y.csproj")))));

        var unit = projects.Should().ContainSingle().Subject;
        unit.Notes.Should().Contain(note => note.Contains("1 ProjectReference(s) that leave the repository") && !note.Contains("elsewhere"));
        unit.Notes.Should().Contain(note => note.Contains("1 ProjectReference(s) that hold a property") && !note.Contains("$(Root)"));
    }

    [Fact]
    public void AReferenceWithBackslashesAndDots_ResolvesToTheRepositoryPath()
    {
        var facts = ProjectGraph.Read("src/App/App.csproj", Project(references: Ref("..\\Lib\\.\\Lib.csproj")), null).Facts!;

        facts.References.Should().Equal("src/Lib/Lib.csproj");
    }

    // ---- d3: the test projects, their runner, the steps ----

    [Fact]
    public void AVsTestUnit_HasTheMeasuredSteps_WithTheUnitFilled()
    {
        var unit = Detected(Detect(("App.slnx", "x"), ("src/App.csproj", Lib), ("tests/T.csproj", VsTest))).Single();

        unit.Steps.Select(step => step.Name).Should().Equal("restore", "build", "test");
        unit.Steps[0].Argv.Should().Equal("dotnet", "restore", "./App.slnx");
        unit.Steps[0].Network.Should().Be(DefaultNetwork.Host);
        unit.Steps[1].Argv.Should().Equal("dotnet", "build", "./App.slnx", "-c", "Debug", "--no-restore", "-m:{jobs}");
        unit.Steps[1].Network.Should().Be(DefaultNetwork.None);
        unit.Steps[2].Argv.Should().Equal("dotnet", "test", "./tests/T.csproj", "--no-build", "-c", "Debug");
        unit.Steps[2].Subject.Should().Be("tests/T.csproj");
        unit.Steps.Should().OnlyContain(step => step.WorkingFolder == ".");
        unit.Fact("runner").Should().Be("vstest");
        unit.TestsNone.Should().BeFalse();
    }

    [Fact]
    public void AnMtpUnitWithTheRunnerNamedInGlobalJson_UsesTheProjectForm()
    {
        var unit = Detected(Detect(("App.slnx", "x"), ("tests/T.csproj", Mtp), ("global.json", SdkMtp))).Single();

        unit.Steps.Single(step => step.Name == "test").Argv.Should().Equal("dotnet", "test", "--project", "./tests/T.csproj", "--no-build", "-c", "Debug");
        unit.Fact("runner").Should().Be("mtp");
        unit.Fact("mtpOptIn").Should().Be("true");
    }

    [Fact]
    public void AnMtpUnitWithNoRunnerSetting_IsARefusal_ThatNamesTheCure()
    {
        var sentence = Refused(Detect(("App.slnx", "x"), ("tests/T.csproj", Mtp), ("global.json", Sdk)));

        sentence.Should().Contain("tests/T.csproj").And.Contain("Microsoft.Testing.Platform").And.Contain("global.json").And.Contain(".coai/toolchain.yaml");
    }

    [Fact]
    public void AnMtpUnitWithNoGlobalJsonAtAll_IsARefusal()
    {
        Refused(Detect(("App.slnx", "x"), ("tests/T.csproj", Mtp))).Should().Contain("global.json");
    }

    [Fact]
    public void AVsTestProjectUnderAnMtpGlobalJson_IsARefusal_AsDotnetTestRefusesIt()
    {
        var sentence = Refused(Detect(("App.slnx", "x"), ("tests/T.csproj", VsTest), ("global.json", SdkMtp)));

        sentence.Should().Contain("tests/T.csproj").And.Contain("VSTest");
    }

    [Fact]
    public void TestProjectsOfTwoRunnersInOneUnit_AreARefusal_ThatNamesBoth()
    {
        var sentence = Refused(Detect(("App.slnx", "x"), ("a/Vs.csproj", VsTest), ("b/Mtp.csproj", Mtp), ("global.json", SdkMtp)));

        sentence.Should().Contain("two runners").And.Contain("a/Vs.csproj").And.Contain("b/Mtp.csproj");
    }

    [Fact]
    public void AnIsTestProjectWithNoRunnerFact_IsARefusal_AndIsTestProjectFalseTakesAProjectOut()
    {
        Refused(Detect(("App.slnx", "x"), ("t/T.csproj", Project("<IsTestProject>true</IsTestProject>")))).Should().Contain("t/T.csproj").And.Contain("runner");

        var unit = Detected(Detect(("App.slnx", "x"), ("t/Helper.csproj", Project("<IsTestProject>false</IsTestProject>", packages: Pkg("Microsoft.NET.Test.Sdk"))))).Single();
        unit.TestsNone.Should().BeTrue();
    }

    [Fact]
    public void AnMtpPropertyInDirectoryBuildProps_IsAFactOfTheProjectsBelowIt()
    {
        var props = "<Project><PropertyGroup><UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner></PropertyGroup></Project>";
        var unit = Detected(Detect(("App.slnx", "x"), ("tests/Directory.Build.props", props), ("tests/T.csproj", Project(packages: Pkg("xunit.v3"), body: "<IsTestProject>true</IsTestProject>")), ("global.json", SdkMtp))).Single();

        unit.Fact("runner").Should().Be("mtp");
    }

    [Fact]
    public void AConditionedProperty_IsNotEvaluated_AndIsNoted()
    {
        var conditioned = Project(packages: Pkg("xunit.v3"), body: "<IsTestProject Condition=\"'$(CI)' == 'true'\">true</IsTestProject>");

        var unit = Detected(Detect(("App.slnx", "x"), ("t/T.csproj", conditioned))).Single();

        unit.TestsNone.Should().BeTrue();
        unit.Notes.Should().Contain(note => note.Contains("condition"));
    }

    // ---- d4: no test project, too many units, the formatter ----

    [Fact]
    public void AUnitWithNoTestProject_HasABuild_NoTestStep_AndSaysTestsNone()
    {
        var unit = Detected(Detect(("App.slnx", "x"), ("src/App.csproj", Lib))).Single();

        unit.Steps.Select(step => step.Name).Should().Equal("restore", "build");
        unit.TestsNone.Should().BeTrue();
        unit.Fact("tests").Should().Be("none");
    }

    [Fact]
    public void MoreThanTwentyUnits_IsARefusal_WithTheNumberAndTheLimit()
    {
        var files = Enumerable.Range(0, 21).Select(index => ($"p{index:00}/P{index}.csproj", Lib)).ToArray();

        var sentence = Refused(Detect(files));

        sentence.Should().Contain("21").And.Contain("20");
    }

    [Fact]
    public void AnEditorconfigAtTheRoot_GivesTheFormatStep_WithTheExtensions()
    {
        var unit = Detected(Detect(("App.slnx", "x"), ("src/App.csproj", Lib), (".editorconfig", "root = true"))).Single();

        var format = unit.Steps.Should().Contain(step => step.Name == "format").Subject;
        format.Argv.Should().Equal("dotnet", "format", "whitespace", ".", "--folder", "--include", "{file}");
        format.Extensions.Should().Equal(".cs");
        format.Network.Should().Be(DefaultNetwork.None);
        unit.Steps.Select(step => step.Name).Should().Equal("restore", "format", "build");
    }

    [Fact]
    public void TheSameManifests_GiveTheSameAnswer()
    {
        var files = new[] { ("App.slnx", "x"), ("src/App.csproj", Lib), ("tests/T.csproj", VsTest) };

        var first = Detected(Detect(files));
        var second = Detected(Detect(Enumerable.Reverse(files).ToArray()));

        second.Select(project => project.Fact("projects")).Should().Equal(first.Select(project => project.Fact("projects")));
        second.Single().Steps.Select(step => string.Join(' ', step.Argv)).Should().Equal(first.Single().Steps.Select(step => string.Join(' ', step.Argv)));
    }

    [Fact]
    public void NoArgvItem_IsCopiedFromAManifest()
    {
        var hostile = Project(references: Ref("..\\evil; rm -rf ~\\X.csproj"), body: "<AssemblyName>$(curl evil)</AssemblyName>");
        var unit = Detected(Detect(("App.slnx", "x"), ("src/App.csproj", hostile), ("tests/T.csproj", VsTest))).Single();

        unit.Steps.SelectMany(step => step.Argv).Should().NotContain(item => item.Contains("evil") || item.Contains("curl"));
    }
}

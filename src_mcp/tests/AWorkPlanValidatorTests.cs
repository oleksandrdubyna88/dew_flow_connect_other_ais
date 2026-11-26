using CoaiMcp.Core.Work;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E1 s2 t3 — the scenarios of the spec (B2 to B9) over small plans in memory: what each rule says about WHERE, the limits that come from the device, the
/// <c>important</c> list and the notes. The vectors hold the codes; these hold the words, the paths and the numbers.
/// </summary>
public sealed class AWorkPlanValidatorTests
{
    private static Dictionary<string, string> Base() => WorkPlan.Base();

    private static WorkReport Validate(Dictionary<string, string> files, WorkLimits? limits = null) => WorkPlan.Validate(files, limits);

    [Fact]
    public void TheBasePlan_IsValid_AndCounted()
    {
        var report = Validate(Base());

        report.Valid.Should().BeTrue(string.Join("; ", report.Errors.Select(error => error.Message)));
        report.Counts.Should().Be(new WorkCounts(1, 1, 1, 1));
        report.Errors.Should().BeEmpty();
        report.Truncated.Should().BeFalse();
    }

    [Fact]
    public void ANoFeatureFile_IsNotAPlan()
    {
        var files = Base();
        files.Remove("feature.yaml");

        var report = Validate(files);

        report.FeatureFound.Should().BeFalse();
        report.Valid.Should().BeFalse();
        report.Errors.Should().ContainSingle().Which.Code.Should().Be(WorkErrorCodes.MissingFile);
        report.Counts.Should().Be(new WorkCounts(0, 0, 0, 0));
    }

    // ---------- B2 and B3: limits and MVP ----------

    [Fact]
    public void B2_TheDeviceCap_IsTheCapChecked_NotAConstant()
    {
        var files = Base();
        files["epoch-1/epoch.yaml"] = "id: epoch-1\ntitle: F\ndependsOn: []\nepics:\n  - e1-core\n  - e2-b\n  - e3-c\n  - e4-d\n";
        foreach (var epic in new[] { "e2-b", "e3-c", "e4-d" })
        {
            files[$"epoch-1/{epic}/epic.yaml"] = $"id: {epic}\ntitle: X\ndependsOn: []\n";
        }

        var capThree = Validate(files, WorkPlan.Defaults with { MaxEpicsPerEpoch = 3 });
        var capFive = Validate(files);
        var capFour = Validate(files, WorkPlan.Defaults with { MaxEpicsPerEpoch = 4 });

        capThree.Errors.Should().ContainSingle().Which.Should().Match<WorkError>(error =>
            error.Code == WorkErrorCodes.TooManyEpics && error.Path == "epoch-1/epoch.yaml" && error.Message.Contains("4 epics") && error.Message.Contains("at most 3"));
        capFive.Valid.Should().BeTrue();
        capFour.Valid.Should().BeTrue("exactly at the cap is fine");
    }

    [Fact]
    public void TheStoryAndTaskCaps_AreTheDevicesToo()
    {
        var files = Base();
        foreach (var story in new[] { "s2-more", "s3-more" })
        {
            files[$"{WorkPlan.Epic}/stories/{story}/story.yaml"] = $"id: {story}\ntitle: X\ndependsOn: []\n";
            files[WorkPlan.TaskPath($"{WorkPlan.Epic}/stories/{story}", "t1-x")] = WorkPlan.Task("t1-x");
        }

        foreach (var task in new[] { "t2-more", "t3-more" })
        {
            files[WorkPlan.TaskPath(WorkPlan.Story, task)] = WorkPlan.Task(task);
        }

        Validate(files, WorkPlan.Defaults with { MaxStoriesPerEpic = 3, MaxTasksPerStory = 3 }).Valid.Should().BeTrue("exactly at both caps");
        var stories = Validate(files, WorkPlan.Defaults with { MaxStoriesPerEpic = 2 });
        var tasks = Validate(files, WorkPlan.Defaults with { MaxTasksPerStory = 2 });

        stories.Errors.Should().ContainSingle().Which.Should().Match<WorkError>(error => error.Code == WorkErrorCodes.TooManyStories && error.Message.Contains("3 stories") && error.Message.Contains("at most 2"));
        tasks.Errors.Should().ContainSingle().Which.Should().Match<WorkError>(error => error.Code == WorkErrorCodes.TooManyTasks && error.Node == "epoch-1/e1-core/s1-first");
    }

    [Fact]
    public void B3_TheMvpEpicsAreCountedFromTheFirst_AndTheMessageSaysTheRange()
    {
        var files = Base();
        files["epoch-1/epoch.yaml"] = "id: epoch-1\ntitle: F\ndependsOn: []\nepics:\n  - e1-core\n  - e2-b\n  - e3-c\n  - e4-d\n";
        files[$"{WorkPlan.Epic}/epic.yaml"] = "id: e1-core\ntitle: X\nmvp: true\ndependsOn: []\n";
        files["epoch-1/e2-b/epic.yaml"] = "id: e2-b\ntitle: X\nmvp: true\ndependsOn: []\n";
        files["epoch-1/e3-c/epic.yaml"] = "id: e3-c\ntitle: X\nmvp: true\ndependsOn: []\n";
        files["epoch-1/e4-d/epic.yaml"] = "id: e4-d\ntitle: X\ndependsOn: []\n";

        var report = Validate(files);

        report.Errors.Should().ContainSingle().Which.Should().Match<WorkError>(error =>
            error.Code == WorkErrorCodes.MvpCount && error.Message.Contains("3 leading MVP") && error.Message.Contains("1 to 2"));
    }

    [Fact]
    public void B3_AnMvpEpicAfterAGap_IsNamed()
    {
        var files = Base();
        files["epoch-1/epoch.yaml"] = "id: epoch-1\ntitle: F\ndependsOn: []\nepics:\n  - e1-core\n  - e2-b\n  - e3-c\n";
        files["epoch-1/e2-b/epic.yaml"] = "id: e2-b\ntitle: X\ndependsOn: []\n";
        files["epoch-1/e3-c/epic.yaml"] = "id: e3-c\ntitle: X\nmvp: true\ndependsOn: []\n";

        var error = Validate(files).Errors.Should().ContainSingle().Subject;

        (error.Code, error.Node, error.Path).Should().Be((WorkErrorCodes.MvpOrder, "epoch-1/e3-c", "epoch-1/e3-c/epic.yaml"));
    }

    [Fact]
    public void AnEpochWithFewerEpicsThanTheMvpMinimum_NeedsThemAllToBeMvp()
    {
        Validate(Base(), WorkPlan.Defaults with { MvpMin = 2, MvpMax = 3 }).Valid.Should().BeTrue("the only epic is the whole MVP");
    }

    // ---------- B4: folder = id ----------

    [Theory]
    [InlineData("epoch-1/epoch.yaml", "id: epoch-1", "id: epoch-9")]
    [InlineData(WorkPlan.Epic + "/epic.yaml", "id: e1-core", "id: e1-other")]
    [InlineData(WorkPlan.Story + "/story.yaml", "id: s1-first", "id: s1-other")]
    [InlineData(WorkPlan.TaskFile, "id: t1-one", "id: t1-other")]
    public void B4_AFolderThatIsNotItsId_IsNamedAtItsLevel(string file, string was, string becomes)
    {
        var files = Base();
        files[file] = files[file].Replace(was, becomes);

        var error = Validate(files).Errors.Should().ContainSingle().Subject;

        error.Code.Should().Be(WorkErrorCodes.FolderIdMismatch);
        error.Path.Should().Be(file);
        error.Message.Should().Contain("the folder name is the id");
    }

    [Fact]
    public void B4_TheFeatureIdIsFree()
    {
        var files = Base();
        files["feature.yaml"] = files["feature.yaml"].Replace("id: demo", "id: whatever it is called");

        Validate(files).Valid.Should().BeTrue();
    }

    [Theory]
    [InlineData("s1-../../../outside")]
    [InlineData("s1-a/b")]
    [InlineData("s1-a\\b")]
    [InlineData("s1-first\nsecond")]
    public void E1_AnIdThatCouldLeaveItsFolder_IsRefused_AtTheStoryLevel(string id)
    {
        var files = Base();
        files[WorkPlan.Story + "/story.yaml"] = $"id: \"{id.Replace("\\", "\\\\").Replace("\n", "\\n")}\"\ntitle: X\ndependsOn: []\n";

        var report = Validate(files);

        report.Errors.Select(error => error.Code).Should().Contain(WorkErrorCodes.BadId);
        report.Valid.Should().BeFalse();
    }

    // ---------- B5: dependencies ----------

    [Fact]
    public void B5_AnUnknownDependency_NamesTheEntryAndWhatWasLegal()
    {
        var files = Base();
        files[WorkPlan.TaskFile] = WorkPlan.Task("t1-one", dependsOn: "\n  - t9-nope");

        var error = Validate(files).Errors.Should().ContainSingle().Subject;

        error.Code.Should().Be(WorkErrorCodes.UnknownDependency);
        error.Node.Should().Be("epoch-1/e1-core/s1-first/t1-one");
        error.Message.Should().Contain("t9-nope").And.Contain("a task of this story");
    }

    [Fact]
    public void B5_ACycle_IsReportedOnceWithItsPath()
    {
        var files = Base();
        files[WorkPlan.TaskFile] = WorkPlan.Task("t1-one", dependsOn: "\n  - t3-three");
        files[WorkPlan.TaskPath(WorkPlan.Story, "t2-two")] = WorkPlan.Task("t2-two", dependsOn: "\n  - t1-one");
        files[WorkPlan.TaskPath(WorkPlan.Story, "t3-three")] = WorkPlan.Task("t3-three", dependsOn: "\n  - t2-two");

        var cycles = Validate(files).Errors.Where(error => error.Code == WorkErrorCodes.DependencyCycle).ToList();

        cycles.Should().ContainSingle().Which.Message.Should().Be("the dependencies go round: t1-one -> t3-three -> t2-two -> t1-one");
    }

    [Fact]
    public void B5_AStoryMayDependOnAStoryOfAnEpicItsEpicDependsOn_ButNotOfOneItDoesNot()
    {
        var files = Base();
        files["epoch-1/epoch.yaml"] = "id: epoch-1\ntitle: F\ndependsOn: []\nepics:\n  - e1-core\n  - e2-b\n  - e3-c\n";
        files["epoch-1/e2-b/epic.yaml"] = "id: e2-b\ntitle: X\ndependsOn:\n  - e1-core\n";
        files["epoch-1/e2-b/stories/s1-use/story.yaml"] = "id: s1-use\ntitle: X\ndependsOn:\n  - s1-first\n";
        files[WorkPlan.TaskPath("epoch-1/e2-b/stories/s1-use", "t1-x")] = WorkPlan.Task("t1-x");
        files["epoch-1/e3-c/epic.yaml"] = "id: e3-c\ntitle: X\ndependsOn: []\n";
        files["epoch-1/e3-c/stories/s1-use/story.yaml"] = "id: s1-use\ntitle: X\ndependsOn:\n  - s1-first\n";
        files[WorkPlan.TaskPath("epoch-1/e3-c/stories/s1-use", "t1-x")] = WorkPlan.Task("t1-x");

        var errors = Validate(files).Errors;

        errors.Should().ContainSingle().Which.Should().Match<WorkError>(error => error.Code == WorkErrorCodes.UnknownDependency && error.Node == "epoch-1/e3-c/s1-use");
    }

    [Fact]
    public void B5_AnEpicOfAnEpochThisEpochDependsOn_IsFound_AndAQualifiedStoryNameResolves()
    {
        var files = Base();
        files["feature.yaml"] = "id: demo\ntitle: D\nepochs:\n  - epoch-1\n  - epoch-2\n";
        files["epoch-2/epoch.yaml"] = "id: epoch-2\ntitle: F\ndependsOn:\n  - epoch-1\nepics:\n  - e1-next\n";
        files["epoch-2/e1-next/epic.yaml"] = "id: e1-next\ntitle: X\nmvp: true\ndependsOn:\n  - e1-core\n";
        files["epoch-2/e1-next/stories/s1-use/story.yaml"] = "id: s1-use\ntitle: X\ndependsOn:\n  - e1-core/s1-first\n";
        files[WorkPlan.TaskPath("epoch-2/e1-next/stories/s1-use", "t1-x")] = WorkPlan.Task("t1-x");

        var report = Validate(files);

        report.Valid.Should().BeTrue(string.Join("; ", report.Errors.Select(error => error.Message)));
        report.Counts.Should().Be(new WorkCounts(2, 2, 2, 2));
    }

    [Fact]
    public void B5_AnEpicThatDependsOnAnEpicOfAnEpochItDoesNotDependOn_IsUnknown()
    {
        var files = Base();
        files["feature.yaml"] = "id: demo\ntitle: D\nepochs:\n  - epoch-1\n  - epoch-2\n";
        files["epoch-2/epoch.yaml"] = "id: epoch-2\ntitle: F\ndependsOn: []\nepics:\n  - e1-next\n";
        files["epoch-2/e1-next/epic.yaml"] = "id: e1-next\ntitle: X\nmvp: true\ndependsOn:\n  - e1-core\n";

        Validate(files).Errors.Should().ContainSingle().Which.Code.Should().Be(WorkErrorCodes.UnknownDependency);
    }

    [Fact]
    public void B5_AnAmbiguousDependency_NamesBothMatches()
    {
        var files = Base();
        files["epoch-1/epoch.yaml"] = "id: epoch-1\ntitle: F\ndependsOn: []\nepics:\n  - e1-core\n  - e2-b\n  - e3-c\n";
        files["epoch-1/e2-b/epic.yaml"] = "id: e2-b\ntitle: X\ndependsOn: []\n";
        files["epoch-1/e2-b/stories/s1-first/story.yaml"] = "id: s1-first\ntitle: X\ndependsOn: []\n";
        files[WorkPlan.TaskPath("epoch-1/e2-b/stories/s1-first", "t1-x")] = WorkPlan.Task("t1-x");
        files["epoch-1/e3-c/epic.yaml"] = "id: e3-c\ntitle: X\ndependsOn:\n  - e1-core\n  - e2-b\n";
        files["epoch-1/e3-c/stories/s1-use/story.yaml"] = "id: s1-use\ntitle: X\ndependsOn:\n  - s1-first\n";
        files[WorkPlan.TaskPath("epoch-1/e3-c/stories/s1-use", "t1-x")] = WorkPlan.Task("t1-x");

        var error = Validate(files).Errors.Should().ContainSingle().Subject;

        error.Code.Should().Be(WorkErrorCodes.AmbiguousDependency);
        error.Message.Should().Contain("epoch-1/e1-core/s1-first").And.Contain("epoch-1/e2-b/s1-first");
    }

    [Fact]
    public void B5_ACrossEpicCycleOfStories_IsFound()
    {
        var files = Base();
        files["epoch-1/epoch.yaml"] = "id: epoch-1\ntitle: F\ndependsOn: []\nepics:\n  - e1-core\n  - e2-b\n";
        files[$"{WorkPlan.Epic}/epic.yaml"] = "id: e1-core\ntitle: X\nmvp: true\ndependsOn:\n  - e2-b\n";
        files[WorkPlan.Story + "/story.yaml"] = "id: s1-first\ntitle: X\ndependsOn:\n  - e2-b/s1-back\n";
        files["epoch-1/e2-b/epic.yaml"] = "id: e2-b\ntitle: X\ndependsOn:\n  - e1-core\n";
        files["epoch-1/e2-b/stories/s1-back/story.yaml"] = "id: s1-back\ntitle: X\ndependsOn:\n  - e1-core/s1-first\n";
        files[WorkPlan.TaskPath("epoch-1/e2-b/stories/s1-back", "t1-x")] = WorkPlan.Task("t1-x");

        Validate(files).Errors.Select(error => error.Code).Distinct().Should().BeEquivalentTo(WorkErrorCodes.DependencyCycle);
    }

    // ---------- B6: parallel ----------

    private static Dictionary<string, string> TwoParallel(string aFiles, string bFiles, string aDeps = "[]", string bDeps = "[]")
    {
        var files = Base();
        files[WorkPlan.TaskFile] = WorkPlan.Task("t1-one", dependsOn: aDeps, parallel: true, files: aFiles);
        files[WorkPlan.TaskPath(WorkPlan.Story, "t2-two")] = WorkPlan.Task("t2-two", dependsOn: bDeps, parallel: true, files: bFiles);

        return files;
    }

    [Theory]
    [InlineData("\n  - src/Roles", "\n  - src/Roles/A.cs")]
    [InlineData("\n  - src/a.cs", "\n  - SRC/A.CS")]
    [InlineData("\n  - src/Roles/", "\n  - src/Roles")]
    [InlineData("\n  - ./src/a", "\n  - src/a/b/c")]
    public void B6_ParallelTasksThatShareAFileOrAFolder_AreRefused_WithTheTwoEntries(string a, string b)
    {
        var error = Validate(TwoParallel(a, b)).Errors.Should().ContainSingle().Subject;

        error.Code.Should().Be(WorkErrorCodes.ParallelOverlap);
        error.Message.Should().Contain("t1-one").And.Contain("t2-two").And.Contain("overlap");
    }

    [Theory]
    [InlineData("\n  - src/Roles", "\n  - src/RolesTwo/A.cs")]
    [InlineData("\n  - src/a.cs", "\n  - src/a.cs.bak")]
    [InlineData("\n  - a", "\n  - b")]
    public void B6_ParallelTasksThatOnlyShareLetters_AreFine(string a, string b)
    {
        Validate(TwoParallel(a, b)).Valid.Should().BeTrue();
    }

    [Fact]
    public void B6_AGlobOrNoFiles_OnAParallelTask_IsRefused_ButNotOnASerialOne()
    {
        var glob = Validate(TwoParallel("\n  - src/*.cs", "\n  - tests/b"));
        var none = Validate(TwoParallel(" []", "\n  - tests/b"));

        glob.Errors.Should().ContainSingle().Which.Message.Should().Contain("glob");
        none.Errors.Should().ContainSingle().Which.Message.Should().Contain("must list its files");
        var serial = Base();
        serial[WorkPlan.TaskFile] = WorkPlan.Task("t1-one", files: " []");
        Validate(serial).Valid.Should().BeTrue();
    }

    [Fact]
    public void B6_ADependencyPath_ThroughASerialSibling_MakesParallelTasksRefused()
    {
        var files = TwoParallel("\n  - src/a", "\n  - src/b", bDeps: "\n  - t3-mid");
        files[WorkPlan.TaskPath(WorkPlan.Story, "t3-mid")] = WorkPlan.Task("t3-mid", dependsOn: "\n  - t1-one");

        Validate(files).Errors.Should().ContainSingle().Which.Code.Should().Be(WorkErrorCodes.ParallelDependent);
    }

    // ---------- B7, B8: dod and types ----------

    [Fact]
    public void B7_ADodThatIsEmptyOrRepeatsAnId_IsRefused_ByTask()
    {
        var empty = Base();
        empty[WorkPlan.TaskFile] = WorkPlan.Task("t1-one").Replace("dod:\n  - id: d1\n    item: it works\n", "dod: []\n");
        var twice = Base();
        twice[WorkPlan.TaskFile] = WorkPlan.Task("t1-one").Replace("it works\n", "it works\n  - id: d1\n    item: again\n");

        Validate(empty).Errors.Should().ContainSingle().Which.Code.Should().Be(WorkErrorCodes.EmptyDod);
        Validate(twice).Errors.Should().ContainSingle().Which.Message.Should().Contain("'d1'");
    }

    [Theory]
    [InlineData("complexity: 3", "complexity: \"5\"", WorkErrorCodes.WrongType)]
    [InlineData("complexity: 3", "complexity: 11", WorkErrorCodes.BadValue)]
    [InlineData("complexity: 3", "complexity: -1", WorkErrorCodes.BadValue)]
    [InlineData("parallel: false", "parallel: yes", WorkErrorCodes.WrongType)]
    [InlineData("parallel: false", "parallel: \"false\"", WorkErrorCodes.WrongType)]
    [InlineData("dod:\n", "secuirty: true\ndod:\n", WorkErrorCodes.UnknownKey)]
    [InlineData("kind: code", "kind: magic", WorkErrorCodes.BadValue)]
    [InlineData("kind: code\n", "", WorkErrorCodes.MissingKey)]
    public void B8_ATypoOrTheWrongType_IsRefused_WithItsLine(string was, string becomes, string code)
    {
        var files = Base();
        files[WorkPlan.TaskFile] = files[WorkPlan.TaskFile].Replace(was, becomes);

        var error = Validate(files).Errors.Should().ContainSingle().Subject;

        error.Code.Should().Be(code);
        error.Path.Should().Be(WorkPlan.TaskFile);
        if (code != WorkErrorCodes.MissingKey)
        {
            error.Message.Should().MatchRegex("line \\d+");
        }
    }

    [Fact]
    public void AFileWithAFault_IsReadNoFurther_SoOneMistakeIsOneError()
    {
        var files = Base();
        files[WorkPlan.TaskFile] = "id: t1-one\ntitle: X\nkind: magic\ncomplexity: \"5\"\ndependsOn:\n  - t9\nparallel: yes\nfiles: []\ndod: []\n";

        var report = Validate(files);

        report.Errors.Select(error => error.Code).Distinct().Should().BeEquivalentTo(WorkErrorCodes.BadValue, WorkErrorCodes.WrongType);
        report.Errors.Should().OnlyContain(error => error.Path == WorkPlan.TaskFile);
    }

    // ---------- B9: important, and the notes ----------

    [Fact]
    public void B9_SecurityKindAndComplexityTen_AreImportant_WithTheirReason_AndNineIsNot()
    {
        var files = Base();
        files[WorkPlan.TaskFile] = WorkPlan.Task("t1-one", extra: "security: true\n");
        files[WorkPlan.TaskPath(WorkPlan.Story, "t2-two")] = WorkPlan.Task("t2-two", kind: "security");
        files[WorkPlan.TaskPath(WorkPlan.Story, "t3-three")] = WorkPlan.Task("t3-three", complexity: 10);
        files[WorkPlan.TaskPath(WorkPlan.Story, "t4-four")] = WorkPlan.Task("t4-four", complexity: 9);
        files[WorkPlan.TaskPath(WorkPlan.Story, "t5-five")] = WorkPlan.Task("t5-five", extra: "security: false\n");

        var report = Validate(files);

        report.Valid.Should().BeTrue();
        report.Important.Select(item => (item.Node.Split('/')[^1], item.Reason)).Should().Equal(
            ("t1-one", "security: true"), ("t2-two", "kind: security"), ("t3-three", "complexity 10"));
    }

    [Fact]
    public void B9_ASecurityKindWithSecurityFalse_IsAContradiction_AndStillImportant()
    {
        var files = Base();
        files[WorkPlan.TaskFile] = WorkPlan.Task("t1-one", kind: "security", extra: "security: false\n");

        var report = Validate(files);

        report.Errors.Should().ContainSingle().Which.Code.Should().Be(WorkErrorCodes.SecurityContradiction);
        report.Important.Should().ContainSingle();
    }

    [Fact]
    public void TheImportantRule_IsOneFunction_ForTheRouterAndTheValidator()
    {
        ImportantRule.Reason("code", 3, securityFlag: false).Should().BeEmpty();
        ImportantRule.Reason("code", 9, securityFlag: false).Should().BeEmpty();
        ImportantRule.Reason("code", 10, securityFlag: false).Should().Be("complexity 10");
        ImportantRule.Reason("security", 0, securityFlag: false).Should().Be("kind: security");
        ImportantRule.Reason("docs", 0, securityFlag: true).Should().Be("security: true");
    }

    [Fact]
    public void ANoteIsMade_WhenTheFeatureMethodDiffers_AndItNeverFailsThePlan()
    {
        var files = Base();
        files["feature.yaml"] = "id: demo\ntitle: D\nworkMethod:\n  mvpEpics:\n    min: 1\n    max: 2\n  maxEpicsPerEpoch: 9\n  complexityScale: \"0-10\"\nepochs:\n  - epoch-1\n";

        var report = Validate(files);

        report.Valid.Should().BeTrue();
        report.Notes.Should().ContainSingle().Which.Should().Contain("maxEpicsPerEpoch is 9").And.Contain("limit is 5");
    }

    [Fact]
    public void ACredentialWord_InATaskText_IsANote_UnlessTheTaskIsSecurityTrue()
    {
        var plain = Base();
        plain[WorkPlan.TaskFile] = WorkPlan.Task("t1-one").Replace("title: Task t1-one", "title: Rotate the vendor token");
        var flagged = Base();
        flagged[WorkPlan.TaskFile] = plain[WorkPlan.TaskFile] + "security: true\n";

        var report = Validate(plain);

        report.Valid.Should().BeTrue();
        report.Notes.Should().ContainSingle().Which.Should().Contain("token").And.Contain("no security: true");
        Validate(flagged).Notes.Should().BeEmpty();
    }

    // ---------- limits, order, caps ----------

    [Fact]
    public void ManyErrors_AreSortedByPathAndCapped_WithTruncatedSet()
    {
        var files = Base();
        for (var t = 2; t <= 260; t++)
        {
            files[WorkPlan.TaskPath(WorkPlan.Story, $"t{t}-x")] = WorkPlan.Task($"t{t}-x", complexity: 11);
        }

        var report = Validate(files, WorkPlan.Defaults with { MaxTasksPerStory = 1000 });

        report.Errors.Should().HaveCount(WorkReport.MaxListed);
        report.Truncated.Should().BeTrue();
        report.Errors.Select(error => error.Path).Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void NoLimitOfTheWorkMethod_IsATypedConstant_InTheValidatorSources()
    {
        var folder = Path.Combine(RepoPaths.Root(), "src_mcp", "core", "Work");
        // The work pack (epoch 3 E3 s3) is not the validator: its constants are byte sizes of a pack and a fence, and never a limit of the work method.
        var files = Directory.EnumerateFiles(folder, "*.cs").Where(file => !Path.GetFileName(file).StartsWith("WorkPack", StringComparison.Ordinal)).ToList();
        var declaring = files.Where(file => File.ReadAllText(file).Contains("const int", StringComparison.Ordinal)).Select(Path.GetFileName).Order().ToList();

        files.Count.Should().BeGreaterThan(8, "the walk found the sources");
        declaring.Should().Equal(
            ["ImportantRule.cs", "StrictYamlReader.cs", "WorkReport.cs", "WorkTables.cs"],
            "the only integer constants are the important complexity, the YAML cap, the answer's list cap and the complexity range — never an epics, stories or tasks limit");
        File.ReadAllText(Path.Combine(folder, "WorkStructureRules.cs")).Should().NotMatchRegex(@"[<>]=? *([2-9]|[1-9][0-9])");
    }

    [Fact]
    public void TheSamePlan_GetsTheSameReport_Twice()
    {
        var files = Base();
        files[WorkPlan.TaskFile] = WorkPlan.Task("t1-one", complexity: 11);

        Validate(files).Should().BeEquivalentTo(Validate(files));
    }
}

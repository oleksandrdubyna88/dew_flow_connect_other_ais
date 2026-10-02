using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The <c>COAI_QCONSULT_*</c> keys (PLAN_question_consultant.md, §4) read into their own record, and D14 (c): a disk
/// root that is a drive, the profile, a system directory, inside the data directory, relative or missing is refused.
/// </summary>
public sealed class QuestionConsultSettingsTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-qsettings-data-").FullName;
    private readonly string _projects = Directory.CreateTempSubdirectory("coai-qsettings-projects-").FullName;
    private readonly string _profile = Directory.CreateTempSubdirectory("coai-qsettings-profile-").FullName;
    private readonly string _system = Directory.CreateTempSubdirectory("coai-qsettings-system-").FullName;

    private SystemPlaces Places => new(_profile, [_system]);

    public void Dispose()
    {
        foreach (var dir in (string[])[_data, _projects, _profile, _system])
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException) { }
        }
    }

    private static Func<string, string?> Env(params (string Key, string Value)[] pairs) =>
        name => pairs.FirstOrDefault(p => p.Key == name).Value;

    [Fact]
    public void TheDefaults_AreTheOperatorsAccepted_AndPanelSettingsCarriesThem()
    {
        var read = QuestionConsultReader.Read(Env(), _data, Places).Settings;

        read.Should().BeEquivalentTo(QuestionConsultSettings.Default);
        read.Enabled.Should().BeTrue();
        read.Mode.Should().Be("require");
        read.Rows.Should().BeEmpty();
        read.Prompts.Prompts.Should().HaveCount(3, "the shipped three");
        read.Roots.Should().BeEmpty();
        read.RowBudget.Should().Be(TimeSpan.FromMinutes(5));
        read.QuestionsPerSession.Should().Be(10);
        read.FreeBatches.Should().Be(2);
        PanelSettings.FromEnvironment(Env()).QuestionConsult.Should().BeEquivalentTo(QuestionConsultSettings.Default,
            "one property on PanelSettings, the record's own defaults");
    }

    [Fact]
    public void EveryKey_IsRead_AndAComplaintCarriesItsKey()
    {
        var env = Env(
            (QuestionConsultKeys.Enabled, "false"),
            (QuestionConsultKeys.Mode, "Remind"),
            (QuestionConsultKeys.Rows, """[{"id":"a","vendor":"claude","runtime":"claude","prompt":"question-disk"}]"""),
            (QuestionConsultKeys.Prompts, """[{"id":"own","title":"Own","capability":"none","text":"t"}]"""),
            (QuestionConsultKeys.Roots, $"[\"{_projects.Replace('\\', '/')}\"]"),
            (QuestionConsultKeys.RowMinutes, "7"),
            (QuestionConsultKeys.QuestionsPerSession, "3"),
            (QuestionConsultKeys.FreeBatches, "4"));

        var read = QuestionConsultReader.Read(env, _data, Places);

        read.Complaints.Should().BeEmpty();
        read.Settings.Enabled.Should().BeFalse();
        read.Settings.Mode.Should().Be("remind", "read without case");
        read.Settings.Rows.Should().ContainSingle().Which.Id.Should().Be("a");
        read.Settings.Prompts.Find("own").Should().NotBeNull();
        read.Settings.Roots.Should().ContainSingle().Which.Should().Be(Path.GetFullPath(_projects), "a root is kept as its full path");
        read.Settings.RowBudget.Should().Be(TimeSpan.FromMinutes(7));
        read.Settings.QuestionsPerSession.Should().Be(3);
        read.Settings.FreeBatches.Should().Be(4);
        var panel = PanelSettings.FromEnvironment(env);
        panel.QuestionConsult.Mode.Should().Be("remind", "the whole reader rides into PanelSettings");
        panel.Unrecognised.Should().BeEmpty();
    }

    [Fact]
    public void AnUnknownMode_IsRequire_AndSaidUnderItsKey()
    {
        var read = QuestionConsultReader.Read(Env((QuestionConsultKeys.Mode, "sometimes")), _data, Places);

        read.Settings.Mode.Should().Be("require");
        var complaint = read.Complaints.Should().ContainSingle().Subject;
        complaint.Key.Should().Be(QuestionConsultKeys.Mode);
        complaint.Sentence.Should().Contain("'sometimes'").And.Contain("'off', 'remind' and 'require'");
    }

    [Fact]
    public void RowsThatCannotBeRead_AreUnreadable_AndTheComplaintIsUnderTheirKey()
    {
        var read = QuestionConsultReader.Read(Env((QuestionConsultKeys.Rows, "[{")), _data, Places);

        read.Settings.RowsUnreadable.Should().BeTrue();
        read.Complaints.Should().ContainSingle().Which.Key.Should().Be(QuestionConsultKeys.Rows);
        PanelSettings.FromEnvironment(Env((QuestionConsultKeys.Rows, "[{"))).Unrecognised.Should().ContainSingle().Which.Should().Contain("COAI_QCONSULT_ROWS");
    }

    [Fact]
    public void ZeroAndNonsense_FallBackForTheThreeNumbers()
    {
        var read = QuestionConsultReader.Read(Env(
            (QuestionConsultKeys.RowMinutes, "0"), (QuestionConsultKeys.QuestionsPerSession, "lots"), (QuestionConsultKeys.FreeBatches, "-1")), _data, Places).Settings;

        read.RowBudget.Should().Be(TimeSpan.FromMinutes(5));
        read.QuestionsPerSession.Should().Be(10);
        read.FreeBatches.Should().Be(2);
    }

    // ---------- D14 (c): the roots ----------

    [Fact]
    public void AProjectFolderThatExists_IsAccepted_Once()
    {
        var verdict = QuestionRoots.Validate([_projects, _projects.Replace('\\', '/') + "/"], _data, Places, Directory.Exists);

        verdict.Refused.Should().BeEmpty();
        verdict.Accepted.Should().ContainSingle("two spellings of one folder are one root").Which.Should().Be(Path.GetFullPath(_projects));
    }

    [Fact]
    public void ADriveRoot_IsRefused_TheWholeDisk()
    {
        var drive = Path.GetPathRoot(_projects)!;

        var verdict = QuestionRoots.Validate([drive], _data, Places, Directory.Exists);

        verdict.Accepted.Should().BeEmpty();
        verdict.Refused.Should().ContainSingle().Which.Should().Contain("drive root").And.Contain("whole disk").And.Contain(QuestionConsultKeys.Roots);
    }

    [Fact]
    public void TheUserProfileItself_IsRefused_ButAProjectUnderItIsNot()
    {
        var project = Directory.CreateDirectory(Path.Combine(_profile, "src", "alpha")).FullName;

        var verdict = QuestionRoots.Validate([_profile, project], _data, Places, Directory.Exists);

        verdict.Refused.Should().ContainSingle().Which.Should().Contain("user profile directory itself");
        verdict.Accepted.Should().ContainSingle().Which.Should().Be(project);
    }

    [Fact]
    public void ASystemDirectory_OrAnythingInsideOne_IsRefused()
    {
        var inside = Directory.CreateDirectory(Path.Combine(_system, "Microsoft", "Something")).FullName;

        var verdict = QuestionRoots.Validate([_system, inside], _data, Places, Directory.Exists);

        verdict.Accepted.Should().BeEmpty();
        verdict.Refused.Should().HaveCount(2).And.OnlyContain(r => r.Contains("system directory"));
    }

    [Fact]
    public void AnythingInsideTheDataDirectory_IsRefused()
    {
        var inside = Directory.CreateDirectory(Path.Combine(_data, "question-consults")).FullName;

        var verdict = QuestionRoots.Validate([inside, _data], _data, Places, Directory.Exists);

        verdict.Accepted.Should().BeEmpty();
        verdict.Refused.Should().HaveCount(2).And.OnlyContain(r => r.Contains("inside the data directory"));
    }

    [Fact]
    public void ARelativeOrMissingRoot_IsRefusedByName()
    {
        var missing = Path.Combine(_projects, "gone");

        var verdict = QuestionRoots.Validate(["src/alpha", "../up", missing], _data, Places, Directory.Exists);

        verdict.Accepted.Should().BeEmpty();
        verdict.Refused.Should().HaveCount(3);
        verdict.Refused[0].Should().Contain("'src/alpha'").And.Contain("not an absolute path");
        verdict.Refused[1].Should().Contain("'../up'");
        verdict.Refused[2].Should().Contain("not a directory on this machine");
    }

    // ---------- S4b item 2: ancestors, links, credential directories ----------

    /// <summary>A nested layout under one temp folder: <c>home/me</c> is the profile, <c>local/coai</c> the data, <c>win/sys</c> a system directory.</summary>
    private sealed class Layout : IDisposable
    {
        public string Base { get; } = Directory.CreateTempSubdirectory("coai-qroots-").FullName;

        public string Home => Path.Combine(Base, "home");

        public string Profile => Directory.CreateDirectory(Path.Combine(Home, "me")).FullName;

        public string Data => Directory.CreateDirectory(Path.Combine(Base, "local", "coai")).FullName;

        public string System => Directory.CreateDirectory(Path.Combine(Base, "win", "sys")).FullName;

        public SystemPlaces Places => new(Profile, [System]);

        public string Under(params string[] parts) => Directory.CreateDirectory(Path.Combine([Base, .. parts])).FullName;

        /// <summary>The links a test made — removed FIRST, as links, so the recursive delete never walks through one.</summary>
        public List<string> Links { get; } = [];

        public void Dispose()
        {
            foreach (var link in Links)
            {
                try
                {
                    Directory.Delete(link);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }

            try
            {
                Directory.Delete(Base, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void ARootThatContainsTheProfileTheDataDirectoryOrASystemDirectory_IsRefused()
    {
        using var at = new Layout();
        _ = (at.Profile, at.Data, at.System);

        var verdict = QuestionRoots.Validate([at.Home, Path.Combine(at.Base, "local"), Path.Combine(at.Base, "win"), at.Base], at.Data, at.Places, Directory.Exists);

        verdict.Accepted.Should().BeEmpty("each of the four CONTAINS a place a disk row may not read — reading its parent reads it too");
        verdict.Refused.Should().HaveCount(4);
        verdict.Refused[0].Should().Contain("contains the user profile directory");
        verdict.Refused[1].Should().Contain("contains the data directory");
        verdict.Refused[2].Should().Contain("contains a system directory");
        at.Under("projects", "alpha");
        QuestionRoots.Validate([Path.Combine(at.Base, "projects")], at.Data, at.Places, Directory.Exists).Accepted
            .Should().ContainSingle("a sibling of the places is the ordinary case");
    }

    [Theory]
    [InlineData(".ssh")]
    [InlineData(".aws")]
    [InlineData(".gnupg")]
    [InlineData(".claude")]
    [InlineData(".codex")]
    [InlineData(".azure")]
    [InlineData(".config/gcloud")]
    public void ACredentialDirectory_OrAnythingInsideOne_IsRefused(string credentials)
    {
        using var at = new Layout();
        var dir = Directory.CreateDirectory(Path.Combine(at.Profile, credentials)).FullName;
        var inside = Directory.CreateDirectory(Path.Combine(dir, "nested")).FullName;

        var verdict = QuestionRoots.Validate([dir, inside], at.Data, at.Places, Directory.Exists);

        verdict.Accepted.Should().BeEmpty($"{credentials} holds a vendor's or the machine's credentials");
        verdict.Refused.Should().HaveCount(2).And.OnlyContain(r => r.Contains("credential"));
    }

    [Fact]
    public void ACredentialDirectoryByName_IsRefusedOutsideTheProfileToo_AndAFolderHoldingOne_IsRefused()
    {
        using var at = new Layout();
        var copied = at.Under("backup", ".ssh");
        var config = Directory.CreateDirectory(Path.Combine(at.Profile, ".config", "gcloud")).FullName;

        var verdict = QuestionRoots.Validate([copied, Path.GetDirectoryName(config)!], at.Data, at.Places, Directory.Exists);

        verdict.Accepted.Should().BeEmpty("a .ssh copied elsewhere is still keys, and ~/.config holds gcloud's");
        verdict.Refused.Should().HaveCount(2).And.OnlyContain(r => r.Contains("credential"));
    }

    [Fact]
    public void ARootReachedThroughALink_IsJudgedAtItsTarget()
    {
        using var at = new Layout();
        var straight = Link(at, Path.Combine(at.Base, "to-profile"), at.Profile);
        var through = Path.Combine(Link(at, Path.Combine(at.Base, "to-home"), at.Home), "me");
        var toKeys = Link(at, Path.Combine(at.Base, "to-keys"), Directory.CreateDirectory(Path.Combine(at.Profile, ".ssh")).FullName);

        var verdict = QuestionRoots.Validate([straight, through, toKeys], at.Data, at.Places, Directory.Exists);

        verdict.Accepted.Should().BeEmpty("a junction or a symlink to the profile, or to a folder above or inside a refused place, is that place");
        verdict.Refused.Should().HaveCount(3);
        verdict.Refused[0].Should().Contain("user profile directory itself");
        verdict.Refused[1].Should().Contain("user profile directory itself", "a link in the MIDDLE of the path moves the rest of it");
        verdict.Refused[2].Should().Contain("credential");
    }

    /// <summary>A directory link without a privilege: a junction on Windows (<c>mklink /J</c>), a symlink elsewhere.</summary>
    private static string Link(Layout at, string link, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            using var made = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", link, target])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            made.WaitForExit(10_000).Should().BeTrue("mklink answers at once");
            made.ExitCode.Should().Be(0, made.StandardError.ReadToEnd());
        }
        else
        {
            Directory.CreateSymbolicLink(link, target);
        }

        Directory.Exists(link).Should().BeTrue("the link was made");
        at.Links.Add(link);

        return link;
    }

    [Fact]
    public void TheRefusedRoots_ReachPanelSettingsAsComplaints_AndNeverTheRootsList()
    {
        var drive = Path.GetPathRoot(_projects)!;
        var env = Env((QuestionConsultKeys.Roots, $"[\"{drive.Replace('\\', '/')}\", \"{_projects.Replace('\\', '/')}\"]"));

        var read = QuestionConsultReader.Read(env, _data, Places);

        read.Settings.Roots.Should().ContainSingle().Which.Should().Be(Path.GetFullPath(_projects));
        read.Complaints.Should().ContainSingle().Which.Key.Should().Be(QuestionConsultKeys.Roots);
    }

    [Fact]
    public void RootsMayAlsoBeSemicolonSeparated_ForAHandSetVariable()
    {
        var other = Directory.CreateDirectory(Path.Combine(_projects, "beta")).FullName;

        var read = QuestionConsultReader.Read(Env((QuestionConsultKeys.Roots, $"{_projects};{other}")), _data, Places).Settings;

        read.Roots.Should().Equal(Path.GetFullPath(_projects), other);
    }
}

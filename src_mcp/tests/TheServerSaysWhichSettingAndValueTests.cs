using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The server says WHICH setting and WHICH value a refusal is about (research/PLAN_paths_per_side.md E5.2), so the Settings
/// page can mark the folder rather than relay a sentence: a missing question-consultant root of this OS travels in
/// <c>--providers</c>' <c>settingProblems</c> as <c>(key, kind, value, sentence)</c>, beside the sentence
/// <c>unrecognised</c> has always carried for an older extension.
/// </summary>
/// <remarks>
/// The operator's case (2026-10-09): a deleted Windows root <c>D:\old-project</c> was one free-text sentence in
/// <c>unrecognised</c>, parsed into a generic toast whose cure — "the server is running without those settings" — was
/// wrong for a list with one folder dropped, and the page could tie nothing to the folder because the KEY and the
/// VALUE were lost on the wire.
/// </remarks>
public sealed class TheServerSaysWhichSettingAndValueTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-problems-data-").FullName;
    private readonly string _projects = Directory.CreateTempSubdirectory("coai-problems-projects-").FullName;
    private readonly string _profile = Directory.CreateTempSubdirectory("coai-problems-profile-").FullName;
    private readonly string _system = Directory.CreateTempSubdirectory("coai-problems-system-").FullName;

    private SystemPlaces Places => new(_profile, [_system]);

    /// <summary>A folder of THIS OS that is gone — spelled as the settings file spells it (forward slashes, as the panel writes JSON).</summary>
    private string Gone => Path.Combine(_projects, "gone").Replace('\\', '/');

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

    private Func<string, string?> RootsEnv() => Env(
        ("COAI_DATA_DIR", _data),
        (QuestionConsultKeys.Roots, $"[\"{_projects.Replace('\\', '/')}\", \"{Gone}\"]"));

    [Fact]
    public void AMissingRootOfThisOs_IsASettingProblem_NamingItsKeyItsKindAndTheStoredValue()
    {
        var read = QuestionConsultReader.Read(RootsEnv(), _data, Places);

        var complaint = read.Complaints.Should().ContainSingle("the existing folder is accepted; only the gone one is refused").Subject;
        complaint.Key.Should().Be(QuestionConsultKeys.Roots);
        complaint.Kind.Should().Be(SettingProblemKinds.MissingFolder, "--providers with a missing root carries no settingProblems today: the kind is lost on the wire");
        complaint.Value.Should().Be(Gone, "the STORED spelling, which is what the page holds and matches on — never the qualified path");
        complaint.Sentence.Should().Contain("is not a directory on this machine", "the sentence an older extension keeps showing");
    }

    [Fact]
    public void ARootTheDiskCouldNotAnswerFor_IsAProblemOfItsOwnKind_NeverMissing()
    {
        var verdict = QuestionRoots.Validate([_projects], _data, Places, _ => RootPresence.Unknown);

        var refusal = verdict.Refusals.Should().ContainSingle().Subject;
        refusal.Kind.Should().Be(SettingProblemKinds.UnreadableFolder, "EACCES or a disk that would not answer is not an absence — the page marks it in the warning tone");
        refusal.Value.Should().Be(_projects);
        verdict.Refused.Should().Equal([refusal.Sentence], "the sentence projection stays what it always was");
    }

    [Fact]
    public void ARootRefusedForWhereItIs_HasNoKind_SoItStaysASentenceAlone()
    {
        var drive = Path.GetPathRoot(_projects)!;

        var verdict = QuestionRoots.Validate([drive], _data, Places, Directory.Exists);

        var refusal = verdict.Refusals.Should().ContainSingle().Subject;
        refusal.Kind.Should().BeEmpty("a drive root is refused by NAME, not for being missing — a mark saying 'missing' there would lie");
        refusal.Sentence.Should().Contain("drive root");
    }

    [Fact]
    public void TheProvidersAnswer_CarriesTheProblems_AsTheExtensionReadsThem_AndListsTheCapability()
    {
        var settings = PanelSettings.FromEnvironment(RootsEnv());
        var answer = new ProvidersAnswer([], "never", "no vault", settings.Unrecognised) { SettingProblems = settings.SettingProblems };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(answer, ServerJsonContext.Default.ProvidersAnswer));
        var problems = json.RootElement.GetProperty("settingProblems").EnumerateArray().ToList();

        var problem = problems.Should().ContainSingle("one missing folder is one problem").Subject;
        problem.GetProperty("key").GetString().Should().Be(QuestionConsultKeys.Roots);
        problem.GetProperty("kind").GetString().Should().Be(SettingProblemKinds.MissingFolder);
        problem.GetProperty("value").GetString().Should().Be(Gone);
        problem.GetProperty("sentence").GetString().Should().Be(settings.Unrecognised.Single(), "the SAME sentence, so the extension can drop it from the generic toast");
        json.RootElement.GetProperty("unrecognised").EnumerateArray().Should().ContainSingle("an older extension still gets its sentence");
        FeaturesMode.Listed.Should().Contain("settingProblems", "the page prefers the server's answer only from a server that lists the capability");
    }

    [Fact]
    public void AnUnrecognisedSettingWithoutAKind_ReadsAsItAlwaysDid_AndIsNoProblemOnTheWire()
    {
        var settings = new PanelSettings { UnrecognisedSettings = [new UnrecognisedSetting("COAI_RETRY_BACKOFF", "cannot read that as seconds")] };

        settings.UnrecognisedSettings.Single().Kind.Should().BeEmpty();
        settings.UnrecognisedSettings.Single().Value.Should().BeEmpty();
        settings.SettingProblems.Should().BeEmpty("a sentence-only refusal has nothing the page can mark");
        settings.Unrecognised.Should().ContainSingle();
    }
}

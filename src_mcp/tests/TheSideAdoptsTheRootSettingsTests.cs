using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// An installation that was already partitioned finds its settings where it left them.
/// </summary>
/// <remarks>
/// <para>Before the side reached <c>settings.json</c>, both sides of a partitioned installation read
/// <c>&lt;root&gt;/settings.json</c>. Moving the file under the side without adopting what is there
/// would start every such installation on DEFAULTS — a person's vendors, keys and round budgets
/// silently gone, at the moment they upgrade.</para>
/// <para>The plan round named three ways to get the adoption itself wrong, and every one of them is
/// a case below: moving the root file strands the second side, publishing a copy directly leaves a
/// partial file that blocks adoption for ever after a kill, and two sides starting at once on a NAS
/// race with no lock.</para>
/// </remarks>
public sealed class TheSideAdoptsTheRootSettingsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("coai-adopt-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException) { }
    }

    private Func<string, string?> Env(string? side) => name => name switch
    {
        "COAI_DATA_DIR" => _root,
        "COAI_DATA_SIDE" => side,
        _ => null,
    };

    private string WriteRoot(string json)
    {
        var path = Path.Combine(_root, SettingsFile.Name);
        File.WriteAllText(path, json);

        return path;
    }

    private string SidePath(string side) => Path.Combine(_root, side, SettingsFile.Name);

    [Fact]
    public void ASideWithNoFileOfItsOwn_AdoptsTheRootsAndSaysSo()
    {
        WriteRoot("""{"COAI_VENDORS":"codex,gemini"}""");

        var said = SettingsFile.AdoptRootSettings(Env("alpha"));

        File.Exists(SidePath("alpha")).Should().BeTrue(
            "an installation that was already partitioned would otherwise start on defaults, with a "
            + "person's vendors and keys silently gone at the moment they upgrade");
        SettingsFile.Read(SidePath("alpha")).Should().ContainKey("COAI_VENDORS");
        said.Should().ContainSingle().Which.Should().Contain(SidePath("alpha"),
            "an adoption nobody is told about is the silent default this exists to prevent");
    }

    [Fact]
    public void AnyReadAdoptsFirst_SoProvidersRunBeforeAnyStartupSeesTheSameSettings()
    {
        // The seam the plan round found and the consultation named the check for: `--providers` can
        // be the FIRST thing that ever runs against a legacy root-only installation. If adoption
        // lived in the server's startup path, it would read an absent side file and report defaults
        // for an installation whose configuration exists - and then a normal start would adopt, so
        // the two would disagree about the same machine.
        //
        // Both roads are the same road here: whatever reads the settings adopts first.
        WriteRoot("""{"COAI_VENDORS":"codex,gemini"}""");
        var env = Env("alpha");

        var providersFirst = SettingsFile.Layer(SettingsFile.DataDirFrom(env), env);

        providersFirst("COAI_VENDORS").Should().Be("codex,gemini",
            "the one-shot mode reported defaults for an installation that has configuration, and a "
            + "normal start afterwards would have answered differently about the same machine");

        var normalStart = SettingsFile.Layer(SettingsFile.DataDirFrom(env), env);

        normalStart("COAI_VENDORS").Should().Be(providersFirst("COAI_VENDORS"),
            "two roads to the same settings must not answer differently");
    }

    [Fact]
    public void TheRootFile_SurvivesAdoption_SoASecondSideFindsItToo()
    {
        // The first way to get it wrong. Moving the file leaves side B starting next, finding
        // neither its own nor the root's, and reverting to defaults in silence.
        var root = WriteRoot("""{"COAI_VENDORS":"codex"}""");

        SettingsFile.AdoptRootSettings(Env("alpha"));

        File.Exists(root).Should().BeTrue("side B has not started yet and it reads this file");

        SettingsFile.AdoptRootSettings(Env("beta"));

        SettingsFile.Read(SidePath("beta")).Should().ContainKey("COAI_VENDORS",
            "the second side to start was handed defaults, which is the first side's adoption "
            + "deleting the other's configuration");
    }

    [Fact]
    public void ASideThatAlreadyHasOne_IsLeftEntirelyAlone()
    {
        WriteRoot("""{"COAI_VENDORS":"from-the-root"}""");
        Directory.CreateDirectory(Path.Combine(_root, "alpha"));
        File.WriteAllText(SidePath("alpha"), """{"COAI_VENDORS":"its-own"}""");

        var said = SettingsFile.AdoptRootSettings(Env("alpha"));

        SettingsFile.Read(SidePath("alpha"))["COAI_VENDORS"].Should().Be("its-own",
            "the steady state is that nothing happens, and overwriting a side's own settings with a "
            + "legacy file would be the worst outcome of the three");
        said.Should().BeEmpty("nothing happened, so there is nothing to tell anybody");
    }

    [Fact]
    public void NoSideAsked_MeansNothingToAdopt()
    {
        WriteRoot("""{"COAI_VENDORS":"codex"}""");

        SettingsFile.AdoptRootSettings(Env(null)).Should().BeEmpty();
        Directory.EnumerateDirectories(_root).Should().BeEmpty(
            "an unpartitioned installation has one settings file and this must not invent a second");
    }

    [Fact]
    public void NoRootFile_MeansAGenuinelyNewInstallation()
    {
        SettingsFile.AdoptRootSettings(Env("alpha")).Should().BeEmpty();
        File.Exists(SidePath("alpha")).Should().BeFalse(
            "there was nothing to adopt, and writing an empty file would make the next start think "
            + "this side had been configured");
    }

    [Fact]
    public void AKillBeforePublication_LeavesAdoptionStillPossible()
    {
        // The second way to get it wrong. A copy published directly can be interrupted half-written;
        // the existence check then skips adoption for ever, and an intact root configuration never
        // arrives. Modelled by the leftover a killed writer leaves: a temporary sibling.
        WriteRoot("""{"COAI_VENDORS":"codex"}""");
        Directory.CreateDirectory(Path.Combine(_root, "alpha"));
        File.WriteAllText(Path.Combine(_root, "alpha", SettingsFile.Name + ".9999.tmp"), "{ half");

        SettingsFile.AdoptRootSettings(Env("alpha"));

        SettingsFile.Read(SidePath("alpha")).Should().ContainKey("COAI_VENDORS",
            "a leftover from a killed run stood between this installation and its own configuration");
    }

    [Fact]
    public void ARootFileThatDoesNotParse_IsNamedAndNotAdopted()
    {
        // Publishing an unreadable file to a second location is copying a defect into a second place,
        // and the side would then be permanently "configured" by something nothing can read.
        WriteRoot("{ this is not json");

        var said = SettingsFile.AdoptRootSettings(Env("alpha"));

        File.Exists(SidePath("alpha")).Should().BeFalse();
        said.Should().ContainSingle().Which.Should().Contain(SettingsFile.Name,
            "a root file that cannot be read is exactly what somebody needs to be told about");
    }

    [Fact]
    public void ASecondAdopterFindsItPublished_AndNeitherFileIsLost()
    {
        // The third way. Two sides starting at once on a NAS; here the destination appears between
        // one adopter's check and its publication, which is what the race looks like from inside.
        WriteRoot("""{"COAI_VENDORS":"codex"}""");
        Directory.CreateDirectory(Path.Combine(_root, "alpha"));
        File.WriteAllText(SidePath("alpha"), """{"COAI_VENDORS":"published-by-the-winner"}""");

        var said = SettingsFile.AdoptRootSettings(Env("alpha"));

        SettingsFile.Read(SidePath("alpha"))["COAI_VENDORS"].Should().Be("published-by-the-winner",
            "the loser of the race overwrote the winner's file, which is last-writer-wins with two "
            + "processes that both believed they were first");
        said.Should().BeEmpty();
        Directory.EnumerateFiles(Path.Combine(_root, "alpha"), "*.tmp").Should().BeEmpty(
            "a refused publication left its temporary file behind");
    }
}

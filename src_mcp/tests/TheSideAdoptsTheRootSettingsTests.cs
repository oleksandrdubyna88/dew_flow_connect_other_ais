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
        SettingsFile.BetweenTheCheckAndThePublication = null;
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

        var providersFirst = SettingsFile.Layer(SettingsFile.DataDirFrom(env), env, _ => { });

        providersFirst("COAI_VENDORS").Should().Be("codex,gemini",
            "the one-shot mode reported defaults for an installation that has configuration, and a "
            + "normal start afterwards would have answered differently about the same machine");

        var normalStart = SettingsFile.Layer(SettingsFile.DataDirFrom(env), env, _ => { });

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
        File.WriteAllText(
            Path.Combine(_root, "alpha", $"{SettingsFile.Name}.9999.{Guid.NewGuid():N}.tmp"), "{ half");

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
    public void TheOtherSideWinsMidPublication_AndTheWinnersFileStands()
    {
        // The third way, and the code round was right about the first version of this test: it
        // created the destination BEFORE calling the adoption, so the existence check returned early
        // and `File.Move(overwrite: false)` — the guarantee the case is named after — was never
        // reached. It would have passed with the guard deleted.
        //
        // A race is only a race from INSIDE, so the other window finishes in the one instant that
        // matters: after this side has looked and found nothing, before it renames.
        WriteRoot("""{"COAI_VENDORS":"codex"}""");
        SettingsFile.BetweenTheCheckAndThePublication = destination =>
        {
            if (destination != SidePath("alpha"))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, """{"COAI_VENDORS":"published-by-the-winner"}""");
        };

        var said = SettingsFile.AdoptRootSettings(Env("alpha"));

        SettingsFile.Read(SidePath("alpha"))["COAI_VENDORS"].Should().Be("published-by-the-winner",
            "the loser of the race overwrote the winner's file, which is last-writer-wins with two "
            + "processes that both believed they were first");
        said.Should().BeEmpty("the other side published this side's own configuration; there is "
            + "nothing here that a person has to be told");
        Directory.EnumerateFiles(Path.Combine(_root, "alpha"), "*.tmp").Should().BeEmpty(
            "a refused publication left its temporary file behind");
    }

    [Fact]
    public void AnEmptyRootObject_IsAdoptedLikeAnyOther()
    {
        // `{}` is a settings file a person legitimately has — the panel opened once and nothing
        // changed from the shipped defaults. Treating it as "nothing to adopt" would leave the side
        // adopting the root LATER, after the person has configured that side, which is the one
        // ordering where adoption could overwrite something real.
        WriteRoot("{}");

        var said = SettingsFile.AdoptRootSettings(Env("alpha"));

        File.Exists(SidePath("alpha")).Should().BeTrue(
            "an empty object is a file that exists, and the side must stop looking at the root");
        said.Should().ContainSingle();
    }

    [Fact]
    public void Layer_TellsItsCaller_WhatTheAdoptionDid()
    {
        // The defect six reviewers across three vendors found in the code round: `Layer` adopted and
        // dropped the sentence on the floor, while the comment beside it claimed the startup path
        // logged what came back. Nothing anywhere called `AdoptRootSettings`. A migration that moves
        // a person's configuration, told to nobody, is the silent default this family of work exists
        // to end.
        WriteRoot("""{"COAI_VENDORS":"codex,gemini"}""");
        var env = Env("alpha");
        var heard = new List<string>();

        SettingsFile.Layer(SettingsFile.DataDirFrom(env), env, heard.Add);

        heard.Should().ContainSingle().Which.Should().Contain(SidePath("alpha"),
            "the caller is where the log and the stderr channel are; a sink that is never called is "
            + "the same as having no sentence at all");
    }

    [Fact]
    public void Layer_SaysNothing_WhenNothingWasAdopted()
    {
        // The other half, and it is what keeps the first half worth having: a line printed on every
        // ordinary start is a line nobody reads by the third day.
        var env = Env("alpha");
        var heard = new List<string>();

        SettingsFile.Layer(SettingsFile.DataDirFrom(env), env, heard.Add);

        heard.Should().BeEmpty("there was no root file, so nothing happened and nothing is worth "
            + "saying about it");
    }

    [Fact]
    public void ARootFileThatCannotBeRead_ReachesTheCallerToo()
    {
        // The failure path has to travel the same road as the success. This is the case where a
        // person's settings did NOT arrive, which is the one they most need told about.
        WriteRoot("{ this is not json");
        var env = Env("alpha");
        var heard = new List<string>();

        SettingsFile.Layer(SettingsFile.DataDirFrom(env), env, heard.Add);

        heard.Should().ContainSingle().Which.Should().Contain("starting on defaults");
    }
}

using CoaiMcp.Server;
using CoaiMcp.ServiceDefaults;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// One rule for where the data directory is, asked by everything that needs to know.
/// </summary>
/// <remarks>
/// <para><b>There were two rules.</b> <c>PanelSettings.ResolveDataDir</c> applied the side and the
/// trim and refused an unusable side name; <c>SettingsFile.DataDirFrom</c> was a bare
/// <c>COAI_DATA_DIR</c> read. So <c>coai.db</c> and <c>sessions/</c> went to
/// <c>&lt;root&gt;/&lt;side&gt;/</c> while <c>settings.json</c> and <c>logs/</c> stayed in
/// <c>&lt;root&gt;/</c> — two sides meant to be independent sharing one settings file and
/// overwriting each other, silently.</para>
/// <para><b>Seven callers</b> read the second rule: two one-shot modes, the log root, the
/// <c>--providers</c> layer, the settings layer the server runs on, and the file its change watcher
/// stats. One correction reaches all of them, which is why this is a call rather than a copy: a
/// second implementation of a resolver is the defect being fixed here.</para>
/// </remarks>
public sealed class TheSettingsFileKnowsItsSideTests
{
    private static Func<string, string?> Env(string? dir, string? side) => name => name switch
    {
        "COAI_DATA_DIR" => dir,
        "COAI_DATA_SIDE" => side,
        _ => null,
    };

    [Fact]
    public void TwoSidesOfOneRoot_KeepTheirOwnSettingsFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "coai-side-" + Guid.NewGuid().ToString("N"));

        var a = SettingsFile.PathFor(SettingsFile.DataDirFrom(Env(root, "alpha")));
        var b = SettingsFile.PathFor(SettingsFile.DataDirFrom(Env(root, "beta")));

        a.Should().NotBe(b,
            "two sides sharing one COAI_DATA_DIR are the whole point of the side: one settings file "
            + "between them is two installations overwriting each other's configuration, and nothing "
            + "says so");
        a.Should().Be(Path.Combine(root, "alpha", SettingsFile.Name));
        b.Should().Be(Path.Combine(root, "beta", SettingsFile.Name));
    }

    [Fact]
    public void NoSideAsked_LeavesTheDirectoryExactlyWhereItWas()
    {
        // The partition is OPT-IN, and this is the half that says so. Moving the data of somebody
        // who set COAI_DATA_DIR last year and never heard of a side would be the surprise
        // `PanelSettings` spends a paragraph refusing.
        var root = Path.Combine(Path.GetTempPath(), "coai-side-" + Guid.NewGuid().ToString("N"));

        SettingsFile.DataDirFrom(Env(root, null)).Should().Be(Path.GetFullPath(root));
        SettingsFile.DataDirFrom(Env(root, "   ")).Should().Be(Path.GetFullPath(root));
    }

    [Fact]
    public void AWhitespaceDataDir_MeansUnsetHereToo()
    {
        // `COAI_DATA_DIR=' '` reaching Path.GetFullPath is the working directory, which is not what
        // anybody meant by setting it. PanelSettings has always read it that way; this did not, so
        // the two halves disagreed about a whitespace variable.
        SettingsFile.DataDirFrom(Env("   ", null)).Should().Be(PanelSettings.DefaultDataDir);
        SettingsFile.DataDirFrom(Env("", null)).Should().Be(PanelSettings.DefaultDataDir);
        SettingsFile.DataDirFrom(Env(null, null)).Should().Be(PanelSettings.DefaultDataDir);
    }

    [Fact]
    public void AnUnusableSide_IsRefusedHereAsLoudlyAsAnywhereElse()
    {
        // A side that was ASKED FOR and refused must never fall back to the shared root: that is the
        // partition inverted, reached by a typo, and reported nowhere. `PanelSettings` throws; this
        // returned the root and let the server start.
        var root = Path.Combine(Path.GetTempPath(), "coai-side-" + Guid.NewGuid().ToString("N"));

        var refused = () => SettingsFile.DataDirFrom(Env(root, "wsl/node1"));

        refused.Should().Throw<InvalidOperationException>()
            .WithMessage("*COAI_DATA_SIDE*",
                "falling back to the root would put this installation and every other one on the "
                + "same database, which is what the partition exists to prevent");
    }

    [Fact]
    public void TheLogRoot_MovesWithTheSide_ThroughTheSameResolverAndNoRuleOfItsOwn()
    {
        // Not a separate decision and not a separate call site: Program.cs composes the log root as
        // `CoaiLogPath.RootFor(SettingsFile.DataDirFrom(...))`, so correcting the resolver partitions
        // the logs as a consequence. Leaving them shared would mean ADDING an exception.
        //
        // Why partitioned at all, since the file name carries the app and the pid: it carries no
        // MACHINE. `CoaiLogPath.For` is deterministic on (root, app, UTC second, pid) and
        // `CoaiLogging` opens the file with `shared: false`, so two machines on one NAS with the same
        // pid in the same second target one file neither will share. Attribution then rests on
        // nothing. (The consultation, 2026-09-18.)
        var root = Path.Combine(Path.GetTempPath(), "coai-side-" + Guid.NewGuid().ToString("N"));

        var a = CoaiLogPath.RootFor(SettingsFile.DataDirFrom(Env(root, "alpha")));
        var b = CoaiLogPath.RootFor(SettingsFile.DataDirFrom(Env(root, "beta")));

        a.Should().Be(Path.Combine(root, "alpha", "logs"));
        b.Should().Be(Path.Combine(root, "beta", "logs"));
        a.Should().NotBe(b);
    }

    [Fact]
    public void TheTwoResolvers_AnswerTheSameThingForEveryShapeOfInput()
    {
        // The property, rather than a list of cases: whatever PanelSettings decides, this decides.
        // A test enumerating ANSWERS passes the day somebody adds a rule to one of the two; this one
        // goes red.
        //
        // The shapes are chosen to cover every CLASS the rules distinguish, which is what the code
        // round asked for and the first draft did not have: unset / empty / whitespace / absolute /
        // trailing-separator / relative for the directory, and unset / empty / whitespace / plain /
        // punctuated / UPPER-CASE (lowered) / dot / dot-dot / with a separator (refused) for the
        // side. The last three are the interesting ones — they are the inputs on which the two could
        // disagree by one THROWING and the other answering.
        var root = Path.Combine(Path.GetTempPath(), "coai-side-" + Guid.NewGuid().ToString("N"));
        string?[] dirs = [null, "", "   ", root, root + Path.DirectorySeparatorChar, "relative-dir"];
        string?[] sides = [null, "", "  ", "alpha", "a_b-1", "  alpha  ", "WSL", ".", "..", "wsl/node1", "wsl\\node1"];

        foreach (var dir in dirs)
        {
            foreach (var side in sides)
            {
                var env = Env(dir, side);
                var why = $"the two resolvers must agree for COAI_DATA_DIR={dir ?? "<null>"} "
                    + $"COAI_DATA_SIDE={side ?? "<null>"}";

                // Agreement includes REFUSING alike. A resolver that answers where the other throws
                // is the original defect wearing a different hat: one half of the product carrying
                // on with a directory the other half refused to name.
                Answer(() => PanelSettings.DataDirectoryFor(env))
                    .Should().Be(Answer(() => SettingsFile.DataDirFrom(env)), why);
            }
        }
    }

    /// <summary>What a resolver said, or how it refused — so a throw is compared rather than thrown.</summary>
    private static string Answer(Func<string> resolve)
    {
        try
        {
            return resolve();
        }
        catch (InvalidOperationException e)
        {
            return "refused: " + e.Message;
        }
    }
}

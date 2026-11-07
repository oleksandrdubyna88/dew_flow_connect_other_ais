using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;
using static CoaiMcp.Tests.Leftovers.SweepPlants;

namespace CoaiMcp.Tests.Leftovers;

/// <summary>
/// The ONE switch of the start-of-run sweep (E5 S2-B, B9, plan D11): the repository's value, the list-only listing that removes nothing, the table of values, and a collect that throws. A test of the switch hands
/// <see cref="StartOfRunSweep.Run(string, StartOfRunSweep.Mode, Func{string, SweepResult}, Func{string, SweepResult}, StartOfRunSweep.Ledger)"/> a ledger of its own: it can never move the end or the root that the order proof and B3 read.
/// </summary>
/// <remarks>
/// Every private root is an owned scope's folder, never the machine's temp root; the plants and helpers are <see cref="SweepPlants"/>. Split from one 839-line file ([S2-B code1 1]); the spec's B-numbers are the same.
/// </remarks>
public sealed class StartOfRunSweepSwitchTests : IDisposable
{
    /// <summary>On macOS every test here stands on the identity read, so the layout self-test must hold first.</summary>
    private readonly OwnedScope _owned = DarwinLayout.FreshOwnedScope();

    public void Dispose() => _owned.Dispose();

    // ---- B9 -------------------------------------------------------------------------------------------------------

    [Fact]
    public void TheSwitch_IsOnInTheRepository_AndListOnlyRemovesNothing()
    {
        // (a) the repository's value, and the file's shape.
        var real = StartOfRunSweep.ReadSwitchFile();
        StartOfRunSweep.ReadMode(() => real, line => Assert.Fail("the repository's switch warns: " + line)).Should().Be(StartOfRunSweep.Mode.Collect, "main carries collect: a typo is red here, never silent");
        using (var document = JsonDocument.Parse(real!))
        {
            document.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("why", "startOfRunSweep");
        }

        // (b) list-only: the listing, never the sweep.
        var root = _owned.Dir("b9-root");
        var dead = Dead();
        var collectible = PlantFolder(root, Name("gone"), Rec(dead));
        var legacy = PlantFolder(root, Owned.Prefix + OwnedToken.RepoToken + "-run-1a2b3c", null);
        var quarantine = Path.Combine(root, "." + Name("held") + "." + QuarantineWord + "4242-0a1b2c3d");
        Directory.CreateDirectory(quarantine);
        var coai = Path.Combine(root, "coai-x");
        Directory.CreateDirectory(coai);
        var before = Snapshot(root);
        var ledger = new StartOfRunSweep.Ledger();

        var listed = StartOfRunSweep.Run(root, StartOfRunSweep.Mode.ListOnly, _ => throw new InvalidOperationException("collect was called"), StartOfRunSweep.ListOnly, ledger);

        using (new AssertionScope())
        {
            listed.Removed.Should().BeEmpty();
            listed.Preserved.Should().BeEmpty();
            listed.Errors.Should().BeEmpty();
            listed.Listed.Should().HaveCount(4, "the gone folder, its claim, the legacy folder and the quarantine; nothing for coai-x");
            listed.Listed.Should().ContainSingle(line => line.StartsWith("switched-off: folder " + Path.GetFileName(collectible) + " — ", StringComparison.Ordinal));
            listed.Listed.Should().ContainSingle(line => line.StartsWith("switched-off: claim " + Path.GetFileName(collectible) + ".owner — ", StringComparison.Ordinal));
            listed.Listed.Should().ContainSingle(line => line.StartsWith("switched-off: folder " + Path.GetFileName(legacy) + " — ", StringComparison.Ordinal));
            listed.Listed.Should().ContainSingle(line => line.StartsWith("switched-off: quarantine " + Path.GetFileName(quarantine) + " — ", StringComparison.Ordinal));
            listed.Listed.Should().OnlyContain(line => line.Contains("shared/leftovers-sweep.json", StringComparison.Ordinal) && line.EndsWith("nothing is removed", StringComparison.Ordinal));
            listed.Listed.Should().NotContain(line => line.Contains("coai-x", StringComparison.Ordinal));
            Snapshot(root).Should().Equal(before, "list-only reads names: no byte changed, nothing was renamed or made");
            ledger.LastRoot.Should().Be(root);
            ledger.EndedAt.Should().NotBe(0);
        }

        StartOfRunSweep.ListOnly(Path.Combine(root, "no-such-root")).Errors.Should().ContainSingle().Which.Should().StartWith("temp root ");

        // (c) the table of values.
        foreach (var (text, mode, warns) in new (string? Text, StartOfRunSweep.Mode Mode, string? Warns)[]
        {
            ("""{ "startOfRunSweep": "collect" }""", StartOfRunSweep.Mode.Collect, null),
            ("""{ "startOfRunSweep": "list-only" }""", StartOfRunSweep.Mode.ListOnly, null),
            ("""{ "startOfRunSweep": "COLLECT" }""", StartOfRunSweep.Mode.ListOnly, "COLLECT"),
            ("""{ "startOfRunSweep": "yes" }""", StartOfRunSweep.Mode.ListOnly, "yes"),
            ("""{ "startOfRunSweep": true }""", StartOfRunSweep.Mode.ListOnly, "true"),
            ("{}", StartOfRunSweep.Mode.ListOnly, "missing"),
            (null, StartOfRunSweep.Mode.ListOnly, "unreadable"),
            ("{", StartOfRunSweep.Mode.ListOnly, "unreadable"),
        })
        {
            var lines = new List<string>();

            StartOfRunSweep.ReadMode(() => text, lines.Add).Should().Be(mode, $"switch text {text ?? "(no file)"}");

            if (warns is null)
            {
                lines.Should().BeEmpty($"{text} is a plain value: no line");
            }
            else
            {
                lines.Should().ContainSingle().Which.Should().Contain("shared/leftovers-sweep.json").And.Contain("startOfRunSweep is " + warns);
            }
        }

        var thrown = new List<string>();
        StartOfRunSweep.ReadMode(() => throw new IOException("denied"), thrown.Add).Should().Be(StartOfRunSweep.Mode.ListOnly);
        thrown.Should().ContainSingle().Which.Should().Contain("unreadable: denied");

        // (d) a collect that throws: the end is still stamped, and the throw propagates (the fixture turns it into its one line).
        var thrower = new StartOfRunSweep.Ledger();
        var act = () => StartOfRunSweep.Run(root, StartOfRunSweep.Mode.Collect, _ => throw new InvalidOperationException("boom"), StartOfRunSweep.ListOnly, thrower);

        act.Should().Throw<InvalidOperationException>().WithMessage("boom");
        thrower.EndedAt.Should().NotBe(0, "the end is stamped in a finally");
        UnmatchablePid.AssertNoSuchProcess(dead);
    }
}

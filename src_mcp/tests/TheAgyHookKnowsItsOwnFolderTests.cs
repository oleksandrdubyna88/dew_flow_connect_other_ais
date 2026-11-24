using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The agy hook takes the coai folder from where it runs — the parent of its own <c>.agents</c> working folder — and never from the
/// payload's <c>cwd</c>, which agy sets to the first of its SORTED <c>workspacePaths</c>: a granted root whenever one sorts before the coai
/// folder (research/PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text_E1_S1_spec.md, W3).
/// </summary>
/// <remarks>The tests set <c>cwd</c> and the order of the entries themselves; none of them sorts.</remarks>
public sealed class TheAgyHookKnowsItsOwnFolderTests : AgyHookFixture
{
    private const string UnknownFolderLine = "coai --agy-hook: denied, this hook does not run from its own .agents folder";

    private string AsAgySends(string path, string[] entries, string tool = "read_file") => Sent(path, entries[0], entries, tool).ToJsonString();

    [Fact]
    public void F1_ARootThatSortsBeforeTheCoaiFolder_IsReadable_ForEveryReader()
    {
        foreach (var tool in AntigravityReadOnly.HookAllowedTools)
        {
            Allowed(AsAgySends(At("aaa/inside.txt"), [At("aaa"), Home], tool), tool);
        }
    }

    [Fact]
    public void F2_TheCoaiFolder_IsDenied_WhenARootThatSortsBeforeItIsTheCwd() =>
        Denied(
            AsAgySends(At("home/.agents/hooks.json"), [At("aaa"), Home]),
            control: AsAgySends(At("aaa/inside.txt"), [At("aaa"), Home]));

    [Fact]
    public void F3_InEveryOrderOfTheEntries_TheRootsAreReadable_AndTheCoaiFolderIsNot()
    {
        string[] entries = [At("aaa"), Home, At("job-1")];
        var orders = Permutations(entries).ToList();
        orders.Should().HaveCount(6);

        foreach (var order in orders)
        {
            var spelled = string.Join(", ", order.Select(Path.GetFileName));
            Allowed(AsAgySends(At("aaa/inside.txt"), order), $"aaa/inside.txt, order {spelled}");
            Allowed(AsAgySends(At("job-1/inside.txt"), order), $"job-1/inside.txt, order {spelled}");
            Denied(AsAgySends(At("home/.agents/hooks.json"), order), control: AsAgySends(At("aaa/inside.txt"), order), why: $"hooks.json, order {spelled}");
        }
    }

    private static IEnumerable<string[]> Permutations(string[] items) =>
        items.Length <= 1
            ? [items]
            : items.SelectMany((item, i) => Permutations([.. items.Where((_, j) => j != i)]).Select(rest => (string[])[item, .. rest]));

    [Fact]
    public void F4_TheCoaiFolderNeedNotBeListed_ItIsDeniedAllTheSame()
    {
        string[] entries = [At("aaa")];

        Allowed(AsAgySends(At("aaa/inside.txt"), entries));
        Denied(AsAgySends(At("home/.agents/hooks.json"), entries), control: AsAgySends(At("aaa/inside.txt"), entries));
    }

    [Fact]
    public void F5_AWideRootThatHoldsTheCoaiFolderAndSortsFirst_StillDoesNotOpenIt()
    {
        string[] entries = [W, Home];

        Denied(AsAgySends(At("home/.agents/hooks.json"), entries), control: AsAgySends(At("job-1/inside.txt"), entries));
        Allowed(AsAgySends(At("job-10/secret.txt"), entries));
    }

    [Fact]
    public void F6_ACwdThatIsARootButNotTheFirstEntry_IsReadable() =>
        Allowed(Sent(At("job-1/inside.txt"), At("job-1"), [Home, At("job-1")]).ToJsonString());

    [Fact]
    public void F7_ASiteThatCannotNameItsFolder_DeniesEveryRead()
    {
        foreach (var folder in (string?[])[null, string.Empty, "home", Home + "\0"])
        {
            Denied(Call(At("job-1/inside.txt")), site: SiteAt(folder), why: $"site folder {folder ?? "null"}");
        }
    }

    [Fact]
    public void F8_ASiteFolderThatCannotBeLookedAt_DeniesTheCall()
    {
        EntryReader reader = path => Path.TrimEndingDirectorySeparator(path) == Home ? throw new IOException("the disk said no") : RealPath.Lstat(path);
        var payload = Sent(At("second/b.txt"), At("job-1"), [At("job-1"), Second]).ToJsonString();

        Denied(payload, control: payload, site: SiteAt(Home, reader));
    }

    [Fact]
    public void F9_AnUnknownFolder_SaysSoOnce_ForAReader_AndNothingForAnotherTool()
    {
        using var log = new StringWriter();

        var read = Parse(AntigravityReadOnly.Decide(Call(At("job-1/inside.txt")), SiteAt(null), log));

        read.Should().Be(("deny", AntigravityReadOnly.DenyOutsideReason));
        log.ToString().Should().Be(UnknownFolderLine + Environment.NewLine);

        using var quiet = new StringWriter();
        var write = Parse(AntigravityReadOnly.Decide(Call(At("job-1/inside.txt"), "write_file"), SiteAt(null), quiet));

        write.Should().Be(("deny", AntigravityReadOnly.DenyReason));
        quiet.ToString().Should().BeEmpty();
    }

    [Fact]
    public void F13_AFolderThatIsNamedRightButCannotBeResolved_SaysTheSameLine_AndDenies()
    {
        var payload = Sent(Inside, Job1, [Job1, Second]).ToJsonString();
        using var quietControl = new StringWriter();
        Parse(AntigravityReadOnly.Decide(payload, Site(), quietControl)).Decision.Should().Be("allow", "the control: a resolvable folder");
        quietControl.ToString().Should().BeEmpty("a folder that resolves says nothing");

        EntryReader cannotLookAtTheFolder = path => Path.TrimEndingDirectorySeparator(path) == Home ? throw new IOException("the disk said no, with " + Inside) : RealPath.Lstat(path);
        var sites = new Dictionary<string, HookSite>
        {
            ["the disk cannot look at it"] = SiteAt(Home, cannotLookAtTheFolder),
            ["it is not local"] = SiteAt(Home, isLocal: text => !text.StartsWith(Home, StringComparison.Ordinal) && Local(text)),
        };

        foreach (var (why, site) in sites)
        {
            using var log = new StringWriter();

            Parse(AntigravityReadOnly.Decide(payload, site, log)).Should().Be(("deny", AntigravityReadOnly.DenyOutsideReason), why);
            log.ToString().Should().Be(UnknownFolderLine + Environment.NewLine, why);
        }
    }

    [Fact]
    public void F14_APathThatCannotBeResolved_IsDeniedInSilence_WhileTheFolderIsFine()
    {
        EntryReader cannotLookAtTheRoot = path => Path.TrimEndingDirectorySeparator(path) == Job1 ? throw new IOException("the disk said no") : RealPath.Lstat(path);
        using var log = new StringWriter();

        Parse(AntigravityReadOnly.Decide(Call(Inside), Site(cannotLookAtTheRoot), log)).Should().Be(("deny", AntigravityReadOnly.DenyOutsideReason));
        log.ToString().Should().BeEmpty("only a folder the hook cannot determine is worth a line; the line names no path");
    }
    [Fact]
    public void F10_TheSiteFolderAndAnEntryNamingOneFolderThroughALink_AreTheSameFolder()
    {
        LinkToTheFixture("via");
        var viaHome = At("via/home");

        string[] spelledAsLink = [Home, At("job-1")];
        Denied(Sent(At("home/.agents/hooks.json"), Home, spelledAsLink).ToJsonString(), control: Sent(Inside, Home, spelledAsLink).ToJsonString(), site: SiteAt(viaHome), controlSite: SiteAt(viaHome));
        Allowed(Sent(Inside, Home, spelledAsLink).ToJsonString(), site: SiteAt(viaHome));

        string[] reverse = [viaHome, At("job-1")];
        Denied(Sent(At("home/.agents/hooks.json"), viaHome, reverse).ToJsonString(), control: Sent(Inside, viaHome, reverse).ToJsonString(), site: SiteAt(Home), controlSite: SiteAt(Home));
        Allowed(Sent(Inside, viaHome, reverse).ToJsonString(), site: SiteAt(Home));
    }

    [Fact]
    public void F11_TheCoaiFolder_IsTheParentOfAnAgentsWorkingFolder_ByTextAlone()
    {
        var sep = Path.DirectorySeparatorChar;
        HookSite.CoaiFolderOf(At("h1/.agents")).Should().Be(At("h1"));
        HookSite.CoaiFolderOf(At("h1/.agents") + sep).Should().Be(At("h1"));
        foreach (var notAgents in new[] { At("h1"), At("h1/.agents/agents"), At("h1/.agentsx"), ".agents", string.Empty })
        {
            HookSite.CoaiFolderOf(notAgents).Should().BeNull(notAgents);
        }

        HookSite.CoaiFolderOf(At("h1/.AGENTS")).Should().Be(DocumentId.Comparison == StringComparison.OrdinalIgnoreCase ? At("h1") : null);
        Directory.Exists(At("h1")).Should().BeFalse("the rule reads text, never the disk");
    }

    [Fact]
    public void F12_TheSiteOfThisProcess_NeverThrows_AndDoesNotKnowAFolderFromATestHost()
    {
        var site = HookSite.OfThisProcess();

        site.CoaiFolder.Should().BeNull("the test host does not run from a folder named .agents");
        site.Read.Should().NotBeNull();
    }
}

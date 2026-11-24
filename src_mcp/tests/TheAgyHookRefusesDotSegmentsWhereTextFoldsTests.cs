using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Where the OS folds <c>.</c> and <c>..</c> as text before it follows any link (Windows), a <c>Path</c> with such a segment is denied: the
/// spelling the hook judges is then the spelling the OS opens (research/PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text_E1_S3_spec.md, W1).
/// </summary>
/// <remarks>
/// <para><c>RealPath</c> applies <c>..</c> to the path resolved so far, which is what a Unix kernel does. With a junction <c>R\nm</c> to
/// <c>R\deep\a\b</c>, <c>R\nm\..\..\outside\secret.txt</c> is judged as inside <c>R</c> and opened by Windows outside it. The flag is an input
/// of <see cref="HookSite"/>, so both arms run on every machine.</para>
/// </remarks>
public sealed class TheAgyHookRefusesDotSegmentsWhereTextFoldsTests : AgyHookFixture
{
    private HookSite LinkedSite(bool folds) => Site(Standing("jl", _ => Entry.Link(At("job-1/sub/a/b"))), folds: folds);

    private string ThroughTheLink => Path.Combine(Job1, "jl", "..", "..", "outside", "secret.txt");

    [Fact]
    public void T1_ADotDotThroughALink_IsDenied_WhereTextFolds()
    {
        File.Exists(At("outside/secret.txt")).Should().BeTrue("folded as text the path is W/outside/secret.txt, outside every root");
        var site = LinkedSite(folds: true);

        Denied(Call(ThroughTheLink), control: Call(Path.Combine(Job1, "jl", "x.txt")), site: site, controlSite: site);
    }

    [Fact]
    public void T2_TheSameDotDot_IsTheWalksVerdict_WhereTextDoesNotFold() =>
        Allowed(Call(ThroughTheLink), "off Windows the kernel is the walk: W/job-1/sub/outside/secret.txt is a missing name inside the root", site: LinkedSite(folds: false));

    [Fact]
    public void T3_ADotOrDotDotSegment_IsDenied_WhenTextFolds_AndAllowed_WhenNot()
    {
        foreach (var spelled in new[] { Path.Combine(Job1, "sub", "..", "inside.txt"), Path.Combine(Job1, ".", "inside.txt") })
        {
            Denied(Call(spelled), site: Site(folds: true), why: $"{spelled}, folds");
            Allowed(Call(spelled), $"{spelled}, does not fold", site: Site(folds: false));
        }
    }

    [Fact]
    public void T4_ANameWithDots_IsNoDotSegment()
    {
        foreach (var name in new[] { "job-1/.hidden", "job-1/..x/f.txt", "job-1/x..y.txt" })
        {
            Allowed(Call(At(name)), name, site: Site(folds: true));
        }
    }

    [Fact]
    public void T5_TheRuleReadsPathOnly_NotTheRoots() =>
        Allowed(Call(Inside, roots: [Path.Combine(Job1, "sub", "..")]), "a root is coai's and agy's text, never the model's", site: Site(folds: true));

    [Theory]
    [InlineData(@"C:\a\..\b", true)]
    [InlineData(@"C:\a\.\b", true)]
    [InlineData("C:/a/../b", true)]
    [InlineData(@"C:\a\..", true)]
    [InlineData(@"C:\a\.", true)]
    [InlineData(@"C:\a\..\", true)]
    [InlineData("/a/../b", true)]
    [InlineData(@"C:\a\b.txt", false)]
    [InlineData(@"C:\..a\b", false)]
    [InlineData(@"C:\a..\b", false)]
    [InlineData(@"C:\...\b", false)]
    [InlineData(@"C:\.. \b", false)]
    [InlineData(@"C:\a\.hidden", false)]
    [InlineData(@"C:\a\b.", false)]
    public void T6_ADotSegment_IsASegmentThatIsExactlyDotOrDotDot_OnEitherSeparator(string text, bool has) =>
        AgyHookPlaces.HasDotSegment(text).Should().Be(has, text);

    [Fact]
    public void T7_TheProductsSite_FoldsWhereThisOsDoes() =>
        HookSite.OfThisProcess().FoldsDotsAsText.Should().Be(OperatingSystem.IsWindows());

    [Fact]
    public void T8_OnWindows_ARealJunctionAndADotDot_LeaveTheRoot_AndAreDenied()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Win32 folds .. as text only on Windows; T1 and T2 pin both arms on every host");
        Junctions.Shell.MakeLink(At("job-1/nm"), At("job-1/sub/a/b")).Should().BeEmpty("a junction needs no privilege");
        var outside = Path.Combine(Job1, "nm", "..", "..", "outside", "secret.txt");
        var site = SiteAt(Home, RealPath.Lstat, HookSite.OfThisProcess().IsLocal, HookSite.OfThisProcess().FoldsDotsAsText);

        File.ReadAllText(outside).Should().Be("text of outside/secret.txt", "the premise: Windows reads the file outside the root");
        Denied(Call(outside), control: Call(Path.Combine(Job1, "nm", "x.txt")), site: site, controlSite: site);
    }
}

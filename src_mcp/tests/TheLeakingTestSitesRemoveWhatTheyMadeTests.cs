using System.Reflection;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A test that makes a temp folder removes it (<c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md</c>, D6): the C# sites.
/// </summary>
/// <remarks>
/// <para>Found by reading the folder the tests share: <c>coai-plan-said-</c> (196 folders) and <c>coai-check-model-</c> (30) were made and never
/// removed, and so were four more beside them in <c>PanelServiceTests</c> (<c>coai-record-</c>, <c>coai-cwd-</c>, <c>coai-deal-</c>, <c>coai-deal-plan-</c>) — the
/// spec said to check every sibling. Under the per-run temp folder a leak no longer outlives the run, but a test that makes a folder and keeps it is
/// still wrong: it grows the folder during the run and leaves it behind when a run is killed.</para>
/// <para>Each arm RUNS the named test method in this process, on a fresh instance, and compares the folders of its prefix before and after. The class is in
/// <c>fakecli-env</c>: the tests it runs set <c>FAKECLI_*</c> variables, and that collection runs alone, so nothing else makes a folder of these prefixes
/// between the two listings. <b>Limit:</b> it names the sites; a NEW leaking site is not found by it — the real-temp counts of <c>research/module_tests.md</c> are that check.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class TheLeakingTestSitesRemoveWhatTheyMadeTests
{
    private static List<string> FoldersOf(string prefix) => [.. Directory.GetDirectories(RunTemp.Folder, prefix + "*")];

    private static async Task RunAsync<T>(string method) where T : new()
    {
        var instance = new T();
        var init = instance as IAsyncLifetime;
        if (init is not null)
        {
            await init.InitializeAsync();
        }

        try
        {
            await (Task)typeof(T).GetMethod(method, BindingFlags.Public | BindingFlags.Instance)!.Invoke(instance, null)!;
        }
        finally
        {
            if (init is not null)
            {
                await init.DisposeAsync();
            }
        }
    }

    [Theory]
    [InlineData("coai-record-", nameof(PanelServiceTests.CodeStage_FansOutThreeRolesPerProvider_WithThreeDistinctPrompts))]
    [InlineData("coai-cwd-", nameof(PanelServiceTests.ThePlanStage_GivesReviewersNoCheckout_ToExplore))]
    [InlineData("coai-plan-said-", nameof(PanelServiceTests.ThePlanStage_IsToldItHasNoCheckout_NotMerelyGivenNone))]
    [InlineData("coai-deal-", nameof(PanelServiceTests.WithDealingOn_ACodeRound_AsksEachRoleOnce_AcrossTheVendors))]
    [InlineData("coai-deal-plan-", nameof(PanelServiceTests.WithDealingOn_ThePlanRound_SpendsADifferentLensPerVendor))]
    public async Task APanelServiceTestThatRecordsLaunches_RemovesItsRecordFolder(string prefix, string method)
    {
        var before = FoldersOf(prefix);

        await RunAsync<PanelServiceTests>(method);

        FoldersOf(prefix).Except(before).Should().BeEmpty($"{method} made a '{prefix}' folder and must remove it");
    }

    [Fact]
    public async Task TheCheckModelTest_RemovesItsDataFolder()
    {
        var before = FoldersOf("coai-check-model-");

        await RunAsync<EpicTwoReviewThreadsTests>(nameof(EpicTwoReviewThreadsTests.CheckModel_IsDispatchedToTheModelCheck_NotTheCallerKindsCheck));

        FoldersOf("coai-check-model-").Except(before).Should().BeEmpty("the test made its own data folder and must remove it");
    }
}

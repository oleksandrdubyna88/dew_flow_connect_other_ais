using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A reviewer list that is EMPTY is empty — never "nothing configured, run the defaults".
/// </summary>
/// <remarks>
/// <para>The cadence consultation for epics 1–3 of todo/PLAN_one_model_catalog.md (finding 3): a catalog whose rows
/// review nothing — a chat model alone, an import, a hand edit — reaches the server as <c>COAI_VENDORS="[]"</c>, and
/// the server read that like an unset variable and ran Codex and Antigravity, reviewers the person never chose.
/// The round's own refusal ("nothing could review the stage … every configured vendor is disabled") is the honest
/// answer to an empty list, and it is what follows now.</para>
/// <para>Unset or blank still means "not configured" — a scripted or containerised run with no list keeps its
/// defaults, and <c>COAI_PROVIDERS</c> still answers for it.</para>
/// </remarks>
public sealed class AnEmptyReviewerListIsEmptyTests
{
    private static IReadOnlyList<string> ProvidersOf(Dictionary<string, string> env) =>
        [.. PanelSettings.FromEnvironment(name => env.GetValueOrDefault(name)).Providers.Select(p => p.Provider)];

    [Fact]
    public void An_explicitly_empty_list_runs_no_reviewer_the_person_did_not_choose()
    {
        ProvidersOf(new() { ["COAI_VENDORS"] = "[]" }).Should().BeEmpty();
        ProvidersOf(new() { ["COAI_VENDORS"] = " [ ] " }).Should().BeEmpty();
    }

    [Fact]
    public void A_list_whose_rows_name_no_vendor_is_not_an_empty_choice_and_runs_the_defaults()
    {
        // CodeRabbit on #688: only a list that SAYS nothing is empty; rows with no usable id are not a choice of nobody.
        ProvidersOf(new() { ["COAI_VENDORS"] = "[{}]" }).Should().Equal("codex", "antigravity");
        ProvidersOf(new() { ["COAI_VENDORS"] = "[{\"id\":\"  \"}]" }).Should().Equal("codex", "antigravity");
    }

    [Fact]
    public void No_list_at_all_is_still_not_configured_and_runs_the_defaults()
    {
        ProvidersOf([]).Should().Equal("codex", "antigravity");
        ProvidersOf(new() { ["COAI_VENDORS"] = string.Empty }).Should().Equal("codex", "antigravity");
        ProvidersOf(new() { ["COAI_PROVIDERS"] = "claude" }).Should().Equal("claude");
    }
}

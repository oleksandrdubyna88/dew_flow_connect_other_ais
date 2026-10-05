using CoaiMcp.Core.Context;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Core.Security;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Pins what <see cref="SecurityRoster.Append"/> records for a pairing that does NOT become work — skipped,
/// excluded, or failed while being prepared — and every arm of its prompt check, so splitting both methods
/// to complexity 4 cannot move a pairing between those lists or change why.
/// </summary>
/// <remarks>
/// <para>Written against the code BEFORE the split and observed green there
/// (research/PLAN_security_lane_methods_within_complexity_4.md).</para>
/// <para>The roster is built directly with a runtime lookup that finds nothing and a launch lookup that throws,
/// so every arm is reached without a vendor: an exclusion is decided before the launch is looked up, and a
/// pairing that gets as far as the launch lands in the per-pairing <c>catch</c> — the arm no other test
/// reaches. Work that does run is <see cref="SecurityLaneRoundTests"/>' subject, through the real runtimes.</para>
/// </remarks>
public sealed class SecurityRosterCharacterizationTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-security-roster-").FullName;

    public void Dispose() => Directory.Delete(_data, true);

    private static readonly FileDiff[] Sql = [new("Query.cs", "@@ -1 +1 @@\n+database.Query(value);")];
    private static readonly FileDiff[] Plain = [new("Plain.cs", "@@ -1 +1 @@\n+return 42;")];

    private SecurityRoster Roster(string lane, Func<ProviderSettings, bool>? canRun = null, bool codexEnabled = true)
    {
        ProviderSettings[] providers = [new("codex") { Enabled = codexEnabled }, new("gemini")];
        var setting = SecurityLaneSetting.Parse(lane, providers);
        var settings = new PanelSettings
        {
            DataDir = _data,
            Providers = providers,
            SecurityLane = setting,
            Rounds = new PanelConfig() with { SecurityLane = setting.Gate },
        };
        return new SecurityRoster(settings, new RolePrompts(_data), canRun ?? (_ => true), _ => null,
            (_, _) => throw new InvalidOperationException("fixture launch"), Serilog.Core.Logger.None);
    }

    private static string Render(RoundWork work) => string.Join("\n",
        [$"reviewers={work.Reviewers.Count} active={work.SecurityActive}",
            .. work.NotAsked.Select(s => $"not asked {s.Role}: {s.Reason}"),
            .. work.Excluded.Select(e => $"excluded {e.Provider}/{e.Role}: {e.Reason}")]);

    private string Append(string lane, IReadOnlyList<FileDiff> files, int round = 1, Func<ProviderSettings, bool>? canRun = null,
        bool codexEnabled = true) =>
        Render(Roster(lane, canRun, codexEnabled).Append(new RoundWork([], [], []), files, Stage.CodeReview, round));

    private const string SqlPair = """{"enabled":true,"runs":[{"vendor":"codex","prompt":"redteam-sql"}]}""";
    private const string Unprepared = "could not prepare security review (InvalidOperationException)";

    private string WriteOverride(string id, byte[] bytes)
    {
        var path = new RolePrompts(_data).FileToWrite(id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void A_lane_that_does_not_apply_returns_the_ordinary_work_itself()
    {
        var ordinary = new RoundWork([], [], []);
        Roster("""{"enabled":false,"runs":[{"vendor":"codex","prompt":"redteam-sql"}]}""")
            .Append(ordinary, Sql, Stage.CodeReview, 1).Should().BeSameAs(ordinary);
    }

    [Fact]
    public void A_spent_lane_budget_skips_every_pairing_serving_the_stage_and_only_those()
    {
        Append("""
            {"enabled":true,"maxRounds":1,"runs":[{"vendor":"codex","prompt":"redteam-sql"},
            {"vendor":"gemini","prompt":"redteam-sql","stages":["feature"]},{"vendor":"gemini","prompt":"redteam-authz","context":"whole"}]}
            """, Sql, round: 2).Should().Be("""
            reviewers=0 active=True
            not asked codex/redteam-sql: security lane round budget spent
            not asked gemini/redteam-authz: security lane round budget spent
            """.ReplaceLineEndings("\n").TrimEnd('\n'));
    }

    [Fact]
    public void A_broken_pairing_or_prompt_is_excluded_with_its_own_reason()
    {
        Append("""
            {"enabled":true,"prompts":[{"id":"redteam-authz","triggers":[]}],
            "runs":[{"vendor":"codex","prompt":"redteam-sql","context":"whole"},{"vendor":"gemini","prompt":"redteam-authz"}]}
            """, Sql).Should().Be("""
            reviewers=0 active=True
            excluded codex/redteam-sql: context must be slice or diff
            excluded gemini/redteam-authz: a preset requires at least one trigger; select a condition before enabling it
            """.ReplaceLineEndings("\n").TrimEnd('\n'));
    }

    private const string GeneralPair = """{"enabled":true,"runs":[{"vendor":"codex","prompt":"redteam-general"}]}""";

    /// <summary>
    /// General is decided by paths, not by what a detector could read: an oversized PROSE file is not code, so a
    /// docs-only commit is a plain skip for it, never "incomplete coverage" (2026-10-04, own review of epic 1).
    /// </summary>
    [Fact]
    public void An_oversized_prose_only_change_is_a_plain_skip_for_general() =>
        Append(GeneralPair, [new("CHANGELOG.md", new string('a', SecuritySignals.MaxFileCharacters + 1))]).Should().Be(
            "reviewers=0 active=True\nnot asked codex/redteam-general: no code file in this committed change; redteam-general reviews code only");

    /// <summary>Files past the detector cap were never looked at — they may be code, so general cannot call that a skip.</summary>
    [Fact]
    public void Files_beyond_the_cap_leave_general_incomplete_even_when_the_rest_is_prose() =>
        Append(GeneralPair, [.. Enumerable.Range(0, SecuritySignals.MaxFiles + 2).Select(i => new FileDiff($"f{i}.md", "+notes"))]).Should().Be(
            "reviewers=0 active=True\nexcluded codex/redteam-general: trigger coverage incomplete: 0 oversized diffs and 2 files beyond the detector limit were not inspected");

    [Fact]
    public void A_fully_inspected_change_without_the_trigger_is_a_skip() =>
        Append(SqlPair, Plain).Should().Be(
            "reviewers=0 active=True\nnot asked codex/redteam-sql: no matching trigger in this committed change; prior fixes were not verified by this run");

    /// <summary>
    /// A card's own words decide whether the ROUND asks it (PLAN_one_model_catalog.md E2.4): the roster classifies with the
    /// lane's table, not the shipped one — a diff carrying the card's word reaches the launch, one without it is not asked.
    /// </summary>
    [Fact]
    public void A_cards_own_words_decide_whether_the_round_asks_it()
    {
        const string Billing = """{"enabled":true,"prompts":[{"id":"redteam-billing","words":["acme.charge("]}],"runs":[{"vendor":"codex","prompt":"redteam-billing"}]}""";
        // A custom card's text, so the only thing deciding the outcome is whether its words are in the change.
        WriteOverride("redteam-billing", System.Text.Encoding.UTF8.GetBytes("Review the billing calls for missing authorization."));

        Append(Billing, [new("Pay.cs", "@@ -1 +1 @@\n+Acme.Charge(order);")]).Should().Contain(Unprepared, "its word is in the change, so it is asked");
        Append(Billing, Plain).Should().Contain("not asked codex/redteam-billing");
    }

    [Fact]
    public void An_oversized_diff_without_the_trigger_is_incomplete_coverage() =>
        Append(SqlPair, [new("Large.cs", new string('a', SecuritySignals.MaxFileCharacters + 1))]).Should().Be(
            "reviewers=0 active=True\nexcluded codex/redteam-sql: trigger coverage incomplete: 1 oversized diffs and 0 files beyond the detector limit were not inspected");

    [Fact]
    public void Files_beyond_the_detector_cap_without_the_trigger_are_incomplete_coverage() =>
        Append(SqlPair, [.. Enumerable.Range(0, SecuritySignals.MaxFiles + 2).Select(i => new FileDiff($"f{i}.cs", "+return 42;"))]).Should().Be(
            "reviewers=0 active=True\nexcluded codex/redteam-sql: trigger coverage incomplete: 0 oversized diffs and 2 files beyond the detector limit were not inspected");

    [Fact]
    public void A_triggered_pairing_on_a_disabled_row_is_excluded() =>
        Append(SqlPair, Sql, codexEnabled: false).Should().Be("reviewers=0 active=True\nexcluded codex/redteam-sql: reviewer row disabled");

    [Fact]
    public void A_triggered_pairing_whose_runtime_cannot_run_is_excluded() =>
        Append(SqlPair, Sql, canRun: _ => false).Should().Be(
            "reviewers=0 active=True\nexcluded codex/redteam-sql: reviewer runtime or credentials unavailable");

    [Fact]
    public void A_failure_while_preparing_one_pairing_is_recorded_on_it_and_the_next_pairing_still_runs()
    {
        Append("""
            {"enabled":true,"runs":[{"vendor":"gemini","prompt":"redteam-sql"},{"vendor":"codex","prompt":"redteam-sql"}]}
            """, Sql, codexEnabled: false).Should().Be($"""
            reviewers=0 active=True
            excluded gemini/redteam-sql: {Unprepared}
            excluded codex/redteam-sql: reviewer row disabled
            """.ReplaceLineEndings("\n").TrimEnd('\n'));
    }

    [Fact]
    public void An_override_file_over_64_KiB_makes_the_prompt_unavailable()
    {
        var path = WriteOverride("redteam-sql", [.. Enumerable.Repeat((byte)'x', SecurityContext.MaxPromptBytes + 1)]);
        Append(SqlPair, Sql).Should().Be(
            $"reviewers=0 active=True\nexcluded codex/redteam-sql: prompt unavailable or over 64 KiB; write text at {path}");
    }

    [Fact]
    public void An_override_file_of_exactly_64_KiB_is_read()
    {
        WriteOverride("redteam-sql", [.. Enumerable.Repeat((byte)'x', SecurityContext.MaxPromptBytes)]);
        Append(SqlPair, Sql).Should().Be($"reviewers=0 active=True\nexcluded codex/redteam-sql: {Unprepared}");
    }

    [Fact]
    public void An_override_within_64_KiB_on_disk_whose_text_is_larger_is_unavailable()
    {
        // 0xFF is no UTF-8: each byte reads back as U+FFFD, three bytes when the text is measured again.
        var path = WriteOverride("redteam-sql", [.. Enumerable.Repeat((byte)0xFF, SecurityContext.MaxPromptBytes)]);
        Append(SqlPair, Sql).Should().Be(
            $"reviewers=0 active=True\nexcluded codex/redteam-sql: prompt unavailable or over 64 KiB; write text at {path}");
    }

    [Theory]
    [InlineData("<!-- OPERATOR: write the audit instructions here -->")]
    [InlineData("\n  <!-- OPERATOR: write the audit instructions here -->  \n")]
    public void An_override_that_is_only_the_operator_placeholder_is_unavailable(string text)
    {
        var path = WriteOverride("redteam-sql", System.Text.Encoding.UTF8.GetBytes(text));
        Append(SqlPair, Sql).Should().Be(
            $"reviewers=0 active=True\nexcluded codex/redteam-sql: prompt unavailable or over 64 KiB; write text at {path}");
    }

    [Theory]
    [InlineData("<!-- OPERATOR: placeholder --> and real instructions")]
    [InlineData("<!-- OPERATOR: a --> b -->")]
    [InlineData("Instructions first. <!-- OPERATOR: note -->")]
    public void Text_beyond_the_placeholder_is_a_prompt(string text)
    {
        WriteOverride("redteam-sql", System.Text.Encoding.UTF8.GetBytes(text));
        Append(SqlPair, Sql).Should().Be($"reviewers=0 active=True\nexcluded codex/redteam-sql: {Unprepared}");
    }

    [Fact]
    public void A_custom_prompt_with_no_text_anywhere_is_unavailable()
    {
        var path = new RolePrompts(_data).FileToWrite("redteam-custom");
        Append("""{"enabled":true,"prompts":[{"id":"redteam-custom"}],"runs":[{"vendor":"codex","prompt":"redteam-custom"}]}""", Sql)
            .Should().Be($"reviewers=0 active=True\nexcluded codex/redteam-custom: prompt unavailable or over 64 KiB; write text at {path}");
    }
}

using System.Text.Json;
using Xunit;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;

namespace CoaiMcp.Tests;

/// <summary>
/// A role nobody compiled in reviews a real change, end to end, and its findings are counted as its
/// own.
/// </summary>
/// <remarks>
/// <para>The unit tests beside this one assert that a custom role is BUILT into a round and handed
/// its own text. That is the mechanism; this is the story — a repository, a branch, a plan gate, a
/// code gate, a vendor answering through the fake CLI, and a verdict — because the pieces it joins
/// live in four classes and the question "does a custom role actually review anything" is not
/// answered by any of them alone. Asked for by codex on the code round of the story that removed
/// the `ReviewRole` enum.</para>
/// <para>Scripted through the fake CLI's environment; nothing here touches a real model or the
/// network.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class ACustomRoleReviewsAChangeTests : IAsyncLifetime
{
    private const string Scope = """
        # SCOPE

        A role a person defined must be able to review a change and have its findings counted under
        its own name. When it is done: the round names the role, the finding carries it, and the
        role's own prompt text is what the reviewer was given.
        """;

    private const string OneMajor = """
        {"findings": [
          {"severity": "major", "category": "reliability", "file": "app.cs", "line": 10,
           "title": "the requirement about retries is not met", "why": "the call has no retry at all",
           "fix": "retry once on a transient failure"}
        ]}
        """;

    private const string Clean = """{"findings": []}""";

    private const string TheText = "You review one thing: whether the stated requirement is met.";

    private readonly ProcessLauncher _launcher = new();
    private string _repo = string.Empty;
    private string _data = string.Empty;

    private static string FakeCliExe => Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-customrole-repo-").FullName;
        _data = Directory.CreateTempSubdirectory("coai-customrole-data-").FullName;

        Directory.CreateDirectory(Path.Combine(_data, "prompts"));
        await File.WriteAllTextAsync(Path.Combine(_data, "prompts", "req-general.md"), TheText);

        await Git("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), "v1\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
        await Git("checkout", "-b", "feature");
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), string.Concat(Enumerable.Repeat("line\n", 40)));
        await Git("add", ".");
        await Git("commit", "-m", "the feature");

        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
    }

    public ValueTask DisposeAsync()
    {
        foreach (var name in (string[])["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_OUTFILE_TEXT", "FAKECLI_EXIT", "FAKECLI_STDERR"])
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        foreach (var dir in (string[])[_repo, _data])
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return ValueTask.CompletedTask;
    }

    private static void Script(string answer)
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "0");
        Environment.SetEnvironmentVariable("FAKECLI_STDERR", "");
    }

    private async Task Git(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest(
            "git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], _repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    /// <summary>
    /// One custom result role, and the four shipped ones switched off so the round is only it.
    /// </summary>
    private PanelService Service()
    {
        var catalog = RoleComposition.Compose([
            new RoleEntry(RoleCatalog.ConventionsRole, Active: false),
            new RoleEntry(RoleCatalog.ArchitectureRole, Active: false),
            new RoleEntry(RoleCatalog.SecurityRole, Active: false),
            new RoleEntry(RoleCatalog.UxDxRole, Active: false),
            new RoleEntry("Requirements", Name: "Requirements", Stage: RoleStages.Result,
                Prompts: [new PromptEntry("req-general", "General", "Whether the requirement is met.")]),
        ]);

        return new PanelService(
            new PanelSettings
            {
                Providers = [new("codex") { ExecutablePath = FakeCliExe }],
                // One round each, and a threshold generous enough that a clean plan proceeds: the
                // subject here is which ROLE reviews, not what the gate makes of what it finds.
                Rounds = new PanelConfig(
                    catalog.Roles.ToDictionary(r => r.Id, _ => new RoleGate(1, 5)),
                    StagePolicy.Human)
                {
                    Catalog = catalog,
                },
                DataDir = _data,
                ReviewerTimeout = TimeSpan.FromSeconds(30),
            },
            VaultKeys.None("no vault in tests"),
            default,
            _launcher,
            Logger.None);
    }

    /// <summary>The shipped plan role, plus one a person added to the same stage.</summary>
    private PanelService WithACustomPlanRole(bool dealLenses = false)
    {
        File.WriteAllText(Path.Combine(_data, "prompts", "brief-general.md"),
            "You read one thing: whether the plan says what it is for.");

        var catalog = RoleComposition.Compose([
            new RoleEntry(RoleCatalog.ConventionsRole, Active: false),
            new RoleEntry(RoleCatalog.ArchitectureRole, Active: false),
            new RoleEntry(RoleCatalog.SecurityRole, Active: false),
            new RoleEntry(RoleCatalog.UxDxRole, Active: false),
            new RoleEntry("Brief", Name: "The brief", Stage: RoleStages.Plan,
                Prompts: [new PromptEntry("brief-general", "General", "Whether the plan says what it is for.")]),
        ]);

        return new PanelService(
            new PanelSettings
            {
                Providers = [new("codex") { ExecutablePath = FakeCliExe }],
                Rounds = new PanelConfig(catalog.Roles.ToDictionary(r => r.Id, _ => new RoleGate(1, 5)), StagePolicy.Human)
                {
                    Catalog = catalog,
                },
                DataDir = _data,
                ReviewerTimeout = TimeSpan.FromSeconds(30),
                DealPlanLenses = dealLenses,
            },
            VaultKeys.None("no vault in tests"),
            default,
            _launcher,
            Logger.None);
    }

    /// <summary>
    /// A plan-stage role a person added actually reviews the plan.
    /// </summary>
    /// <remarks>
    /// Everything around it already worked: composition put the role in the catalog, the settings
    /// gave it <c>COAI_ROUNDS_BRIEF</c> and an enable switch that the shipped plan role deliberately
    /// does not have, and <c>RolesForRound</c> listed it. <c>ReviewPlanAsync</c> then passed a
    /// hardcoded one-element array to <c>BuildWork</c>, so the role was configured, switchable, and
    /// never asked anything — the code stage had been reading its roster from the catalog since
    /// story B1 and the plan stage had not. (gemini, twice on this story's code round.)
    /// </remarks>
    [Fact]
    public async Task ACustomPlanRole_ReviewsThePlan()
    {
        var service = WithACustomPlanRole();
        await service.OpenAsync(_repo, "feature");

        Script(OneMajor);
        var plan = Parse(await service.ReviewPlanAsync(_repo, "feature", Scope));

        plan.GetProperty("reviewers").GetString().Should().Contain("2 reviewers",
            "the shipped plan role and the one this person added");

        // Which two, from the round's own record — the findings cannot answer it, because two
        // reviewers reporting the same thing are merged into one finding carrying one role.
        var round = Parse(await service.StatusAsync(_repo, "feature")).GetProperty("rounds")[0];
        round.GetProperty("reviewerStates").EnumerateArray().Select(r => r.GetProperty("role").GetString())
            .Should().BeEquivalentTo([RoleCatalog.PlanRole, "Brief"]);
    }

    /// <summary>
    /// And it still reviews the plan when the round is dealing lenses rather than asking everyone
    /// everything.
    /// </summary>
    /// <remarks>
    /// The unspent-lens pool was read from the shipped plan role alone, which was the whole plan
    /// stage until a person could add a second role to it. With lens dealing switched on, every
    /// question in the hand then belonged to <c>PlanCritique</c> — so the round a person configured
    /// two plan roles for asked one of them twice and the other nothing. (Found while fixing the
    /// lens-ownership defect codex and gemini both raised on the second code round.)
    /// </remarks>
    [Fact]
    public async Task ACustomPlanRole_ReviewsThePlan_EvenWhenTheRoundDealsItsLenses()
    {
        var service = WithACustomPlanRole(dealLenses: true);
        await service.OpenAsync(_repo, "feature");

        Script(OneMajor);
        await service.ReviewPlanAsync(_repo, "feature", Scope);

        var round = Parse(await service.StatusAsync(_repo, "feature")).GetProperty("rounds")[0];
        // Both, and each ONCE: a hand that gave one role two lenses and the other none would satisfy
        // "contains Brief" while being the very defect this test is about. (CodeRabbit, this plan's
        // pull request.)
        round.GetProperty("reviewerStates").EnumerateArray().Select(r => r.GetProperty("role").GetString())
            .Should().BeEquivalentTo([RoleCatalog.PlanRole, "Brief"],
                "a hand dealt from one role's lenses is not a round of two roles");
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static string AcceptAll(JsonElement answer) =>
        JsonSerializer.Serialize(
            Enumerable.Range(0, answer.GetProperty("findings").GetArrayLength())
                .Select(i => new { finding = i, action = "accept" }));

    [Fact]
    public async Task ItReviewsTheChange_AndItsFindingsAreCountedAsItsOwn()
    {
        var service = Service();
        await service.OpenAsync(_repo, "feature");

        Script(Clean);
        var plan = Parse(await service.ReviewPlanAsync(_repo, "feature", Scope));
        await service.ResolveAsync(_repo, "feature", AcceptAll(plan));
        Script(OneMajor);

        var code = Parse(await service.ReviewCodeAsync(_repo, "feature", "main", Scope));

        code.GetProperty("reviewers").GetString().Should().Contain("1 reviewer",
            "the four shipped code roles are off, so the round is the custom one alone");
        code.GetProperty("findings").EnumerateArray().Should().NotBeEmpty()
            .And.OnlyContain(f => f.GetProperty("role").GetString() == "Requirements",
                "a finding is counted against the threshold of the role that RAISED it");
    }

    [Fact]
    public async Task TheReviewerWasGivenTheRolesOwnText()
    {
        // The prompt the vendor actually received, read out of the round's own audit rather than
        // inferred: the answer file is the fake CLI's, but the prompt is ours.
        var service = Service();
        await service.OpenAsync(_repo, "feature");

        Script(Clean);
        var plan = Parse(await service.ReviewPlanAsync(_repo, "feature", Scope));
        await service.ResolveAsync(_repo, "feature", AcceptAll(plan));
        Script(OneMajor);

        var work = service.BuildWork(["Requirements"], _repo, "ctx", round: 1, isPlanStage: false).Reviewers;
        var arguments = work[0].Invocation.Request.Arguments.ToList();

        // codex takes its prompt on stdin, so there is no file to read — the request carries it.
        work[0].Invocation.Request.StdIn.Should().Contain(TheText);
        arguments.Should().Contain("exec", "the shipped codex adapter built this launch");
    }
}

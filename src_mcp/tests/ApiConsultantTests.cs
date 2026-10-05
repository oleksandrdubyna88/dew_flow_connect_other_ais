using System.Text.Json;
using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Feature;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Normalizer;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Feature;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A hosted API as a QUESTION consultant (PLAN_question_consultant.md, S1 acceptance 4): it composes
/// <c>ApiRuntime</c> the way <c>LocalConsultant</c> composes <c>LocalRuntime</c>, answers one shot on a
/// <c>none</c> grant only, is given the context after the secret check plus the outline of what exists
/// at HEAD, and serves a symbol request from the committed HEAD — never from the working tree (A9).
/// </summary>
public sealed class ApiConsultantTests : IAsyncLifetime
{
    private const string Key = "sk-test-0123456789abcdefghijklmnop";

    private const string CartAtHead =
        "public sealed class Cart\n"
        + "{\n"
        + "    public int Add(int n)\n"
        + "    {\n"
        + "        return n + 1; // committed\n"
        + "    }\n"
        + "}\n";

    private const string CartUncommitted =
        "public sealed class Cart\n"
        + "{\n"
        + "    public int Add(int n)\n"
        + "    {\n"
        + "        return n + 2; // UNCOMMITTED\n"
        + "    }\n"
        + "}\n";

    private static readonly VendorIdentity OpenRouterGrok =
        new("grok-openrouter", "api", "https://openrouter.ai/api/v1", KeyName: "openrouter");

    private readonly ProcessLauncher _launcher = new();
    private readonly string _data = Directory.CreateTempSubdirectory("coai-apiq-").FullName;
    private TempGitRepo _git = null!;
    private string _head = string.Empty;

    private string Scratch => Path.Combine(_data, "scratch");

    private string Answers => Path.Combine(_data, "answers");

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Scratch);
        Directory.CreateDirectory(Answers);
        _git = await TempGitRepo.InitAsync(_launcher, "coai-apiq-repo-");
        Directory.CreateDirectory(Path.Combine(_git.Path, "src"));
        await _git.WriteAsync("src/Cart.cs", CartAtHead);
        await _git.CommitAsync("the cart");
        _head = await _git.HeadAsync();
        // An edit nobody committed: what the resolver must NOT serve (the stated limit of A9).
        await _git.WriteAsync("src/Cart.cs", CartUncommitted);
    }

    public async ValueTask DisposeAsync()
    {
        await _git.DisposeAsync();
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException) { }
    }

    private static ApiConsultant Consultant(QuestionMaterial? material = null) =>
        new(new ApiRuntime("grok-openrouter", "https://openrouter.ai/api/v1"), "grok-openrouter", "openrouter", material);

    private ConsultantLaunch Launch(string prompt = "the question", Confinement.Planned? plan = null) =>
        new(_git.Path, prompt, string.Empty, Answers, new ReviewerSettings("grok-openrouter") { Model = "x-ai/grok-4.7", ApiKey = Key }, SchemaFile())
        {
            Confinement = new LaunchConfinement.Planned(plan ?? ((Confinement.Planned)ConfinementPlanner.Plan("api", CapabilityGrant.None))),
            ScratchDir = Scratch,
        };

    private string SchemaFile() =>
        ConsultSchemaFile.Ensure(_data, QuestionAnswerSchema.Name, QuestionAnswerSchema.Json, "An api question row needs it and will be refused").Path;

    private SourceResolver Resolver() => new(new GitHistory(_launcher), new TreeSitterOutliner(), _git.Path, _head);

    // ---------- resolution (A11) ----------

    [Fact]
    public void TheOpenRouterRow_ResolvesToTheApiConsultant_WithItsVaultKeyName()
    {
        var runtime = QuestionResolution.For(OpenRouterGrok);

        var api = runtime.Should().BeOfType<ApiConsultant>().Which;
        api.Vendor.Should().Be("grok-openrouter");
        api.VaultKeyName.Should().Be("openrouter", "the key is filed under the row's key name, not its id (S3.6)");
        api.NeedsAnswerSchema.Should().BeTrue("the shim refuses a request without a schema");
    }

    [Fact]
    public void EveryRuntimeTheQuestionListAdvertises_Resolves_AndTheStuckConsultantsListIsUnchanged()
    {
        QuestionResolution.Answering.Should().Equal("codex", "claude", "antigravity", "local", "api");
        foreach (var runtime in QuestionResolution.Answering)
        {
            var identity = runtime == "api"
                ? new VendorIdentity(runtime, runtime, "https://api.example.com/v1")
                : new VendorIdentity(runtime, runtime, runtime == "local" ? "http://127.0.0.1:11434/v1" : string.Empty);

            QuestionResolution.For(identity).Should().NotBeNull(runtime);
        }

        // The stuck consultant's list was held narrower than this one until PLAN_one_model_catalog.md E2.3, which gave it
        // the api runtime: the two now name the same runtimes, read from shared/feature-availability.json.
        ConsultantResolution.Consulting.Should().Equal("codex", "claude", "antigravity", "local", "api");
        ConsultantResolution.For(OpenRouterGrok).Should().BeOfType<ApiConsultant>("an api row consults since E2.3");
    }

    [Theory]
    [InlineData("remsoftdev-codex", "remote", "https://coai.remsoft.dev")]
    [InlineData("gemini", "gemini", "")]
    // A codex row on a custom endpoint answers since E2.3: the question consultant resolves CLI rows through the same
    // CodexConsultant, which now carries the endpoint's provider on every launch.
    public void ARowNeitherListCanLaunch_IsRefusedByName(string provider, string runtime, string baseUrl)
    {
        var identity = new VendorIdentity(provider, runtime, baseUrl);

        QuestionResolution.For(identity).Should().BeNull();
        QuestionResolution.CannotAnswer(identity).Should().Contain(provider).And.Contain("question consultant");
    }

    [Fact]
    public void TheCliConsultants_AreAnsweringRuntimes_AndTheirConversationMembersStayOnTheDerivedSeam()
    {
        typeof(IConsultantRuntime).GetInterfaces().Should().Contain(typeof(IAnsweringRuntime),
            "IAnsweringRuntime is the base the question consultant launches through");
        typeof(IAnsweringRuntime).GetMembers().Select(m => m.Name).Should().Contain(["Vendor", "Build", "NeedsAnswerSchema", "ReadAdvice"])
            .And.NotContain(["Memory", "ReadHandle", "DroppedTheConversation", "UsageIsCumulative"],
                "a one-shot answer has no conversation to remember, resume or drop");
        QuestionResolution.For(new VendorIdentity("claude", "claude", string.Empty)).Should().BeAssignableTo<IConsultantRuntime>(
            "the shipped adapters answer questions too, through the planned confinement");
    }

    // ---------- the launch ----------

    [Fact]
    public void ItLaunchesThisBinaryInAskApiMode_FromTheScratchDirectory_WithTheKeyInTheEnvironmentOnly()
    {
        var invocation = Consultant().Build(Launch());

        invocation.Request.Arguments.Should().Contain("--ask-api").And.ContainInOrder("--vendor", "grok-openrouter");
        invocation.Request.Arguments.Should().ContainInOrder("--model", "x-ai/grok-4.7");
        invocation.Request.WorkingDirectory.Should().Be(Scratch, "a none row stands nowhere near the checkout");
        invocation.Role.Should().Be(ConsultantRoles.Question);
        invocation.Request.Environment.Should().ContainKey(ApiRuntime.KeyVariable).WhoseValue.Should().Be(Key);
        invocation.Request.Arguments.Should().NotContain(a => a.Contains(Key), "a command line is visible to every process listing");
        File.ReadAllText(PromptFileOf(invocation)).Should().NotContain(Key, "the prompt file is kept as evidence");
        invocation.Request.InheritsEnvironment.Should().BeFalse("the minimal child environment (S1 acceptance 6)");
        invocation.Request.Passthrough.Should().BeEquivalentTo(ProcessEnvironment.Minimal);
        invocation.SharedResource.Should().BeEmpty("a hosted vendor's fleet is bounded by its rate limit, not by a card here");
    }

    [Fact]
    public void DiskAndWeb_AreRefusedByName_AndTheStuckConsultantsShapeBuilds()
    {
        var disk = new Confinement.Planned("api", CapabilityGrant.Disk("D:/x"), [], [], CwdKind.Root, ["D:/x"], AdmissionFlag.None);
        var web = new Confinement.Planned("api", CapabilityGrant.Web, [], [], CwdKind.Scratch, [], AdmissionFlag.None);

        var buildDisk = () => Consultant().Build(Launch(plan: disk));
        var buildWeb = () => Consultant().Build(Launch(plan: web));
        var buildShipped = () => Consultant().Build(Launch() with { Confinement = LaunchConfinement.AsShipped });

        buildDisk.Should().Throw<ArgumentException>().WithMessage("*'disk'*api*");
        buildWeb.Should().Throw<ArgumentException>().WithMessage("*'web'*api*");
        // A stuck consultation is an api shape since E2.3 (AnApiRowConsultsTests): one completion through the shim.
        buildShipped.Should().NotThrow();
    }

    [Fact]
    public void TheSchemaItNeeds_IsTheConsultAnswerWithSourceRequests_OnDisk()
    {
        using var schema = JsonDocument.Parse(QuestionAnswerSchema.Json);
        var required = schema.RootElement.GetProperty("required").EnumerateArray().Select(r => r.GetString()).ToList();

        required.Should().Equal("answer", "sourceRequests");
        schema.RootElement.GetProperty("properties").GetProperty("answer").GetProperty("type").GetString().Should().Be("string");
        schema.RootElement.GetProperty("additionalProperties").GetBoolean().Should().BeFalse("OpenAI's strict rules");
        QuestionAnswerSchema.Name.Should().NotBe(ConsultAnswerSchema.Name, "the stuck consultant's file is never overwritten");
        File.ReadAllText(SchemaFile()).Should().Be(QuestionAnswerSchema.Json);
    }

    // ---------- the prompt: context after SecretCheck, plus the outline (A9) ----------

    [Fact]
    public async Task GivenAContext_ThePromptCarriesItAfterSecretCheck_PlusTheOutlineOfWhatExists()
    {
        const string context = "I tried `dotnet build`; the launcher at src/Cart.cs returns n + 1 and I expected n + 2";
        var checkedContext = SecretCheck.Inspect(context).Should().BeOfType<SecretCheckResult.Clean>().Which.Context;
        var outline = await new FeatureOutlineBuilder(_launcher, new TreeSitterOutliner()).BuildAtHeadAsync(_git.Path, _head);
        var prompt = ApiQuestionPrompt.Compose(new ApiQuestionInput("You are the best developer in the world.", "Why is Add off by one?", checkedContext, outline.Section, FollowUps: 3, Nonce: "n0nce"));

        var invocation = Consultant().Build(Launch(prompt));

        var sent = File.ReadAllText(PromptFileOf(invocation));
        sent.Should().Be(prompt, "the prompt travels as a file, byte for byte");
        sent.Should().Contain(context, "the context, unredacted, after the check").And.Contain("Why is Add off by one?");
        sent.Should().Contain("src/Cart.cs").And.Contain("Add", "the outline names what exists at HEAD");
        // A body line, not `return n` — the context above says "returns n + 1", which contains that.
        sent.Should().NotContain("return n + 1").And.NotContain("// committed", "an outline has signatures and no bodies");
        sent.Should().Contain("(n0nce)", "the material is fenced with the turn's nonce");
        sent.Should().Contain("sourceRequests").And.Contain("3 follow-up", "the model is told it may ask for source, and how often");
        sent.Should().Contain("committed", "the stated limit: source is served from HEAD, uncommitted work only through the context");
        outline.Files.Should().OnlyContain(f => f.Change == FileChange.Added, "against the empty tree every file is new");
        outline.BaseNote.Should().Contain("exists");
    }

    [Fact]
    public void ARefusedContext_HasNoCheckedContextToCompose()
    {
        SecretCheck.Inspect("the key is sk-live-0123456789abcdefghijklmnop").Should().BeOfType<SecretCheckResult.Refused>()
            .Which.Class.Should().Be("vendor-key");
        // The composer takes a CheckedContext and nothing else; see SecretCheckTests for the constructor.
        ApiQuestionPrompt.Compose(new ApiQuestionInput("i", "q", CheckedContext.Empty, string.Empty, 0, "n")).Should().Contain("## The question");
    }

    // ---------- the source turns: a symbol request is served from HEAD ----------

    [Fact]
    public async Task ASymbolRequest_IsServedFromTheCommittedHead_AndTheUncommittedEditIsNot()
    {
        var consultant = Consultant(new QuestionMaterial(string.Empty, new SourceTurns.On(Resolver(), FollowUps: 3)));
        var launch = Launch("## The question\nwhy?");
        var asked = """{"answer":"I need to see Cart.Add first.","sourceRequests":[{"file":"src/Cart.cs","symbol":"Add","startLine":null,"endLine":null,"why":"the seam"}]}""";

        var decision = await consultant.AfterAsync(launch, AnsweringMemory.Start(launch.Prompt), asked, TestContext.Current.CancellationToken);

        var next = decision.Should().BeOfType<AnsweringTurn.Next>().Which;
        next.Launch.Prompt.Should().StartWith(launch.Prompt, "the base prompt is resent byte for byte (D25)");
        next.Launch.Prompt.Should().Contain("return n + 1; // committed", "served from HEAD");
        next.Launch.Prompt.Should().NotContain("UNCOMMITTED", "the working tree is never served — the stated limit of A9");
        next.Launch.Prompt.Should().Contain($"@ {_head}", "every slice names the commit it was read at");
        next.Launch.Prompt.Should().Contain("## Turn 2 of 4");
        next.Memory.Turn.Should().Be(2);
        next.Memory.ServedSoFar.Should().ContainSingle().Which.Symbol.Should().Be("Add");
        next.Note.Should().Contain("served src/Cart.cs Add");
        next.Launch.Confinement.Should().Be(launch.Confinement, "the next turn is the same planned launch with a longer prompt");
    }

    [Fact]
    public async Task AnAnswerWithNoRequest_IsDone_WithItsAdvice_AndProseIsTheAdviceItself()
    {
        var consultant = Consultant(new QuestionMaterial(string.Empty, new SourceTurns.On(Resolver(), FollowUps: 3)));
        var launch = Launch();

        var structured = await consultant.AfterAsync(launch, AnsweringMemory.Start(launch.Prompt), """{"answer":"Use n + 2.","sourceRequests":null}""", TestContext.Current.CancellationToken);
        var prose = await consultant.AfterAsync(launch, AnsweringMemory.Start(launch.Prompt), "Just use n + 2.", TestContext.Current.CancellationToken);

        structured.Should().BeOfType<AnsweringTurn.Done>().Which.Advice.Should().Be("Use n + 2.");
        prose.Should().BeOfType<AnsweringTurn.Done>().Which.Advice.Should().Be("Just use n + 2.", "a vendor that answered prose answered");
        consultant.ReadAdvice("""{"answer":"Use n + 2.","sourceRequests":null}""").Should().Be("Use n + 2.");
        consultant.ReadAdvice("plain words").Should().Be("plain words");
    }

    [Fact]
    public async Task TheFollowUpCap_AndSourceTurnsOff_EndTheConversation_NamingWhatWasNotServed()
    {
        var asked = """{"answer":"more","sourceRequests":[{"file":"src/Cart.cs","symbol":"Add","startLine":null,"endLine":null,"why":"w"}]}""";
        var capped = Consultant(new QuestionMaterial(string.Empty, new SourceTurns.On(Resolver(), FollowUps: 1)));
        var off = Consultant(new QuestionMaterial(string.Empty, SourceTurns.None));
        var launch = Launch();

        var secondTurn = AnsweringMemory.Start(launch.Prompt) with { Turn = 2 };
        var atCap = await capped.AfterAsync(launch, secondTurn, asked, TestContext.Current.CancellationToken);
        var noTurns = await off.AfterAsync(launch, AnsweringMemory.Start(launch.Prompt), asked, TestContext.Current.CancellationToken);

        atCap.Should().BeOfType<AnsweringTurn.Done>().Which.Note.Should().Contain("follow-up cap");
        var done = noTurns.Should().BeOfType<AnsweringTurn.Done>().Which;
        done.Advice.Should().Be("more");
        done.Note.Should().Contain("src/Cart.cs").And.Contain("not served", "a request no turn will answer is written down, never dropped");
        off.FollowUps.Should().Be(0);
        capped.FollowUps.Should().Be(1);
    }

    [Fact]
    public async Task ARequestOutsideTheRepository_IsARefusalLineInTheNextTurn_NeverAnException()
    {
        var consultant = Consultant(new QuestionMaterial(string.Empty, new SourceTurns.On(Resolver(), FollowUps: 3)));
        var launch = Launch();
        var asked = """{"answer":"x","sourceRequests":[{"file":"../secrets.txt","symbol":null,"startLine":null,"endLine":null,"why":"w"},{"file":"src/Cart.cs","symbol":"Nope","startLine":null,"endLine":null,"why":"w"}]}""";

        var next = (await consultant.AfterAsync(launch, AnsweringMemory.Start(launch.Prompt), asked, TestContext.Current.CancellationToken))
            .Should().BeOfType<AnsweringTurn.Next>().Which;

        next.Launch.Prompt.Should().Contain("could not be read", "the parser's refusal of a path that climbs out");
        next.Launch.Prompt.Should().Contain("not served: src/Cart.cs Nope", "the resolver's refusal of a symbol the file does not declare");
        next.Memory.ServedSoFar.Should().BeEmpty();
    }

    [Fact]
    public async Task TheOutlineOfWhatExists_IsBuiltAtHead_AgainstTheRepositorysEmptyTree()
    {
        var outline = await new FeatureOutlineBuilder(_launcher, new TreeSitterOutliner()).BuildAtHeadAsync(_git.Path, _head);

        outline.HeadSha.Should().Be(_head);
        outline.Files.Select(f => f.Path).Should().Equal("src/Cart.cs");
        outline.Section.Should().Contain("Cart").And.Contain("Add");
        outline.Section.Should().NotContain("UNCOMMITTED", "git objects at head, never the working tree");
    }

    [Fact]
    public void RowOutcomes_AreTheSixWordsOfSection4()
    {
        RowOutcomes.All.Should().Equal("answered", "timed_out", "failed", "refused", "blocked", "disabled");
        RowOutcomes.IsKnown("answered").Should().BeTrue();
        RowOutcomes.IsKnown("critical").Should().BeFalse();
    }

    private static string PromptFileOf(ReviewerInvocation invocation)
    {
        var args = invocation.Request.Arguments.ToList();

        return args[args.IndexOf("--prompt-file") + 1];
    }
}

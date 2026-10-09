using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A codex row on somebody else's endpoint (OpenRouter, DeepSeek) consults through the Codex CLI with ITS provider —
/// on the first turn and on every resume (research/PLAN_one_model_catalog.md, epic 2, story 3; D9).
/// </summary>
/// <remarks>
/// <para>The codex consultant built its own argv and never asked the endpoint runtime for its provider overrides, so an
/// endpoint row would have reached OpenAI's service with the endpoint's key — which is why ConsultantResolution refused
/// it ("cannot hold a consultation"). The reviewer has carried those four <c>-c</c> overrides since custom endpoints
/// shipped; the consultant now carries the same ones, from the same runtime, and the key in the same variable.</para>
/// <para>A resume must carry them too: <c>codex exec resume</c> reads the provider from the command line like any other
/// launch, and a resume without them would send turn two to OpenAI.</para>
/// </remarks>
public sealed class AnEndpointConsultantKeepsItsProviderTests
{
    private const string Repo = "D:/repo";
    private const string Endpoint = "https://openrouter.example/api/v1";

    private static ReviewerSettings Settings() => new("openrouter") { Model = "z-ai/glm-5.3", ApiKey = "sk-or-test" };

    private static Runners.Processes.ProcessRequest Launch(string handle) =>
        new CodexConsultant(new CustomCodexRuntime("openrouter", Endpoint), "openrouter")
            .Build(new ConsultantLaunch(Repo, "help me", handle, "D:/answers", Settings()))
            .Request;

    [Theory]
    [InlineData("")]
    [InlineData("0199a4f2-6c1e-7a40-9d3b-1f2e3d4c5b6a")]
    public void EveryTurn_NamesTheEndpointsProvider(string handle)
    {
        var argv = Launch(handle).Arguments;

        argv.Should().ContainInConsecutiveOrder("-c", "model_provider=openrouter");
        argv.Should().Contain($"model_providers.openrouter.base_url={Endpoint}");
        argv.Should().Contain("model_providers.openrouter.env_key=OPENROUTER_API_KEY");
    }

    [Fact]
    public void TheKeyTravelsInTheEndpointsVariable_NeverOpenAis()
    {
        var environment = Launch(string.Empty).Environment;

        environment.Should().ContainKey("OPENROUTER_API_KEY").WhoseValue.Should().Be("sk-or-test");
        environment.Should().NotContainKey("OPENAI_API_KEY", "an endpoint's key must not be offered to OpenAI's service");
    }

    [Fact]
    public void APlainCodexConsultant_IsUnchanged()
    {
        var request = new CodexConsultant(new CodexRuntime())
            .Build(new ConsultantLaunch(Repo, "help me", string.Empty, "D:/answers", new ReviewerSettings("codex") { ApiKey = "sk-openai" }));

        request.Request.Arguments.Should().NotContain(argument => argument.StartsWith("model_provider", StringComparison.Ordinal));
        request.Request.Environment.Should().ContainKey("OPENAI_API_KEY");
    }

    [Fact]
    public void AnEndpointRow_IsAConsultant_NoLongerRefused()
    {
        var row = new VendorIdentity("openrouter", "codex", Endpoint);

        ConsultantResolution.For(row).Should().BeOfType<CodexConsultant>("its provider now travels with every turn");
    }
}

/// <summary>The refusal names what a row IS: a Team server row has a base URL — the server's — and is no custom endpoint.</summary>
public sealed class ARefusalNamesWhatTheRowIsTests
{
    [Fact]
    public void ATeamServerRow_IsNotCalledACustomEndpoint()
    {
        ConsultantResolution.CannotConsult(new VendorIdentity("srv-codex", "remote", "https://coai.example"))
            .Should().Contain("runs on 'remote'").And.NotContain("custom endpoint");
    }
}

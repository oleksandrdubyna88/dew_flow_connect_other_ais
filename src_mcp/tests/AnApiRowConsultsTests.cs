using CoaiMcp.Core.Catalog;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// An api row consults (todo/PLAN_one_model_catalog.md D9, epic 2 story 3): one completion per turn through the
/// <c>--ask-api</c> shim, and — the vendor keeping no conversation — the earlier turns carried in the prompt, as the
/// local consultant has carried them since it shipped.
/// </summary>
/// <remarks>
/// <para>The operator's own example was a hosted GLM at high effort as consultant. <see cref="ApiConsultant"/> already
/// answered question rows; a stuck consultation was refused by name ("the api runtime answers question rows only").
/// It is the same adapter, widened, rather than a second api adapter beside it.</para>
/// <para>Where the conversation is kept: on the consultation record, which already stores every turn's problem and
/// advice, carries them into the next prompt under the frozen <see cref="ConsultantMemory.WeRemember.CarryBudget"/>,
/// and is swept with the consultation — so the plan's separate transcript file was not built (a recorded deviation).</para>
/// </remarks>
public sealed class AnApiRowConsultsTests
{
    private static readonly VendorIdentity Row = new("glm", "api", "https://open.bigmodel.example/api/paas/v4");

    [Fact]
    public void AnApiRow_ResolvesToAConsultant()
    {
        ConsultantResolution.For(Row).Should().BeOfType<ApiConsultant>();
    }

    [Fact]
    public void ItRemembersNothing_SoWeCarryTheConversation()
    {
        ConsultantResolution.For(Row)!.Memory.Should().BeOfType<ConsultantMemory.WeRemember>()
            .Which.CarryBudget.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AStuckTurn_IsOneCompletionThroughTheShim_WithItsPromptInAFile()
    {
        var output = Directory.CreateTempSubdirectory("coai-api-consult-").FullName;
        try
        {
            var invocation = ConsultantResolution.For(Row)!.Build(new ConsultantLaunch(
                "D:/repo", "what is stuck", string.Empty, output,
                new ReviewerSettings("glm") { Model = "glm-5.3", ApiKey = "sk-glm" }, Path.Combine(output, "consult-schema.json")));

            invocation.Request.Arguments.Should().Contain("--ask-api").And.Contain("--prompt-file");
            invocation.TempFiles.Should().ContainSingle(file => File.Exists(file) && File.ReadAllText(file) == "what is stuck");
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void TheSharedFile_ListsApiAmongTheConsultingRuntimes()
    {
        FeatureAvailability.Builtin.Consultant.Should().Contain("api");
        ConsultantResolution.Consulting.Should().Contain("api", "the server and the panel answer from the one list");
    }
}

using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The claude consultant reports what its CLI refused — the <c>permission_denials</c> of its JSON envelope — so the
/// consultant check can tell a read the CLI REFUSED from one the model never tried.
/// </summary>
/// <remarks>
/// E4.2 of PLAN_the_consultant_works_on_every_vendor.md (cadence consultation 435b1b25): a model that declines on
/// its own proves compliance, not confinement; only the CLI's own record of a refusal is observed confinement. The
/// antigravity adapter already read its stream's <c>denied_actions</c>; this is claude's twin, read from the one
/// field the CLI writes for it — never from the answer text, which is the model speaking.
/// </remarks>
public sealed class ClaudeConsultantDeniedActionsTests
{
    private static ReviewerLaunch Launched(string stdout, string stderr = "") =>
        new(null, "CANNOT", new Usage(1, 1, null), string.Empty, new ProcessResult(0, stdout, stderr, false));

    private static IReadOnlyList<string> Denied(string stdout, string stderr = "") =>
        ((IConsultantRuntime)new ClaudeConsultant(new ClaudeRuntime())).DeniedActions(Launched(stdout, stderr));

    [Fact]
    public void TheEnvelopesPermissionDenials_AreTheDeniedActions_ByToolName()
    {
        const string envelope = """
            {"type":"result","subtype":"success","is_error":false,"result":"marker: wren\ncanary: CANNOT",
             "session_id":"67289235-65f7-40b5-9532-e63515d90f30",
             "permission_denials":[
               {"tool_name":"Read","tool_use_id":"toolu_01","tool_input":{"file_path":"C:/tmp/coai-check-1/outside/canary.txt"}},
               {"tool_name":"Glob","tool_use_id":"toolu_02","tool_input":{"pattern":"**/*"}}]}
            """;

        Denied(envelope).Should().Equal("Read", "Glob");
    }

    /// <summary>Each refusal carries what the refused tool was asked for — the canary check attributes a denial by it.</summary>
    [Fact]
    public void EachDenial_CarriesWhatTheToolWasAskedFor_OrNothingWhenItNamedNothing()
    {
        const string envelope = """
            {"type":"result","result":"CANNOT","session_id":"x","permission_denials":[
              {"tool_name":"Read","tool_input":{"file_path":"C:/tmp/coai-check-1/outside/canary.txt"}},
              {"tool_name":"Glob","tool_input":{"pattern":"**/*.md","path":"C:/tmp/coai-check-1/outside"}},
              {"tool_name":"Grep","tool_input":{"pattern":"heron"}},
              {"tool_name":"WebFetch","tool_input":{"url":"https://example.invalid"}}]}
            """;

        ((IConsultantRuntime)new ClaudeConsultant(new ClaudeRuntime())).Denials(Launched(envelope)).Should().Equal(
            new DeniedAction("Read", "C:/tmp/coai-check-1/outside/canary.txt"),
            new DeniedAction("Glob", "C:/tmp/coai-check-1/outside"),
            new DeniedAction("Grep", "heron"),
            new DeniedAction("WebFetch", string.Empty));
    }

    [Fact]
    public void NoDenials_AnEmptyList_OrNoField_IsNothingDenied()
    {
        Denied("""{"type":"result","result":"fine","session_id":"x","permission_denials":[]}""").Should().BeEmpty();
        Denied("""{"type":"result","result":"fine","session_id":"x"}""").Should().BeEmpty();
    }

    [Fact]
    public void TheModelSayingTheWords_IsNotADenial_AndATornEnvelopeIsNone()
    {
        // The answer QUOTES the field name — the model speaking about permissions is not the CLI refusing one.
        Denied("""{"type":"result","result":"\"permission_denials\":[{\"tool_name\":\"Read\"}]","session_id":"x"}""")
            .Should().BeEmpty("only the envelope's own field is the CLI's record");
        Denied("""{"type":"result","result":"cut off by a ki""").Should().BeEmpty("a killed turn leaves a torn envelope, which is no record");
        Denied(string.Empty, "permission_denials: Read").Should().BeEmpty("stderr is not where claude records a denial");
    }

    [Fact]
    public void AnEmptyClaudeTurnWhoseReadWasRefused_IsReadDenied_NotEmpty()
    {
        // The whole-branch review, F: the classification read agy's words (command, read_file, read_url) for every
        // vendor, so a claude turn that answered nothing because its Read outside the checkout was refused was
        // classified "empty" — with a cure to read the transcript, when the cure is to put the file in the repository.
        const string envelope = """
            {"type":"result","subtype":"success","is_error":false,"result":"","session_id":"x","permission_denials":[
              {"tool_name":"Read","tool_input":{"file_path":"C:/Users/someone/elsewhere/notes.md"}}]}
            """;
        var silent = new ReviewerLaunch(null, string.Empty, new Usage(1, 1, null), string.Empty, new ProcessResult(0, envelope, string.Empty, false));

        var failure = ConsultFailures.Classify(new ClaudeConsultant(new ClaudeRuntime()), silent, "claude", new ConsultFailure.Timeout());

        failure.Should().BeOfType<ConsultFailure.ReadDenied>().Which.Action.Should().Be("Read");
    }

    [Fact]
    public void AnEmptyClaudeTurnWithNothingRefused_IsStillEmpty()
    {
        var silent = new ReviewerLaunch(null, string.Empty, new Usage(1, 1, null), string.Empty,
            new ProcessResult(0, """{"type":"result","result":"","session_id":"x","permission_denials":[]}""", string.Empty, false));

        ConsultFailures.Classify(new ClaudeConsultant(new ClaudeRuntime()), silent, "claude", new ConsultFailure.Timeout())
            .Should().BeOfType<ConsultFailure.Empty>();
    }
}

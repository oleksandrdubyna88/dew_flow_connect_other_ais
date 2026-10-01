using CoaiMcp.Core.QuestionConsult;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The secret half ALONE: what a <c>none</c> row on a hosted runtime has its context checked by
/// (PLAN_question_consultant.md, A9, D10) — a path or a code fence passes, a secret shape refuses, and
/// nothing is redacted (S1 acceptance 3).
/// </summary>
/// <remarks>
/// It reuses the notice redaction's own passes (<c>Redaction</c>) to RECOGNISE a secret, and refuses
/// rather than taking it out: the text goes to a hosted model and into the vendor's own store, and a
/// redactor that missed a shape would send the rest with it. Refusing hands the caller the cure.
/// </remarks>
public sealed class SecretCheckTests
{
    [Theory]
    [InlineData("the file is at C:\\Users\\me\\src\\Shop.cs and the test is in tests/ShopTests.cs")]
    [InlineData("```csharp\nvar x = Foo();\n```")]
    [InlineData("I tried `dotnet build` and then `git status`; the launcher at ./src/x.ts is wrong")]
    [InlineData("the endpoint is http://127.0.0.1:11434/v1 and COAI_DATA_DIR=D:/data")]
    [InlineData("")]
    public void APathACodeFenceAConfigLine_AllPass_Unchanged(string context)
    {
        // The code ban is the WEB row's; a none row on a hosted runtime may carry code and paths (A9).
        SecretCheck.Inspect(context).Should().BeOfType<SecretCheckResult.Clean>()
            .Which.Context.Text.Should().Be(context, "nothing is redacted");
    }

    [Theory]
    [InlineData("Authorization: Bearer abcdefghijklmnopqrstuvwxyz0123456789", "bearer")]
    [InlineData("the key is sk-live-0123456789abcdefghijklmnop and it still 401s", "vendor-key")]
    [InlineData("ghp_0123456789abcdefghijklmnopqrstuvwxyz is the token I used", "vendor-key")]
    [InlineData("connect with https://deploy:hunter22secret@registry.example.com/v2", "url-authority")]
    [InlineData("I call GET /x?api_key=abc123def456 and it works", "parameter")]
    [InlineData("the config has password: hunter22secret in it", "labelled")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEA\n-----END RSA PRIVATE KEY-----", "private-key")]
    public void ASecretShape_IsRefused_NamingItsClass_AndNeverQuotingIt(string context, string cls)
    {
        var refused = SecretCheck.Inspect(context).Should().BeOfType<SecretCheckResult.Refused>().Which;

        refused.Class.Should().Be(cls);
        refused.Cure.Should().Contain("placeholder", "the consultant rule's own cure: a placeholder, and say you did");
        foreach (var secret in (string[])["abcdefghijklmnopqrstuvwxyz0123456789", "sk-live-0123456789abcdefghijklmnop", "ghp_0123456789abcdefghijklmnopqrstuvwxyz", "hunter22secret", "abc123def456", "MIIEowIBAAKCAQEA"])
        {
            refused.Reason.Should().NotContain(secret, "a refusal is written down");
            refused.Cure.Should().NotContain(secret);
        }
    }

    [Fact]
    public void TheClassIsTheFirstPassThatWouldHaveRedacted_InTheRedactionsOwnOrder()
    {
        // The notice redaction runs the private-key pass first, then parameter, url-authority, bearer,
        // vendor-key, labelled — one road for recognising a secret, not a second list of shapes.
        SecretCheck.WhichSecret("-----BEGIN PRIVATE KEY-----\nMIIE\n-----END PRIVATE KEY-----\n?token=abc123def456").Should().Be("private-key");
        SecretCheck.WhichSecret("?token=abc123def456 Bearer abcdefghijklmnopqrstuvwxyz0123456789").Should().Be("parameter");
        SecretCheck.WhichSecret("nothing here").Should().BeEmpty();
    }

    [Fact]
    public void ARefusedContext_HasNoCheckedContextToCarry()
    {
        // The prompt composer takes a CheckedContext and nothing else, so "after SecretCheck" is a
        // type: a refused context cannot reach a prompt because there is no value to hand it.
        SecretCheck.Inspect("sk-live-0123456789abcdefghijklmnop").Should().NotBeOfType<SecretCheckResult.Clean>();
        typeof(CheckedContext).GetConstructors().Should().BeEmpty("only the check may build one");
    }
}

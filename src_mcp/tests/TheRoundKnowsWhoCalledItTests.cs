using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Which AI called the gate, and which model it says it is running.
/// </summary>
/// <remarks>
/// <para><b>Issue #174.</b> "I used to work only with Claude. Now there is Codex Astra as well, and
/// I need to track through the MCP — if possible — who is calling, and which model."</para>
///
/// <para><b>Two halves with two different sources.</b> WHO is answerable from what is already here:
/// the MCP handshake carries the client (<c>claude-code</c>, <c>codex</c>), and four session
/// variables name a vendor each. WHICH MODEL is in neither — no field of the protocol carries it —
/// so the calling AI DECLARES it on <c>open</c>. Decided by the operator on 2026-09-13; the
/// alternative, an environment variable beside <c>COAI_CALLER_SESSION</c>, is read once when the
/// client starts and would confidently name the model somebody switched away from.</para>
///
/// <para><b>What is never done here is guessing.</b> A caller that declares no model is recorded as
/// declaring none, and a caller nothing identifies is recorded as unknown. Both are states with a
/// name, because the only alternative is a blank that reads as "claude" to whoever looks.</para>
/// </remarks>
public sealed class TheRoundKnowsWhoCalledItTests
{
    /// <summary>An environment of exactly these variables, and nothing else.</summary>
    private static Func<string, string?> Env(params (string Name, string Value)[] vars) =>
        name => vars.FirstOrDefault(v => v.Name == name) is { Name: not null } hit ? hit.Value : null;

    [Theory]
    [InlineData("CLAUDE_CODE_SESSION_ID", "claude")]
    [InlineData("CODEX_SESSION_ID", "codex")]
    [InlineData("GEMINI_CLI_SESSION_ID", "gemini")]
    public void TheVariableThatMatched_NamesTheVendor(string variable, string vendor)
    {
        // It already walked these four and returned the value; the NAME of the one that matched was
        // dropped on the floor, and that name is the vendor half of what the issue asks for.
        var identity = CallerIdentity.From(Env((variable, "abc123")));

        identity.Vendor.Should().Be(vendor);
        identity.Id.Should().Be("abc123");
    }

    [Fact]
    public void AnOperatorSuppliedId_SaysSoRatherThanNamingAVendor()
    {
        // COAI_CALLER_SESSION exists so a client with no id of its own can be given one. It implies
        // nothing about which vendor that client is, and inventing one would be the guess this
        // whole change exists to avoid.
        var identity = CallerIdentity.From(Env(("COAI_CALLER_SESSION", "whoever")));

        identity.Vendor.Should().Be(CallerIdentity.Stated);
        identity.Id.Should().Be("whoever");
    }

    [Fact]
    public void ACallerNothingIdentifies_IsUnknown_NotBlank()
    {
        // A blank vendor renders as a gap, and a gap beside a model reads as "we know who this was".
        var identity = CallerIdentity.From(_ => null);

        identity.Vendor.Should().Be(CallerIdentity.Unknown);
        identity.Id.Should().BeEmpty();
    }

    [Fact]
    public void TheOperatorsOwnIdWins_AsItAlwaysHas()
    {
        var identity = CallerIdentity.From(Env(
            ("COAI_CALLER_SESSION", "chosen"), ("CLAUDE_CODE_SESSION_ID", "inherited")));

        identity.Id.Should().Be("chosen", "the order of the four variables is unchanged");
    }

    // ---------- the declaration: what the handshake says, and what the AI says ----------

    [Fact]
    public void TheHandshakeNamesTheClient_AndItsVersionIsKept()
    {
        var declared = CallerDeclaration.From(
            CallerIdentity.From(_ => null), client: "claude-code", clientVersion: "7.3.1", model: "claude-opus-5");

        declared.Client.Should().Be("claude-code");
        declared.ClientVersion.Should().Be("7.3.1");
        declared.Model.Should().Be("claude-opus-5");
    }

    [Theory]
    [InlineData("claude-code", "claude")]
    [InlineData("codex", "codex")]
    [InlineData("gemini-cli", "gemini")]
    [InlineData("antigravity", "antigravity")]
    public void TheClientFromTheHandshake_DecidesTheVendor(string client, string vendor)
    {
        // The handshake is negotiated on the connection that is calling RIGHT NOW. The environment
        // variables are the process's, inherited from whatever launched it, and a server that
        // outlives one client would keep answering with the launcher's vendor for ever. Raised as
        // Blocking by gemini on this change's plan round.
        CallerDeclaration.From(CallerIdentity.From(_ => null), client, "1.0", "").Vendor.Should().Be(vendor);
    }

    /// <summary>
    /// A client that identified itself and is not recognised is UNKNOWN — never the launcher's.
    /// </summary>
    /// <remarks>
    /// <para>Raised by codex on the second code round, and it is this change's own rule applied to
    /// its own fallback. The first build borrowed the environment's vendor whenever the handshake
    /// name was not on the list, so a server launched by Claude Code would record <c>claude</c> for
    /// a client calling itself <c>some-editor</c>. The environment belongs to whatever started the
    /// process; the handshake belongs to the connection that is calling, and a caller that HAS
    /// identified itself as something we do not know is unknown rather than the launcher.</para>
    /// <para>The name it gave is still kept verbatim, because that is the honest part.</para>
    /// </remarks>
    [Fact]
    public void AClientNobodyRecognises_IsUnknown_NotWhicheverVendorLaunchedUs()
    {
        var declared = CallerDeclaration.From(
            CallerIdentity.From(Env(("CODEX_SESSION_ID", "s"))), client: "some-editor", clientVersion: "2", model: "");

        declared.Vendor.Should().Be(CallerIdentity.Unknown);
        declared.Client.Should().Be("some-editor", "what it called itself is kept whether or not we know it");
    }

    [Fact]
    public void AClientNobodyRecognises_AndNoVariable_IsUnknown()
    {
        CallerDeclaration.From(CallerIdentity.From(_ => null), "some-editor", "2", "").Vendor
            .Should().Be(CallerIdentity.Unknown);
    }

    [Fact]
    public void WithNoClientAtAll_TheVariableIsWhatThereIs()
    {
        // The handshake said nothing, so the environment is the only evidence and using it is not a
        // guess. This is the case the fallback exists for, and the only one.
        CallerDeclaration.From(CallerIdentity.From(Env(("CODEX_SESSION_ID", "s"))), "", "", "").Vendor
            .Should().Be("codex");
    }

    /// <summary>
    /// An operator-supplied id is not a vendor, and is never rendered as one.
    /// </summary>
    /// <remarks>
    /// <c>COAI_CALLER_SESSION</c> yields the <c>stated</c> marker on the IDENTITY, which is useful
    /// there — it records which variable answered. As a VENDOR it would render "asked by stated",
    /// which reads as a vendor called "stated". The state has a word already. (gemini, round 2.)
    /// </remarks>
    [Fact]
    public void AnOperatorSuppliedId_NeverBecomesAVendorCalledStated()
    {
        var identity = CallerIdentity.From(Env(("COAI_CALLER_SESSION", "whoever")));
        identity.Vendor.Should().Be(CallerIdentity.Stated, "the identity still records which variable answered");

        CallerDeclaration.From(identity, "", "", "").Vendor.Should().Be(CallerIdentity.Unknown);
        CallerDeclaration.From(identity, "", "", "").Phrase.Should().Be("unknown · model not stated");
    }

    [Fact]
    public void AHandshakeThatCarriedNothing_DoesNotThrow_AndSaysNothing()
    {
        // `clientInfo` is optional in the protocol and the SDK's property is nullable, so the tool
        // lambda can be handed nulls. A round must still be recorded. (gemini, Major.)
        var declared = CallerDeclaration.From(CallerIdentity.From(_ => null), null!, null!, null!);

        declared.Client.Should().BeEmpty();
        declared.ClientVersion.Should().BeEmpty();
        declared.Model.Should().BeEmpty();
        declared.Vendor.Should().Be(CallerIdentity.Unknown);
    }

    // ---------- the model is declared, never defaulted ----------

    [Fact]
    public void ACallerThatDeclaresNoModel_RecordsNoModel()
    {
        // The whole point of the operator's choice. A default here would make every round from
        // every client claim a model nobody stated.
        CallerDeclaration.From(CallerIdentity.From(Env(("CLAUDE_CODE_SESSION_ID", "s"))), "claude-code", "1", "")
            .Model.Should().BeEmpty();
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void AModelOfNothingButWhitespace_IsNoModelAtAll(string model)
    {
        CallerDeclaration.From(default, "", "", model).Model.Should().BeEmpty();
    }

    [Fact]
    public void AModelIsTrimmed_BecauseItIsSomebodyElsesString()
    {
        CallerDeclaration.From(default, "", "", "  claude-opus-5  ").Model.Should().Be("claude-opus-5");
    }

    /// <summary>
    /// A bidirectional override cannot be smuggled into the name of who reviewed what.
    /// </summary>
    /// <remarks>
    /// Raised by gemini on the code round, and it is the one thing <c>char.IsControl</c> does not
    /// catch: U+202E is Unicode category <c>Cf</c>, not a control character, and it reverses
    /// everything after it. A caller is an external AI, and this string is rendered beside a vendor
    /// in a log people read to find out who reviewed what — so a model id that DISPLAYS as another
    /// one while the bytes say what they say is identity spoofing in exactly the record that exists
    /// to prevent it.
    /// </remarks>
    [Theory]
    [InlineData('‮')] // right-to-left override
    [InlineData('​')] // zero-width space
    [InlineData('‍')] // zero-width joiner
    [InlineData('﻿')] // zero-width no-break space
    public void AnInvisibleFormatCharacter_DoesNotSurviveIntoTheRecord(char invisible)
    {
        CallerDeclaration.From(default, "", "", "claude" + invisible + "-opus-5").Model
            .Should().Be("claude-opus-5");
    }

    [Fact]
    public void AModelThatIsNothingButInvisibleCharacters_IsNoModelAtAll()
    {
        CallerDeclaration.From(default, "", "", "‮​﻿").Model.Should().BeEmpty();
    }

    [Fact]
    public void APaddedLongModel_KeepsItsCharactersRatherThanItsPadding()
    {
        // The cap bounds the ALLOCATION as well as the field, so it is applied lazily — and a value
        // whose leading spaces were counted against the budget would lose real characters off the
        // end. Leading whitespace is dropped before the cap, which is what this pins.
        var declared = CallerDeclaration.From(default, "", "", new string(' ', 50) + new string('m', 120));

        declared.Model.Should().Be(new string('m', 120));
    }

    [Fact]
    public void AnAbsurdlyLongModel_IsCapped_NotStored()
    {
        // Freeform input declared by an external AI, written to a session file and rendered on a
        // page. A cap is the difference between a field and an injection point. (gemini, Minor.)
        var declared = CallerDeclaration.From(default, "", "", new string('m', 5000));

        declared.Model.Length.Should().Be(CallerDeclaration.LongestField);
    }

    [Fact]
    public void ControlCharacters_DoNotSurviveIntoTheRecord()
    {
        var declared = CallerDeclaration.From(default, "", "", "claude" + (char)13 + (char)10 + "-opus 5");

        // A SPACE is not a control character: "gpt 5 codex" is a thing somebody may declare.
        declared.Model.Should().Be("claude-opus 5", "a newline in a log line is a second log line");
    }

    // ---------- how it reads when somebody looks at it ----------

    [Fact]
    public void AStatedModel_ReadsAsTheClientAndTheModel()
    {
        CallerDeclaration.From(default, "claude-code", "7.3.1", "claude-opus-5").Phrase
            .Should().Be("claude-code 7.3.1 · claude-opus-5");
    }

    [Fact]
    public void NoModel_ReadsAsNotStated_RatherThanAsAGap()
    {
        CallerDeclaration.From(default, "codex", "0.9", "").Phrase.Should().Be("codex 0.9 · model not stated");
    }

    [Fact]
    public void NoClientEither_ReadsAsTheVendorWeDoHave()
    {
        CallerDeclaration.From(CallerIdentity.From(Env(("CODEX_SESSION_ID", "s"))), "", "", "").Phrase
            .Should().Be("codex · model not stated");
    }

    [Fact]
    public void NothingAtAll_SaysUnknown()
    {
        CallerDeclaration.From(default, "", "", "").Phrase.Should().Be("unknown · model not stated");
    }

    [Fact]
    public void AClientWithNoVersion_IsNotFollowedByAGap()
    {
        CallerDeclaration.From(default, "claude-code", "", "claude-opus-5").Phrase
            .Should().Be("claude-code · claude-opus-5");
    }
}

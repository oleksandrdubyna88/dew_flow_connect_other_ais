using System.Text;
using CoaiMcp.Core.Notices;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A credential NAMED on one line is redacted when its value stands on the NEXT line
/// (todo/PLAN_redaction_reaches_a_value_on_the_next_line.md, S-1 to S-7, S-11, S-13 to S-16).
/// </summary>
/// <remarks>
/// <para><b>The defect.</b> The labelled rule is a one-line shape: its value class excludes every white
/// space character, a line break included. A served file's <c>"api_key":</c> followed by its value on the
/// next line leaked on the <see cref="Redaction.SafeSource"/> road, and a notice's block scalar leaked
/// its second line on the <see cref="Redaction.SafeText"/> road, where the strip glues the lines.</para>
/// <para><b>Every canary is fake</b> and carries <c>FAKE-</c>: nothing here looks like a real token to a
/// scanner. Every line break is a REAL one (a test string with <c>\n</c> as two characters is a different
/// shape, and the one-line rule already covers it).</para>
/// </remarks>
public sealed class ALabelledValueOnTheNextLineIsRedactedTests
{
    private const string Lf = "\n";

    private static string Source(string text) => Redaction.SafeSource(text);

    private static string Notice(string text) => Redaction.SafeText(text, Redaction.DetailLimit);

    private static string Lines(params string[] lines) => string.Join(Lf, lines);

    // ---------- S-1, S-3: the SafeSource road keeps the layout, and every next-line shape leaked ----------

    [Fact]
    public void R1_AJsonValueOnTheNextLine_IsRedacted_AndTheLineCountSurvives()
    {
        var given = Lines("{", "  \"api_key\":", "    \"FAKE-CANARY-0001\",", "}");

        var safe = Source(given);

        safe.Should().NotContain("FAKE-CANARY-0001");
        safe.Should().Be(Lines("{", "  \"api_key\":", "    \"[redacted]\",", "}"), "the layout, the quotes and the comma are kept");
    }

    [Theory]
    [InlineData("api_key:\n  FAKE-CANARY-0003", "api_key:\n  [redacted]", "R3 YAML, the value indented")]
    [InlineData("X-Api-Key:\n FAKE-CANARY-0005", "X-Api-Key:\n [redacted]", "R5 a folded header, one space")]
    [InlineData("X-Api-Key:\n     FAKE-CANARY-0006", "X-Api-Key:\n     [redacted]", "R6 a folded header, five spaces")]
    [InlineData("X-Api-Key:\n\tFAKE-CANARY-0005", "X-Api-Key:\n\t[redacted]", "R5 a folded header, a tab")]
    [InlineData("service:\n  api_key:\n    FAKE-CANARY-0010", "service:\n  api_key:\n    [redacted]", "R10 the inner pair of a nested YAML")]
    [InlineData("export TOKEN=\\\nFAKE-CANARY-0007", "export TOKEN=[redacted]\n[redacted]", "R7 a shell continuation (the labelled rule also takes the backslash, as it always did)")]
    public void EachShapeWithEvidence_HasItsValueRedacted_ThroughSafeSource(string given, string expected, string what)
    {
        var safe = Source(given);

        safe.Should().NotContain("FAKE-CANARY");
        safe.Should().Be(expected, what);
    }

    [Fact]
    public void ACrLfBreak_IsOneBreak_AndIsKept()
    {
        var given = "api_key:\r\n  FAKE-CANARY-0003\r\nname: visible\r\n";

        Source(given).Should().Be("api_key:\r\n  [redacted]\r\nname: visible\r\n");
    }

    [Fact]
    public void ALoneCarriageReturn_IsABreakToo()
    {
        Source("api_key:\r  FAKE-CANARY-0003\rname: visible").Should().Be("api_key:\r  [redacted]\rname: visible");
    }

    [Fact]
    public void AValueTokenIsTheRegexsOwnClass_SoTheRestOfTheLineIsKept()
    {
        var given = Lines("\"password\":", "   'FAKE-CANARY-0003', \"next\": 1");

        Source(given).Should().Be(Lines("\"password\":", "   '[redacted]', \"next\": 1"));
    }

    // ---------- S-2, S-4: the SafeText road drops the breaks and keeps the indentation ----------

    [Fact]
    public void R2_ANestedJsonValue_EightSpacesDeep_IsRedacted_OnTheNoticeRoad()
    {
        var given = Lines("{", "  \"nested\": {", "    \"api_key\":", "        \"FAKE-CANARY-0002\"", "  }", "}");

        var safe = Notice(given);

        safe.Should().NotContain("FAKE-CANARY-0002");
        safe.Should().Be("{  \"nested\": {    \"api_key\":        \"[redacted]\"  }}");
    }

    [Fact]
    public void R4_ABlockScalarsValue_IsRedacted_OnBothRoads()
    {
        var given = Lines("password: >", "  FAKE-CANARY-0004", string.Empty);

        var source = Source(given);
        var notice = Notice(given);

        source.Should().NotContain("FAKE-CANARY-0004");
        notice.Should().NotContain("FAKE-CANARY-0004");
        source.Split('\n').Should().HaveCount(3, "no line break is added or removed");
        source.Split('\n')[1].Should().Be("  [redacted]");
        notice.Should().Contain("[redacted]");
    }

    [Fact]
    public void R4_AGluedBlockIndicator_IsNotTakenForTheValue()
    {
        // The pre-glued shape of the two API quote sites: the gap is the indentation plus one.
        Notice("password: >  FAKE-CANARY-0004").Should().NotContain("FAKE-CANARY-0004");
    }

    [Fact]
    public void R4b_ATwoLineBlockScalar_HasNeitherLineReadable_OnBothRoads()
    {
        var given = Lines("password: |", "  FAKE-FIRST", "  FAKE-SECOND");

        var source = Source(given);
        var notice = Notice(given);

        source.Should().NotContain("FAKE-FIRST").And.NotContain("FAKE-SECOND");
        notice.Should().NotContain("FAKE-FIRST").And.NotContain("FAKE-SECOND");
        source.Split('\n').Should().HaveCount(3);
        notice.Split("[redacted]").Length.Should().BeGreaterThanOrEqualTo(3, "two placeholders: one per line of the scalar");
    }

    // ---------- S-5: the declines — green before and after ----------

    [Theory]
    [InlineData("password:\nthe one you chose last week", "a label then prose, no indentation and no quote")]
    [InlineData("the key\nto this is patience", "a label WORD with no joiner")]
    [InlineData("api_key:\n\n  x", "a blank line after a plain joiner")]
    [InlineData("api_key:", "a label at the end of the text")]
    [InlineData("api_key:\n", "a label and one trailing break")]
    [InlineData("api_key:\r\n", "a label and one trailing CRLF")]
    public void ADecline_LeavesTheTextUntouched_OnTheSourceRoad(string given, string what)
    {
        // `password:` as the FIRST line reads `password:` + LF + prose: the labelled rule cannot cross the
        // break and the pass has no evidence, so the text is what it was.
        Source(given).Should().Be(given, what);
    }

    [Fact]
    public void ANestedKeyWithItsValueOnItsOwnLine_StaysMasked_AndItsParentIsNotSwallowed()
    {
        // THE regression a crossing regex joiner causes (D2): `service:` is no credential, so it must never
        // take `api_key:` for its value. Green today and it MUST stay green.
        var given = Lines("service:", "  api_key: FAKE-CANARY-0012");

        var safe = Source(given);

        safe.Should().NotContain("FAKE-CANARY-0012");
        safe.Should().Be(Lines("service:", "  api_key: [redacted]"));
    }

    [Fact]
    public void ALabelThatIsNotACredential_NeverReachesTheNextLine()
    {
        var given = Lines("title:", "  FAKE-CANARY-9999");

        Source(given).Should().Be(given);
    }

    [Fact]
    public void ATwoBreakGap_BetweenAPlainLabelAndItsValue_Declines()
    {
        // D11 (e), recorded: no evidence reaches across two line breaks of a PLAIN continuation.
        var given = "api_key:" + Lf + Lf + "  FAKE-CANARY-9998";

        Source(given).Should().Be(given);
    }

    [Fact]
    public void ALabelAfterALabelWithNoDeeperIndent_IsLeftToTheLabelledRule()
    {
        var given = Lines("api_key:", "name: visible");

        Source(given).Should().Be(given);
    }

    // ---------- S-13: a credential-NAMED container keeps its child's label (green before and after) ----------

    [Fact]
    public void ACredentialNamedContainer_KeepsItsChildsLabel_OnTheSourceRoad()
    {
        // Only the SOURCE half of the plan's S-13. On the notice road the strip glues the lines into
        // `credentials:  password: FAKE-CANARY-CHILD`, whose leftmost match starts at `credentials` and takes
        // `password:` for its value — the one-line rule's leftmost-match swallow, which the plan gives to its
        // gated story S4 (D10). It leaks today and still does after S1+S2; measured, and recorded in the plan.
        var given = Lines("credentials:", "  password: FAKE-CANARY-CHILD");

        Source(given).Should().Be(Lines("credentials:", "  password: [redacted]"));
    }

    [Fact]
    public void AChildAssignmentWithItsOwnQuote_IsStillALabel()
    {
        var given = Lines("secrets:", "  \"password\": \"FAKE-CANARY-CHILD\"");

        Source(given).Should().Be(Lines("secrets:", "  \"password\": \"[redacted]\""));
    }

    [Fact]
    public void ABase64ValueEndingInPadding_IsNotTakenForAChildLabel()
    {
        // `abc=` + `=` is padding, not an assignment: a child-label reading would decline and leave a
        // secret on its own line readable.
        var given = Lines("authorization:", "  FAKE0CANARY0BASE64==");

        Source(given).Should().NotContain("FAKE0CANARY0BASE64");
    }

    // ---------- S-14: blank lines inside a block ----------

    [Fact]
    public void ABlankLineInsideABlockScalar_DoesNotEndTheBlock()
    {
        var given = Lines("password: |", "  FAKE-FIRST", string.Empty, "  FAKE-SECOND", "ordinary: visible");

        var safe = Source(given);

        safe.Should().NotContain("FAKE-FIRST").And.NotContain("FAKE-SECOND");
        safe.Split('\n').Should().HaveCount(5, "five lines in, five out");
        safe.Split('\n')[1..].Should().Equal("  [redacted]", string.Empty, "  [redacted]", "ordinary: visible");
    }

    [Fact]
    public void ABlockEndsAtTheFirstLineThatIsNotDeeper_AndWhatFollowsSurvives()
    {
        var given = Lines("config:", "  password: |", "    FAKE-FIRST", "    FAKE-SECOND", "  name: visible", "other: visible");

        var safe = Source(given);

        safe.Should().NotContain("FAKE-");
        safe.Split('\n')[2..].Should().Equal("    [redacted]", "    [redacted]", "  name: visible", "other: visible");
    }

    [Fact]
    public void AnIndicatorBlock_WithAnAssignmentShapedLine_IsStillValue()
    {
        // Inside a BLOCK every line is value, assignment-shaped or not (D3).
        var given = Lines("password: |", "  user=FAKE-X", "  pass=FAKE-Y");

        Source(given).Should().NotContain("FAKE-").And.NotContain("user=").And.NotContain("pass=");
    }

    // ---------- S-15: compound labels as continuation labels ----------

    [Theory]
    [InlineData("db_password:\n  FAKE-CANARY-0015")]
    [InlineData("apiKey:\n  \"FAKE-CANARY-0015\"")]
    [InlineData("X-Api-Key:\n FAKE-CANARY-0015")]
    [InlineData("\"client_secret\":\n    \"FAKE-CANARY-0015\"")]
    [InlineData("refresh_token =\n  FAKE-CANARY-0015")]
    [InlineData("'api_key' :\n  FAKE-CANARY-0015")]
    public void ACompoundLabel_ReachesItsNextLineValue(string given)
    {
        Source(given).Should().NotContain("FAKE-CANARY-0015");
    }

    [Fact]
    public void ALabelRunLongerThanItsBound_IsReadFromItsLast64Characters()
    {
        // The regex's leftmost start produces the LAST 64 label characters; `x` * 100 + `password` is a name
        // the continuation pass reads as `xxx…password` and which names a credential by its tail.
        var given = new string('x', 100) + "_password:" + Lf + "  FAKE-CANARY-0015";

        Source(given).Should().NotContain("FAKE-CANARY-0015");
    }

    // ---------- S-16: TOML triple quotes, and the one-line quote class ----------

    [Fact]
    public void R11_ATomlTripleQuotedValue_IsRedacted_AndItsClosingQuotesAreKept()
    {
        var given = Lines("password = \"\"\"", "FAKE-CANARY-0011", "\"\"\"", "name = \"visible\"");

        var safe = Source(given);

        safe.Should().NotContain("FAKE-CANARY-0011");
        safe.Should().Be(Lines("password = \"\"\"", "[redacted]", "\"\"\"", "name = \"visible\""));
    }

    [Fact]
    public void ATripleQuotedValue_OfSeveralLines_AndATextBeforeTheClosingQuotes_IsAllRedacted()
    {
        var given = Lines("secret = '''", "  FAKE-FIRST", "", "  FAKE-SECOND'''  # note", "after = 1");

        var safe = Source(given);

        safe.Should().NotContain("FAKE-");
        safe.Should().Be(Lines("secret = '''", "  [redacted]", "", "  [redacted]'''  # note", "after = 1"));
    }

    [Fact]
    public void AnUnclosedTripleQuote_RedactsToTheEndOfTheText()
    {
        var given = Lines("password = \"\"\"", "FAKE-FIRST", "FAKE-SECOND");

        Source(given).Should().NotContain("FAKE-");
    }

    [Theory]
    [InlineData("password='FAKE-CANARY-0016'", "password='[redacted]'")]
    [InlineData("password = \"\"\"FAKE-CANARY-0016\"\"\"", "password = \"\"\"[redacted]\"\"\"")]
    [InlineData("'client_secret': 'FAKE-CANARY-0016'", "'client_secret': '[redacted]'")]
    public void AnySingleOrTripleQuotedValue_OnOneLine_IsRedacted_OnBothRoads(string given, string expected)
    {
        Notice(given).Should().Be(expected);
        Source(given).Should().Be(expected);
    }

    // ---------- the joiner's gap ----------

    [Fact]
    public void AGapOfThirtyTwo_AfterTheJoiner_IsStillTheValuesGap()
    {
        var given = "api_key:" + new string(' ', 32) + "FAKE-CANARY-0017";

        Notice(given).Should().NotContain("FAKE-CANARY-0017");
    }

    [Fact]
    public void FiveSpacesBeforeTheJoiner_StillStay_AsPinned()
    {
        Notice("password     =x").Should().Be("password     =x");
    }

    // ---------- S-7: the secret check answers what the redaction would take ----------

    [Fact]
    public void R1_IsRecognisedAsALabelledSecret_ByTheSecretCheck()
    {
        var given = Lines("{", "  \"api_key\":", "    \"FAKE-CANARY-0001\",", "}");

        Redaction.FirstSecretClass(given).Should().Be("labelled");
    }

    [Theory]
    [InlineData("\"private_key\":\n\"-----BEGIN PRIVATE KEY-----\nFAKEBODYFAKEBODY\n-----END PRIVATE KEY-----\"", "private-key", "a PEM block whose label continues")]
    [InlineData("api_key:\n  FAKE-CANARY-0001\nGET /x?access_token=x", "labelled", "a continuation beside a query credential")]
    [InlineData("GET /x?access_token=x", "parameter", "a query credential alone")]
    [InlineData("the key\nto this is patience", "", "nothing")]
    public void ThePrecedenceOfTheClasses_IsPinned(string given, string expected, string what)
    {
        Redaction.FirstSecretClass(given).Should().Be(expected, what);
    }

    // ---------- the pass order on the notice road ----------

    [Fact]
    public void ARealLineBreakPrivateKey_InANotice_LosesItsBody_OnePlaceholderPerBodyLine()
    {
        // D5, the one deliberate output change: the block pass moved ahead of the strip, so a key with REAL
        // line breaks keeps its header, its footer and one placeholder per body line, glued by the strip.
        var key = "-----BEGIN PRIVATE KEY-----\nFAKEKEYBODYLINEONE\nFAKEKEYBODYLINETWO\n-----END PRIVATE KEY-----";

        var safe = Notice("the vendor answered: " + key + " — check it");

        safe.Should().NotContain("FAKEKEYBODY");
        safe.Should().Be("the vendor answered: -----BEGIN PRIVATE KEY-----[redacted][redacted]-----END PRIVATE KEY----- — check it");
    }

    [Fact]
    public void AControlCharacterInsideAFooterlessKeyBody_DoesNotEndTheRun()
    {
        // The strip still removes the control bytes BEFORE the block pass reads them (the layout — tab, LF,
        // CR — is what waits for after the continuation pass): a footerless body with a stray byte in it is
        // one run, exactly as it was when the strip ran first. Green before and after.
        var cut = "-----BEGIN PRIVATE KEY-----" + Lf + "FAKEKEYBODYLINEONE" + char.ConvertFromUtf32(1) + "FAKEKEYBODYLINETWO";

        Notice(cut).Should().NotContain("FAKEKEYBODYLINETWO");
    }

    [Fact]
    public void ANoticeWithNoLineBreak_IsNotChangedByThePass()
    {
        var given = "api_key: abc123 and { \"client_secret\": \"s3cr3t-value\" } and PASSWORD = hunter2";

        Notice(given).Should().Be("api_key: [redacted] and { \"client_secret\": \"[redacted]\" } and PASSWORD = [redacted]");
    }

    // ---------- S-11: confidentiality PAST the old block cap — content, not time ----------

    [Fact]
    public void ABlockScalar_OfTwoHundredSixtyTwoThousandCharacters_HasNoCanaryAnywhere()
    {
        var (given, canaries) = ALongBlockScalar();

        var safe = Source(given);
        var cls = Redaction.FirstSecretClass(given);

        FirstCanary(safe).Should().Be("none", "a block ends by its STRUCTURE, so its tail past 65 536 characters is value too ({0} canaries were planted, one every 1 000 characters)", canaries);
        safe.Should().Contain("ordinary: visible", "the line after the block is not part of it");
        safe.Split('\n').Length.Should().Be(given.Split('\n').Length, "the line count survives");
        cls.Should().Be("labelled");
    }

    /// <summary>The first canary still readable in <paramref name="text"/> — the message of a failure names how far in it is, and nothing bulky.</summary>
    private static string FirstCanary(string text)
    {
        var at = text.IndexOf("FAKE-CANARY-DEEP", StringComparison.Ordinal);

        return at < 0 ? "none" : text.Substring(at, text.IndexOf(' ', at) - at) + $" (at index {at})";
    }

    private static (string Text, int Canaries) ALongBlockScalar()
    {
        var text = new StringBuilder("password: |").Append('\n');
        var canaries = 0;
        for (var line = 0; text.Length < 262_000; line++)
        {
            text.Append("  ");
            if (line % 10 == 9)
            {
                text.Append("FAKE-CANARY-DEEP-").Append(++canaries).Append(' ');
            }

            text.Append('x', 100).Append('\n');
        }

        text.Append("ordinary: visible");

        return (text.ToString(), canaries);
    }

    [Fact]
    public void AFileAroundABlockScalar_KeepsEverythingOutsideIt()
    {
        // The text before and after a block scalar is not touched.
        var given = Lines("name: visible", "api_key: |", "  FAKE-FIRST", "version: 2");

        Source(given).Should().Be(Lines("name: visible", "api_key: [redacted]", "  [redacted]", "version: 2"));
    }
}

using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CoaiMcp.Core.Notices;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The server's notice line is the extension's line — held as bytes, not as a resemblance.
/// </summary>
/// <remarks>
/// <para><b>Every expected string here was produced by node</b>, through the extension's compiled
/// <c>safeText</c> and <c>notificationLine</c> (2026-09-21, node 24), and copied in — never derived
/// from what the .NET code happened to return. A test whose expectation comes from the code under
/// test proves the code agrees with itself.</para>
/// <para>What these tests cannot do is drive the TypeScript serialiser: CI runs the .NET tests before
/// <c>npm ci</c>, and the plan forbids a shared list of vectors in words. So the LIVE agreement — 395
/// generated cases and four whole records, diffed byte for byte across both runtimes — was measured
/// by a scratch harness outside the repository on the day this was written (0 differences), and the
/// cases pinned below are the ones that carry a decision: each shape, each language, each place .NET
/// and JavaScript were measured to disagree before the port answered it.</para>
/// </remarks>
public sealed class ServerNoticeLineTests
{
    private const string Secret = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>The U+2026 HORIZONTAL ELLIPSIS suffix, spelled by number so this file never carries it raw.</summary>
    private static readonly string TruncatedSuffix = char.ConvertFromUtf32(0x2026) + "(truncated)";

    private static string U(int unit) => ((char)unit).ToString();

    private static string CP(int codePoint) => char.ConvertFromUtf32(codePoint);

    private static string Safe(string value) => Redaction.SafeText(value, Redaction.TitleLimit);

    /// <summary>Every string field this record type has, filled — so the sweep below has something to sweep.</summary>
    private static ServerNotice EveryFieldFilled(string value) => new()
    {
        Utc = "2026-09-16T17:05:39.812Z",
        Class = "stand-down",
        Source = value,
        Code = value,
        Subject = value,
        Title = value,
        Detail = value,
        Cure = value,
        Action = value,
        Offered = value,
        Answer = value,
        Run = value,
        Repo = value,
        Branch = value,
        Session = value,
        Provider = value,
        Role = value,
        Pid = 37308,
        Seq = 10,
        Bound = 100,
        More = new Dictionary<string, object> { ["whatTheServerSent"] = value, ["n"] = 7 },
    };

    private static JsonElement Parsed(string line) => JsonDocument.Parse(line).RootElement.Clone();

    [Fact]
    public void NoStringFieldOfARecordReachesTheLine_CarryingASecret_IdentityFieldsIncluded()
    {
        // The extension's own first test, and the one that rejected the exemption for `source`,
        // `code` and `run`: a secret in EVERY string field, and none of it on the line. The fields are
        // read off the record type rather than named, so a field added next year is swept the day it
        // is added — the defect the plan round found in the plan's own first draft.
        var record = EveryFieldFilled($"something went wrong: Bearer {Secret}");

        var line = ServerNoticeLine.Of(record);

        line.Should().NotContain(Secret);
        line.Should().Contain("[redacted]");
        var strings = typeof(ServerNotice).GetProperties()
            .Where(p => p.PropertyType == typeof(string) && (string?)p.GetValue(record) is { } s && s.Contains(Secret))
            .Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..])
            .ToList();
        strings.Should().HaveCountGreaterThanOrEqualTo(15, "the record must have string fields to sweep");
        var back = Parsed(line);
        foreach (var field in strings.Append("whatTheServerSent"))
        {
            back.GetProperty(field).GetString().Should().NotContain(Secret, $"{field} kept the secret");
        }
    }

    [Theory]
    [InlineData("Authorization: Bearer abcdefghijkl", "Authorization: [redacted] [redacted]")]
    [InlineData("https://user:pw@host/x", "https://[redacted]@host/x")]
    [InlineData("failed against https://someone:hunter2@coai.remsoft.dev/api", "failed against https://[redacted]@coai.remsoft.dev/api")]
    [InlineData("?api-version=2024-02-01", "?api-version=2024-02-01")]
    [InlineData("GET /models?api-version=2024-02-01&api_key=abcd1234efgh", "GET /models?api-version=2024-02-01&api_key=[redacted]")]
    [InlineData("?access_token=xyz", "?access_token=[redacted]")]
    [InlineData("password=letmein", "password=[redacted]")]
    [InlineData("api_key: abc123def456 and {\"client_secret\": \"s3cr3t-value\"} and PASSWORD = hunter2",
        "api_key: [redacted] and {\"client_secret\": \"[redacted]\"} and PASSWORD = [redacted]")]
    [InlineData("the cli said: sk-abcdefghijklmnop", "the cli said: sk-[redacted]")]
    [InlineData("the cli said: ghp_abcdefghijklmnop", "the cli said: ghp_[redacted]")]
    [InlineData("the cli said: gho_abcdefghijklmnop", "the cli said: gho_[redacted]")]
    [InlineData("the cli said: ghu_abcdefghijklmnop", "the cli said: ghu_[redacted]")]
    [InlineData("the cli said: ghs_abcdefghijklmnop", "the cli said: ghs_[redacted]")]
    [InlineData("the cli said: github_pat_abcdefghijklmnop", "the cli said: github_pat_[redacted]")]
    [InlineData("the cli said: xoxb-abcdefghijklmnop", "the cli said: xoxb-[redacted]")]
    [InlineData("Token abcdefgh", "Token [redacted]")]
    [InlineData("token=abcdefgh", "token=[redacted]")]
    [InlineData("x-auth-token: abcdefghijkl", "x-auth-token: [redacted]")]
    public void EachShape_IsRedactedExactlyAsTheExtensionRedactsIt(string given, string expected)
    {
        // `Authorization: Bearer …` comes back with TWO [redacted]: the bearer pattern takes the
        // value, then the labelled pass sees `Authorization: Bearer` and takes that too. The
        // extension's docstring claims the opposite; its code does this, and the code is the contract.
        Safe(given).Should().Be(expected);
    }

    [Theory]
    [InlineData("author=octocat and api-version=2024-02-01 and count: 17")]
    [InlineData("could not reach https://coai.example.com:8443/api/v1/rounds")]
    [InlineData("SK-abcdefghijkl")]
    [InlineData("tokens abcdefgh")]
    [InlineData("Set-Cookie: session=abc")]
    [InlineData("1https://u:p@h")]
    [InlineData("password     =x")]
    public void AnOrdinaryString_IsLeftExactlyAlone(string given)
    {
        // Each of these is a place a looser port would have fired: the vendor pattern carries no
        // case flag so `SK-` is not `sk-`; `tokens` is not `token` followed by a space; `session` is
        // not a credential word; a digit before `https` is inside a word, so there is no boundary;
        // five spaces are more than the joiner's `{0,4}`.
        Safe(given).Should().Be(given);
    }

    [Theory]
    [InlineData("?sig=abc&author=me", "?sig=[redacted]")]
    [InlineData("https://x:y@h?token=1#secret=2", "https://[redacted]@h?token=[redacted]#secret=[redacted]")]
    [InlineData("https://u:p@https://u:p@host", "https://[redacted]@https://[redacted]@host")]
    public void TheOrderOfThePasses_IsTheContract(string given, string expected)
    {
        // `?sig=abc&author=me` loses `&author=me` as well: the parameter pass leaves
        // `?sig=[redacted]&author=me`, and the labelled pass, running LAST, reads `sig=` with a value
        // that runs to the end of the string. The second URL survives its own pass because the
        // authority pattern ends on `@` and the next one starts right after it — exactly what a
        // consumed-prefix imitation of `\b` would have missed, which is why the port uses a lookbehind.
        Safe(given).Should().Be(expected);
    }

    [Theory]
    [InlineData("Ошибка: Authorization: Bearer abcdefghijkl при запросе к https://someone:hunter2@coai.remsoft.dev/api?api-version=2024-02-01&access_token=xyz",
        "Ошибка: Authorization: [redacted] [redacted] при запросе к https://[redacted]@coai.remsoft.dev/api?api-version=2024-02-01&access_token=[redacted]")]
    [InlineData("Fehler: password=letmein für den Schlüssel sk-abcdefghijklmnop, Größe 12",
        "Fehler: password=letmein für den Schlüssel sk-[redacted], Größe 12")]
    [InlineData("парольsk-abcdefghijkl", "парольsk-[redacted]")]
    [InlineData("Tokenßsk-abcdefghijkl", "Tokenßsk-[redacted]")]
    public void AroundCyrillicAndGermanProse_TheBoundaryIsJavaScriptsASCIIOne(string given, string expected)
    {
        // THE LARGEST KNOWN RISK OF THIS PORT, measured. .NET's `\b` is Unicode-aware, so a key glued
        // to a Cyrillic word or to ß has no boundary before it here — and the extension, whose `\b`
        // is ASCII without the `u` flag, redacts it. The last two rows were NOT redacted by a `\b`
        // port; the lookbehind `(?<![A-Za-z0-9_])` is the extension's boundary spelled out.
        //
        // The German row keeps `password=letmein`, and that is the CONTRACT, not a miss of the port:
        // the labelled pattern's leftmost match starts at `Fehler`, an ordinary word, whose value
        // `password=letmein` runs to the next space — so the pair is swallowed inside a match that
        // is not a credential and never seen on its own. node returns exactly this; the first draft
        // of this row expected the pair redacted and the test said no.
        Safe(given).Should().Be(expected);
    }

    [Fact]
    public void IgnoreCaseIsSpelledOut_BecauseDotNetPairsKWithTheKelvinSign()
    {
        // Measured: under RegexOptions.IgnoreCase the invariant culture pairs `k` with U+212A, so
        // `to‹KELVIN›en abcdefgh` would have been redacted here and is kept by the extension, whose
        // `i` without `u` pairs ASCII only. The words are spelled as [Tt][Oo][Kk][Ee][Nn] instead.
        var kelvin = CP(0x212A);

        Safe("to" + kelvin + "en abcdefgh").Should().Be("to" + kelvin + "en abcdefgh");
        Safe("basic abcde" + kelvin + "gh").Should().Be("basic abcde" + kelvin + "gh");
        Safe("BEARER abcdefgh").Should().Be("BEARER [redacted]");
        Safe("bEaReR abcdefgh").Should().Be("bEaReR [redacted]");
    }

    [Fact]
    public void WhitespaceIsJavaScripts_NELIsNotSpaceAndTheByteOrderMarkIs()
    {
        // Measured: .NET's `\s` admits U+0085 and refuses U+FEFF; JavaScript's does the opposite.
        // A password carrying a NEL survived a port that wrote `\s`.
        Safe("https://u:p" + U(0x85) + "@host").Should().Be("https://[redacted]@host");
        Safe("https://u:p" + U(0xFEFF) + "@host").Should().Be("https://u:p" + U(0xFEFF) + "@host");
    }

    [Fact]
    public void ControlCharactersAreRemovedByCodePoint_NULAndDELIncluded()
    {
        // By NUMBER, never a literal or a class: the extension's comment records a NUL byte reaching
        // a source file twice that way, and CredentialWords.cs beside this code carries one today.
        Safe("before" + U(0) + U(7) + "after").Should().Be("beforeafter");
        Safe("x" + U(127) + "y").Should().Be("xy");
        Safe("a" + U(0) + "b" + U(31) + "c" + U(32) + "d" + U(127) + "e" + U(128) + "f").Should().Be("abc de" + U(128) + "f");
        Redaction.IsPrintable((char)31).Should().BeFalse();
        Redaction.IsPrintable((char)32).Should().BeTrue();
        Redaction.IsPrintable((char)127).Should().BeFalse();
        Redaction.IsPrintable((char)128).Should().BeTrue();
    }

    [Fact]
    public void ADetailOf5000_IsCutAt4096_AndATitleOf1001_At1000()
    {
        var detail = Redaction.SafeText(new string('x', 5000), Redaction.LimitFor("detail"));
        var title = Redaction.SafeText(new string('x', 1001), Redaction.LimitFor("title"));

        detail.Should().StartWith(new string('x', 4096)).And.EndWith(TruncatedSuffix);
        title.Should().StartWith(new string('x', 1000)).And.EndWith(TruncatedSuffix);
        Redaction.LimitFor("detail").Should().Be(4096);
        Redaction.LimitFor("anythingElse").Should().Be(1000);
    }

    [Fact]
    public void TheSuffixIsAddedAfterTheCut_SoATruncatedValueIsLongerThanItsLimit()
    {
        // The extension writes `named.slice(0, limit) + TRUNCATED` — the suffix is not inside the
        // budget. node: a 5000-character detail comes back 4108 long, a 1001-character title 1012.
        Redaction.SafeText(new string('x', 5000), 4096).Should().HaveLength(4108);
        Redaction.SafeText(new string('x', 1001), 1000).Should().HaveLength(1012);
        Redaction.SafeText(new string('x', 1000), 1000).Should().HaveLength(1000, "exactly at the limit is not cut");
        Redaction.Truncated.Should().HaveLength(12);
        Redaction.Truncated[0].Should().Be((char)0x2026, "the suffix opens with ONE ellipsis character, not three full stops");
    }

    [Fact]
    public void ACutInsideAnEmoji_LeavesTheLoneSurrogate_AndTheLineEscapesItAsJavaScriptDoes()
    {
        // `slice(0, 1000)` on 999 letters and an emoji keeps the high surrogate alone; node then
        // writes it as lowercase `\ud83d`. Utf8JsonWriter would have written `�` (measured),
        // which is why the quoting is written by hand.
        var title = Redaction.SafeText(new string('a', 999) + CP(0x1F600), Redaction.TitleLimit);

        title.Should().HaveLength(1012);
        title[999].Should().Be((char)0xD83D);
        var line = ServerNoticeLine.Of(new() { Utc = "u", Class = "failure", Source = "s", Code = "c", Title = title });
        line.Should().Contain("aaa\\ud83d" + TruncatedSuffix + "\"}");
    }

    [Fact]
    public void MoreIsFlattenedBeforeRedaction_AndNeverAppearsAsAField()
    {
        var line = ServerNoticeLine.Of(new()
        {
            Utc = "u", Class = "failure", Source = "s", Code = "c",
            More = new Dictionary<string, object> { ["whatTheServerSent"] = "the call failed with api_key: abc123def456" },
        });

        line.Should().NotContain("abc123def456");
        line.Should().Contain("\"whatTheServerSent\":\"the call failed with api_key: [redacted]\"");
        Parsed(line).TryGetProperty("more", out _).Should().BeFalse("the bag is an implementation detail, not a field");
    }

    [Fact]
    public void ANumberInMore_PassesThroughAsANumber()
    {
        var line = ServerNoticeLine.Of(new()
        {
            Utc = "u", Class = "failure", Source = "s", Code = "c",
            More = new Dictionary<string, object> { ["n"] = 7, ["ratio"] = 0.1 + 0.2, ["big"] = 9007199254740992L },
        });

        line.Should().Contain("\"n\":7,\"ratio\":0.30000000000000004,\"big\":9007199254740992");
        Parsed(line).GetProperty("n").ValueKind.Should().Be(JsonValueKind.Number);
    }

    [Theory]
    [InlineData(37308d, "37308")]
    [InlineData(1.5, "1.5")]
    [InlineData(7.0, "7")]
    [InlineData(-0.0, "0")]
    [InlineData(-1.5, "-1.5")]
    [InlineData(1e21, "1e+21")]
    [InlineData(1e20, "100000000000000000000")]
    [InlineData(1e15, "1000000000000000")]
    [InlineData(1e-7, "1e-7")]
    [InlineData(0.000001, "0.000001")]
    [InlineData(0.000001234, "0.000001234")]
    [InlineData(5e-324, "5e-324")]
    [InlineData(1.7976931348623157e308, "1.7976931348623157e+308")]
    [InlineData(123456789012345680000d, "123456789012345680000")]
    public void ADoubleIsWrittenAsJavaScriptWritesIt(double value, string expected)
    {
        // Every expectation is JSON.stringify's output from node. .NET's own writer gives 1E+21,
        // 1E-07 and -0 for three of these rows (measured).
        ServerNoticeLine.JsNumber(value).Should().Be(expected);
    }

    [Fact]
    public void EveryCodeThisServerCanWrite_IsUnchangedByTheRedactor()
    {
        // The guard that replaced an exemption. `class`, `source`, `code` and `run` are literals, and
        // the code round wanted them exempt so that a persisted key could never stop matching the key
        // the suppressor admitted; the invariant "every string field is redacted" was worth more. So
        // the concern is answered here instead: a code the redactor would rewrite fails on the day it
        // is added.
        ServerNoticeCodes.All.Should().HaveCountGreaterThanOrEqualTo(14);
        foreach (var code in ServerNoticeCodes.All)
        {
            Safe(code).Should().Be(code, $"redaction rewrites the code {code}");
        }
    }

    [Fact]
    public void TheCatalogListsEveryConstant_SoTheGuardAboveCannotMissOne()
    {
        var constants = typeof(ServerNoticeCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        ServerNoticeCodes.All.Should().BeEquivalentTo(constants, "a constant missing from All is a code the redactor guard never sees");
        ServerNoticeCodes.All.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void EveryPattern_IsNonBacktracking_OrSaysExactlyWhyItCannotBe()
    {
        var patterns = Redaction.Patterns;

        patterns.Should().HaveCount(5, "one parameter pattern, three secret shapes, one labelled pattern");
        foreach (var pattern in patterns)
        {
            var nonBacktracking = pattern.Regex.Options.HasFlag(RegexOptions.NonBacktracking);
            (nonBacktracking || pattern.WhyNotNonBacktracking.StartsWith("NOT NonBacktracking because:", StringComparison.Ordinal))
                .Should().BeTrue($"{pattern.Name} is neither NonBacktracking nor excused");
            (nonBacktracking && pattern.WhyNotNonBacktracking.Length > 0)
                .Should().BeFalse($"{pattern.Name} carries a reason it does not need");
        }

        // The two that can, do — measured to build and to give the backtracking engine's exact
        // output on 395 cases. Pinned so nobody drops the flag while keeping an empty reason.
        patterns.Where(p => p.Name is "parameter" or "labelled")
            .Should().OnlyContain(p => p.Regex.Options.HasFlag(RegexOptions.NonBacktracking));
    }

    [Fact]
    public void ThePropertyOrder_IsTheExtensionParsersOrder_WhichIsAFixedPointOfItsSerialiser()
    {
        // Measured in node: notificationLine(parseNotificationLine(line)) reproduces this order
        // byte for byte, and no other order survives that round trip — `pid, seq, bound` come AFTER
        // the optional strings, unlike the interface declaration and unlike `noticeRecord`.
        var line = ServerNoticeLine.Of(EveryFieldFilled("v"));

        var names = Parsed(line).EnumerateObject().Select(p => p.Name).ToList();
        names.Should().Equal(
            "utc", "class", "source", "code", "subject", "title", "detail", "cure", "action", "offered", "answer",
            "run", "repo", "branch", "session", "provider", "role", "pid", "seq", "bound", "whatTheServerSent", "n");
        ServerNoticeLine.Order.Should().Equal(names.Take(20));
    }

    [Fact]
    public void TheLineIsCompactJson_EndingInOneNewline_WithTheExtensionsNames()
    {
        var line = ServerNoticeLine.Of(new() { Utc = "2026-09-16T17:05:39.812Z", Class = "failure", Source = "coai-mcp", Code = "refused" });

        // Exactly node's 90 units for the same record.
        line.Should().Be("{\"utc\":\"2026-09-16T17:05:39.812Z\",\"class\":\"failure\",\"source\":\"coai-mcp\",\"code\":\"refused\"}\n");
    }

    [Fact]
    public void StringsAreQuotedAsJsonStringifyQuotesThem_NotAsAnyDotNetEncoderDoes()
    {
        // Measured on Utf8JsonWriter: the default encoder escapes < > & ' + ` and every non-ASCII
        // character; UnsafeRelaxedJsonEscaping still writes an emoji as 😀 and U+2028,
        // U+0085, U+00A0, U+FEFF, U+E000 and U+FFFE as uppercase escapes. JSON.stringify writes all
        // of them raw and escapes only what is asserted here.
        ServerNoticeLine.Quoted("\"q\" \\ <tag> & 'a' + `t` /s").Should().Be("\"\\\"q\\\" \\\\ <tag> & 'a' + `t` /s\"");
        ServerNoticeLine.Quoted("я ä " + CP(0x1F600)).Should().Be("\"я ä " + CP(0x1F600) + "\"");
        ServerNoticeLine.Quoted(U(0x2028) + U(0x85) + U(0xA0) + U(0xFEFF) + U(0xE000) + U(0xFFFE) + U(0x7F))
            .Should().Be("\"" + U(0x2028) + U(0x85) + U(0xA0) + U(0xFEFF) + U(0xE000) + U(0xFFFE) + U(0x7F) + "\"");
        ServerNoticeLine.Quoted("lone" + U(0xD83D) + "high low" + U(0xDE00) + "end").Should().Be("\"lone\\ud83dhigh low\\ude00end\"");
        ServerNoticeLine.Quoted(U(0) + U(0x1F) + "\n\t\r\b\f").Should().Be("\"\\u0000\\u001f\\n\\t\\r\\b\\f\"");
        Encoding.UTF8.GetBytes(ServerNoticeLine.Quoted(CP(0x1F600))).Should().Equal(0x22, 0xF0, 0x9F, 0x98, 0x80, 0x22);
    }

    [Fact]
    public void AMoreKeyIsQuotedButNeverRedacted_ExactlyAsTheExtensionTreatsAFieldName()
    {
        // notificationLine maps VALUES through safeText and leaves the field name as it is.
        var line = ServerNoticeLine.Of(new()
        {
            Utc = "u", Class = "failure", Source = "s", Code = "c",
            More = new Dictionary<string, object> { ["k\"ey"] = "v\\al", ["e\nl"] = "x", ["ключ"] = "значение" },
        });

        line.Should().Contain("\"k\\\"ey\":\"v\\\\al\",\"e\\nl\":\"x\",\"ключ\":\"значение\"");
    }

    [Fact]
    public void AnEmptyOptionalString_IsAbsent_AsTheExtensionsGivenMakesIt()
    {
        var line = ServerNoticeLine.Of(new() { Utc = "u", Class = "failure", Source = "s", Code = "c", Subject = "", Title = "t" });

        Parsed(line).TryGetProperty("subject", out _).Should().BeFalse();
        Parsed(line).GetProperty("title").GetString().Should().Be("t");
    }

    [Fact]
    public void ZeroIsKept_BecauseAbsentAndZeroAreDifferentFacts()
    {
        var line = ServerNoticeLine.Of(new() { Utc = "u", Class = "failure", Source = "s", Code = "c", Seq = 0 });

        line.Should().Contain("\"seq\":0");
        ServerNoticeLine.Of(new() { Utc = "u", Class = "failure", Source = "s", Code = "c" }).Should().NotContain("seq");
    }

    [Theory]
    [InlineData("Utc")]
    [InlineData("Class")]
    [InlineData("Source")]
    [InlineData("Code")]
    public void ARequiredFieldRefusesToBeEmpty_BecauseTheParserWouldDropTheWholeLine(string field)
    {
        var building = () => new ServerNotice
        {
            Utc = field == "Utc" ? " " : "u",
            Class = field == "Class" ? "" : "failure",
            Source = field == "Source" ? "" : "s",
            Code = field == "Code" ? "" : "c",
        };

        building.Should().Throw<ArgumentException>().WithMessage($"{field} is required*");
    }

    [Fact]
    public void MoreRefusesWhatJavaScriptWouldWriteDifferently()
    {
        // A key that names a field would overwrite it in {...named, ...more}; "7" would be moved to
        // the front by JavaScript's property order; 2^53 + 1 has no exact JavaScript value; NaN
        // would become null; a bool is not flat data.
        foreach (var (more, saying) in new (Dictionary<string, object>, string)[]
                 {
                     (new() { ["title"] = "x" }, "own fields"),
                     (new() { ["7"] = "x" }, "array index"),
                     (new() { [""] = "x" }, "empty key"),
                     (new() { ["n"] = 9007199254740993L }, "past 2^53"),
                     (new() { ["n"] = double.NaN }, "not a finite number"),
                     (new() { ["b"] = true }, "Boolean"),
                 })
        {
            var building = () => new ServerNotice { Utc = "u", Class = "failure", Source = "s", Code = "c", More = more };
            building.Should().Throw<ArgumentException>().WithMessage($"*{saying}*");
        }

        var kept = new ServerNotice
        {
            Utc = "u", Class = "failure", Source = "s", Code = "c",
            More = new Dictionary<string, object> { ["07"] = "not an index", ["n"] = 9007199254740992L, ["r"] = 1.5, ["s"] = "" },
        };
        ServerNoticeLine.Of(kept).Should().Contain("\"07\":\"not an index\",\"n\":9007199254740992,\"r\":1.5,\"s\":\"\"");
    }

    [Fact]
    public void IsoWritesTheInstantAsToISOStringDoes()
    {
        // node: new Date(Date.UTC(2026, 8, 16, 17, 5, 39, 812)).toISOString() — three fractional
        // digits and a Z, where .NET's round-trip format would carry seven.
        ServerNotice.Iso(new DateTimeOffset(2026, 9, 16, 17, 5, 39, 812, TimeSpan.Zero)).Should().Be("2026-09-16T17:05:39.812Z");
        ServerNotice.Iso(new DateTimeOffset(2026, 9, 16, 19, 5, 39, 812, TimeSpan.FromHours(2))).Should().Be("2026-09-16T17:05:39.812Z");
    }
}

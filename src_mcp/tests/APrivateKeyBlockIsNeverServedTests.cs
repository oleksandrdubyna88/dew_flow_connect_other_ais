using System.Text.Json;
using CoaiMcp.Core.Feature;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Normalizer;
using CoaiMcp.Runners.Feature;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A PEM private key inside an ORDINARY file — a service-account JSON, a test fixture, a notice quoting a
/// response body — never reaches a reviewer: the block is taken out header to footer, across real line
/// breaks and JSON-escaped ones, for every private-key variant, on every road a file's text travels.
/// </summary>
/// <remarks>
/// <para><b>The defect</b> (found by epic 3's code round, 2026-09-26). Every redaction pattern is a
/// ONE-LINE shape, and a key body is base64 on lines of its own — so a committed
/// <c>service-account.json</c> was served with only its <c>-----BEGIN</c> taken out by the labelled pass,
/// and the body followed the reviewer into every later turn's "served in earlier turns". The file's NAME
/// is not a credential shape (D15 refuses <c>*.pem</c>, <c>*.key</c>, <c>id_rsa*</c>…), so the content
/// pass is the only thing between the key and the vendor.</para>
/// <para><b>The line count survives.</b> A served file's line numbers are what the outline's spans and the
/// reviewer's next request are counted in, so the block keeps every line break and each line inside it
/// becomes one <c>[redacted]</c>.</para>
/// <para>The headers are assembled at run time rather than written down, for the reason
/// <c>noticeShapes.ts</c> gives for its Basic credentials: a scanner that reads this file as a key would
/// not be wrong about the SHAPE, and the shape is the point.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class APrivateKeyBlockIsNeverServedTests : IAsyncLifetime
{
    /// <summary>What every body line carries, so one assertion covers all of them.</summary>
    private const string BodyMarker = "KEYBODYLINE";

    private const string PlanPath = "todo/PLAN_the_cart.md";

    private const string PlanText = "# PLAN — the cart\n\nThree epics: prices, the cart, the checkout.\n";

    private const string Epics = """
        [{"title": "Prices", "summary": "Prices are doubled at the till."},
         {"title": "The cart", "summary": "A cart holds what the customer picked."},
         {"title": "Checkout", "summary": "Checkout reads the cart and charges it."}]
        """;

    private const string Lessons = """
        {"pitfalls": ["The till doubled prices twice when Buy was called from the cart; fixed in epic 2."],
         "blockers": ["none — every epic merged without a blocker; the shared flag held in all three."],
         "findings": ["The cart and the checkout both round the total; check they round the same way."]}
        """;

    private const string Clean = """{"findings": [], "notes": "The epics add up to the plan.", "sourceRequests": []}""";

    private const string AsksForTheAccount = """
        {"findings": [], "notes": "waiting",
         "sourceRequests": [{"file": "config/service-account.json", "symbol": null, "startLine": 1, "endLine": 8, "why": "which project does it sign for?"}]}
        """;

    private const string AsksForSell = """
        {"findings": [], "notes": "waiting",
         "sourceRequests": [{"file": "src/Shop.cs", "symbol": "Shop.Sell", "startLine": null, "endLine": null, "why": "rounding"}]}
        """;

    private readonly ProcessLauncher _launcher = new();
    private TempGitRepo _repo = null!;
    private string _data = string.Empty;
    private string _record = string.Empty;
    private string _base = string.Empty;
    private string _head = string.Empty;

    private static string FakeCliExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public async ValueTask InitializeAsync()
    {
        _repo = await TempGitRepo.InitAsync(_launcher, "coai-pem-repo-");
        _data = Directory.CreateTempSubdirectory("coai-pem-data-").FullName;
        _record = Directory.CreateTempSubdirectory("coai-pem-record-").FullName;
        await Write(PlanPath, PlanText);
        await Write("src/Shop.cs", Shop("var total = n;"));
        await Write("config/service-account.json", ServiceAccount(Escaped(string.Empty, 2)));
        await Write("tests/KeyFixture.cs", KeyFixture(5, "AAAA"));
        await _repo.CommitAsync("base");
        _base = await _repo.HeadAsync();

        await Write("src/Shop.cs", Shop("var total = n * 2;"));
        await Write("config/service-account.json", ServiceAccount(Escaped(string.Empty, 2, "ROTATED")));
        await Write("tests/KeyFixture.cs", KeyFixture(5, "BBBB"));
        await _repo.CommitAsync("the feature: the key was rotated and a fixture line changed");
        _head = await _repo.HeadAsync();

        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", _record);
        Environment.SetEnvironmentVariable("FAKECLI_TURN_MARKER", TurnTail.HeadingPrefix + "{n} of");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var name in Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(n => n.StartsWith("FAKECLI_", StringComparison.Ordinal)))
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        await _repo.DisposeAsync();
        foreach (var dir in (string[])[_data, _record])
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // ---------- the fixtures, assembled ----------

    /// <summary>The header of a private-key block: <c>-----BEGIN </c>, the kind (<c>RSA </c>, <c>EC </c>, nothing…), <c>PRIVATE KEY</c>, <c>-----</c>.</summary>
    private static string Header(string kind, string suffix = "") => "-----BEGIN " + kind + "PRIVATE" + " KEY" + suffix + "-----";

    private static string Footer(string kind, string suffix = "") => "-----END " + kind + "PRIVATE" + " KEY" + suffix + "-----";

    /// <summary>One 64-column body line, base64 alphabet only, carrying the marker and its number.</summary>
    private static string BodyLine(int n, string filler = "AAAA") =>
        ("MIIEvQ" + BodyMarker + n.ToString("00") + string.Concat(Enumerable.Repeat(filler, 20)))[..64];

    /// <summary>A block with real line breaks, as a <c>.pem</c> file or a fixture holds it.</summary>
    private static string Pem(string kind, int lines = 3, string suffix = "") =>
        Header(kind, suffix) + "\n" + string.Concat(Enumerable.Range(1, lines).Select(n => BodyLine(n) + "\n")) + Footer(kind, suffix) + "\n";

    /// <summary>The same block JSON-escaped on ONE line, as a service account's <c>private_key</c> holds it.</summary>
    private static string Escaped(string kind, int lines = 2, string filler = "AAAA") =>
        Header(kind) + "\\n" + string.Concat(Enumerable.Range(1, lines).Select(n => BodyLine(n, filler) + "\\n")) + Footer(kind) + "\\n";

    /// <summary>A Google-style service-account file: eight lines, the key escaped on line 5.</summary>
    private static string ServiceAccount(string escapedKey) =>
        "{\n"
        + "  \"type\": \"service_account\",\n"
        + "  \"project_id\": \"example-project\",\n"
        + "  \"private_key_id\": \"0123456789abcdef\",\n"
        + $"  \"private_key\": \"{escapedKey}\",\n"
        + "  \"client_email\": \"svc@example-project.iam.example.invalid\",\n"
        + "  \"token_uri\": \"https://oauth2.example.invalid/token\"\n"
        + "}\n";

    /// <summary>A C# fixture holding a nine-line body in a verbatim string — long enough that a change in its middle shows no marker in a three-line hunk.</summary>
    private static string KeyFixture(int changedLine, string fillerOnThatLine) =>
        "public static class KeyFixture\n{\n    public const string Pem = @\"\n"
        + "        " + Header("RSA ") + "\n"
        + string.Concat(Enumerable.Range(1, 9).Select(n => "        " + BodyLine(n, n == changedLine ? fillerOnThatLine : "AAAA") + "\n"))
        + "        " + Footer("RSA ") + "\n"
        + "        \";\n\n    public static int Length() => Pem.Length;\n}\n";

    private static string Shop(string buyLine) =>
        "public sealed class Shop\n{\n"
        + $"    public int Buy(int n)\n    {{\n        {buyLine}\n        return total;\n    }}\n\n"
        + "    public int Sell(int n)\n    {\n        var unchangedBodyMarker = n - 1;\n        return unchangedBodyMarker;\n    }\n}\n";

    private async Task Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_repo.Path, path))!);
        await _repo.WriteAsync(path, text);
    }

    private static int LineCount(string text) => text.Split('\n').Length;

    // ---------- the pass itself, on a served file's road ----------

    [Fact]
    public void AServiceAccountFile_IsServedWithoutItsKeyBody_AndKeepsItsOtherLinesAndItsLineCount()
    {
        var file = ServiceAccount(Escaped(string.Empty));

        var safe = Redaction.SafeSource(file);

        safe.Should().NotContain(BodyMarker, "the key body is the secret, whatever the labelled pass did to the header");
        safe.Should().Contain("\"client_email\": \"svc@example-project.iam.example.invalid\"", "every other line is kept");
        safe.Should().Contain(Footer(string.Empty), "the footer names what was taken out");
        LineCount(safe).Should().Be(LineCount(file), "a served file's line numbers must survive its redaction");
    }

    [Fact]
    public void APemBlockInsideACSharpFixture_LosesEveryBodyLine_AndKeepsTheCodeAroundIt()
    {
        var file = KeyFixture(5, "AAAA");

        var safe = Redaction.SafeSource(file);

        safe.Should().NotContain(BodyMarker);
        safe.Should().Contain("        " + Header("RSA ") + "\n").And.Contain("        " + Footer("RSA ") + "\n", "the markers stay, indentation and all");
        safe.Should().Contain("public static int Length() => Pem.Length;");
        LineCount(safe).Should().Be(LineCount(file));
        safe.Split('\n').Count(line => line == "        " + Redaction.Redacted).Should().Be(9, "one placeholder per body line, each on its own line");
    }

    [Fact]
    public void AnEscapedOneLineJson_LosesTheBody_BetweenItsMarkers()
    {
        var line = "{\"private_key\":\"" + Escaped("EC ") + "\"}";

        var safe = Redaction.SafeSource(line);

        safe.Should().NotContain(BodyMarker);
        safe.Should().Contain(Redaction.Redacted + Footer("EC ") + "\\n\"}", "the escaped block collapses to one placeholder before its footer");
        safe.Should().NotContain("\n", "one line in, one line out");
    }

    [Theory]
    [InlineData("")]
    [InlineData("RSA ")]
    [InlineData("EC ")]
    [InlineData("DSA ")]
    [InlineData("OPENSSH ")]
    [InlineData("ENCRYPTED ")]
    public void EveryPrivateKeyVariant_IsTakenOutHeaderToFooter(string kind)
    {
        var safe = Redaction.SafeSource("before\n" + Pem(kind) + "after\n");

        safe.Should().NotContain(BodyMarker, $"'{Header(kind)}' opens a private key");
        safe.Should().Be("before\n" + Header(kind) + "\n" + Redaction.Redacted + "\n" + Redaction.Redacted + "\n" + Redaction.Redacted + "\n" + Footer(kind) + "\nafter\n");
    }

    [Fact]
    public void APgpPrivateKeyBlock_IsAPrivateKeyToo() =>
        Redaction.SafeSource(Pem("PGP ", suffix: " BLOCK")).Should().NotContain(BodyMarker);

    [Fact]
    public void AnEncryptedPemsOwnHeaderLines_AreInsideTheBlock()
    {
        var pem = Header("RSA ") + "\nProc-Type: 4,ENCRYPTED\nDEK-Info: AES-128-CBC,0123456789ABCDEF0123456789ABCDEF\n\n" + BodyLine(1) + "\n" + Footer("RSA ") + "\n";

        var safe = Redaction.SafeSource(pem);

        safe.Should().NotContain(BodyMarker).And.NotContain("DEK-Info", "the cipher line is part of the key material");
        safe.Should().Be(Header("RSA ") + "\n" + Redaction.Redacted + "\n" + Redaction.Redacted + "\n\n" + Redaction.Redacted + "\n" + Footer("RSA ") + "\n",
            "the blank line stays blank: every line break inside the block is kept");
    }

    [Fact]
    public void ATruncatedBlock_WithNoFooter_StillLosesTheBodyItHas()
    {
        var cut = "the response was: {\"private_key\": \"" + Header(string.Empty) + "\\n" + BodyLine(1) + "\\n" + BodyLine(2)[..20];

        Redaction.SafeSource(cut).Should().NotContain(BodyMarker, "a key cut short is still a key fragment");
        Redaction.SafeText(cut, Redaction.DetailLimit).Should().NotContain(BodyMarker, "and a notice quoting it is not a way past");
    }

    [Fact]
    public void AHeaderLiteralInCode_WithNoBodyAfterIt_LeavesTheCodeAlone()
    {
        var code = "const string Header = \"" + Header(string.Empty) + "\";\nreturn Header.Length;\n";

        Redaction.SafeSource(code).Should().Be(code, "a parser's constant is not a key");
    }

    [Fact]
    public void AHeaderAndAFooterLiteral_RedactOnlyWhatStandsBetweenThem_AndKeepTheLineCount()
    {
        var code = "const string Header = \"" + Header(string.Empty) + "\";\nconst string Footer = \"" + Footer(string.Empty) + "\";\nreturn 1;\n";

        var safe = Redaction.SafeSource(code);

        safe.Should().EndWith("\";\nreturn 1;\n", "what follows the footer is kept");
        LineCount(safe).Should().Be(LineCount(code));
    }

    /// <summary>
    /// The pass is linear by construction: a body-between-markers PATTERN searches <c>headers × bound</c>
    /// against text made of footerless headers, and the extension side has no match ceiling to fall back
    /// on. Ten thousand headers with no footer, and a footerless header before a body-shaped run longer than
    /// the bound, are each answered well inside the redaction's whole-call ceiling.
    /// </summary>
    [Fact]
    public void TenThousandFooterlessHeaders_AreAnsweredInLinearTime()
    {
        // A `;` between the headers, because a header is made of `-`, capitals and spaces — all key-body
        // characters — so headers separated by a space ARE a footerless header's body, and are taken.
        var headers = string.Concat(Enumerable.Repeat(Header(string.Empty) + ";", 10_000));
        var run = Header(string.Empty) + "\n" + new string('A', PrivateKeyBlocks.BodyLimit * 3) + "\n" + BodyMarker;
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var safeHeaders = PrivateKeyBlocks.Redact(headers);
        var safeRun = PrivateKeyBlocks.Redact(run);

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2), "every character is examined a bounded number of times whatever the text is shaped like");
        safeHeaders.Should().Be(headers, "a header followed by a semicolon has no body to take");
        safeRun.Should().StartWith(Header(string.Empty) + "\n" + Redaction.Redacted + "A", "up to the bound the run was a key body");
        safeRun.Should().EndWith(BodyMarker, "past the bound the run is the text it was");
        // The bound counts from the header's end, and the newline after it is the run's first character —
        // kept as layout — so `BodyLimit - 1` characters of body became the one placeholder.
        (run.Length - safeRun.Length).Should().Be(PrivateKeyBlocks.BodyLimit - 1 - Redaction.Redacted.Length);
    }

    [Fact]
    public void ANoticeQuotingAKey_LosesTheBody_OnOneLine()
    {
        var detail = "the vendor answered 400: " + Pem("RSA ") + " — check the key";

        var safe = Redaction.SafeText(detail, Redaction.DetailLimit);

        safe.Should().NotContain(BodyMarker, "a notice drops every line break, so the block arrives as ONE line — and is still a block");
        safe.Should().Contain("check the key");
    }

    // ---------- the pack: hunks inside a body that show no marker ----------

    /// <summary>
    /// A three-line hunk in the middle of a nine-line body carries no header and no footer — nothing a
    /// block pass over the hunk alone can see. The file's own redaction knows where the block is, and a
    /// hunk line inside it is withheld the same way.
    /// </summary>
    [Fact]
    public async Task AHunkInsideAKeyBody_WithNoMarkerInView_IsWithheldFromThePack()
    {
        var outline = await new FeatureOutlineBuilder(_launcher, new TreeSitterOutliner()).BuildAsync(_repo.Path, _base, _head);

        var pack = outline.Section + "\n" + OmissionsRenderer.Render(outline.Omissions, [], FeatureBudget.OmissionsReserveBytes);

        pack.Should().Contain("#### tests/KeyFixture.cs", "the changed member's hunk is shown");
        pack.Should().NotContain(BodyMarker, "not one body line of the key reaches the pack, changed or context");
    }

    // ---------- the served text and the tail, end to end ----------

    /// <summary>The fake CLI's answer for one turn, on the codex path (the <c>-o</c> file).</summary>
    private static void Turn(int n, string answer)
    {
        Environment.SetEnvironmentVariable($"FAKECLI_TURN{n}_OUTFILE_TEXT", answer);
        Environment.SetEnvironmentVariable($"FAKECLI_TURN{n}_STDOUT", answer);
    }

    private PanelService Service() =>
        new(
            new PanelSettings
            {
                Providers = [new("codex") { ExecutablePath = FakeCliExe, Feature = true }],
                Rounds = new PanelConfig(PanelConfig.AllRoles.ToDictionary(r => r, _ => new RoleGate(2, 5)), StagePolicy.Human),
                DataDir = _data,
                ReviewerTimeout = TimeSpan.FromSeconds(30),
                RateLimitBackoff = TimeSpan.FromMilliseconds(5),
                FeatureSourceFollowUps = 3,
            },
            VaultKeys.None("no vault in tests"),
            default,
            _launcher,
            Logger.None, Noticing.None);

    /// <summary>What the reviewer was handed on each launch: the prompt, the last field of the recorded argv.</summary>
    private IReadOnlyList<string> Prompts() =>
        [.. Directory.GetFiles(_record, "*.argv").OrderBy(File.GetCreationTimeUtc).Select(f => File.ReadAllText(f).Split('\0')[^1])];

    [Fact]
    public async Task AServiceAccountAskedForInTurnTwo_IsServedWithoutItsKey_AndTheTailNeverRepeatsIt()
    {
        Turn(1, AsksForTheAccount);
        Turn(2, AsksForSell);
        Turn(3, Clean);

        var answer = JsonDocument.Parse(await Service().ReviewFeatureAsync(_repo.Path, PlanPath, _base, Epics, Lessons)).RootElement;

        answer.TryGetProperty("error", out var error).Should().BeFalse($"the round runs: {error}");
        var prompts = Prompts();
        prompts.Should().HaveCount(3, "two requests, two follow-up turns");
        prompts[1].Should().Contain("### config/service-account.json lines 1-8", "the file is served — it is not a credential SHAPE by name");
        prompts[1].Should().Contain("client_email", "and what is not the key is read");
        prompts[2].Should().Contain("### Served in earlier turns", "turn 3's tail repeats what turn 2 was served");
        foreach (var (prompt, turn) in prompts.Select((p, i) => (p, i + 1)))
        {
            prompt.Should().NotContain(BodyMarker, $"turn {turn}'s prompt — the pack, the served file, or the repeated tail — must not carry the key body");
        }
    }
}

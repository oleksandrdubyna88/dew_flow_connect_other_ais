using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CoaiMcp.Server.Keys;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s7 t2 — the one-shot mode <c>--keys</c>: list, add (ONE line on stdin, no echo), import, relabel, disable, enable, remove (KEY_POOL_SPEC.md sections 4 and 8). Scenarios K1 and K2 of the spec; the numbers
/// in the names are the task's <c>dod</c> ids. The sources (environment, file, creds) and <c>scan</c> are in <see cref="AKeySourcesTests"/>.
/// </summary>
[System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
public sealed class AKeysModeTests
{
    // ---------- d1: chosen by args[0], never 64, in the list ----------

    [Fact]
    public void D1_TheModeIsChosenByTheFirstArgument_AndIsAWriterTheDispatchRefusesOnAMissingNamedRoot()
    {
        Program.Classify(["--keys", "list"]).Should().Be(Program.Startup.Keys);
        Program.WriterModes.Should().Contain(Program.Startup.Keys);
    }

    [Fact]
    public void D1_TheModeIsNamedInTheOneShotListOfTheProjectFile()
    {
        var project = File.ReadAllText(Path.Combine(RepoPaths.Root(), ".agents", "PROJECT.md"));
        var list = project[project.IndexOf("What is sanctioned", StringComparison.Ordinal)..];
        list = list[..list.IndexOf("and their stdout is their entire interface", StringComparison.Ordinal)];

        list.Should().Contain("`--keys`", "a one-shot mode is added to that list, in THAT file");
        list.Should().Contain("`--role-delta`", "the scan must see the list it reads");
    }

    [Theory]
    [InlineData("")]
    [InlineData("bogus")]
    [InlineData("add --label")]
    [InlineData("list extra")]
    [InlineData("list --json --json")]
    [InlineData("remove --label x")]
    public void D1_WrongArguments_Answer65_WithASentence_NeverSixtyFour(string line)
    {
        using var world = new KeyWorld();
        var args = line.Length == 0 ? [] : line.Split(' ');

        var (code, output, said) = new KeysRun(world).Do(string.Empty, args);

        code.Should().Be(65, "this binary KNOWS the mode: 64 means never heard of it");
        output.Should().BeEmpty();
        said.Should().NotBeEmpty();
    }

    [Fact]
    public void D1_TheRealBinary_AnswersTheModeWith65NotSixtyFour_AndMakesNoFolderForAList()
    {
        using var data = TempDir.For("coai-keysmode-");

        OneShotProcess.Run(data.Path, "--keys").Code.Should().Be(65);
        OneShotProcess.Run(data.Path, "--keys", "bogus").Code.Should().Be(65);
        var listed = OneShotProcess.Run(data.Path, "--keys", "list", "--json");

        listed.Code.Should().Be(0);
        using var answer = JsonDocument.Parse(listed.Out);
        answer.RootElement.GetProperty("rows").GetArrayLength().Should().Be(0);
        Directory.Exists(Path.Combine(data.Path, "keys")).Should().BeFalse("a read makes no folder");
    }

    // ---------- d2: the text is ONE line on stdin ----------

    [Fact]
    public void D2_AnAdd_ReadsOneLineOnStdin_AndTrimsTheLineEnd()
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world);

        var (code, output, said) = run.Do(KeyWorld.Token("a") + "\r\nignored second line\r\n", "add", "--label", "laptop", "--json");

        code.Should().Be(0, said);
        using var answer = JsonDocument.Parse(output);
        answer.RootElement.GetProperty("id").GetString().Should().Be(KeyWorld.IdOf(KeyWorld.Token("a")));
        answer.RootElement.GetProperty("label").GetString().Should().Be("laptop");
        answer.RootElement.GetProperty("state").GetString().Should().Be("enabled");
        output.Should().NotContain(KeyWorld.Sentinel);
        said.Should().NotContain(KeyWorld.Sentinel);
        File.ReadAllText(world.KeyFile("a")).Trim().Should().Be(KeyWorld.Token("a"));
    }

    [Fact]
    public void D2_ALineOf4096BytesIsRead_AndOneOf4097IsRefused_WithoutEchoingIt()
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world);
        var valid = KeyWorld.Token("a") + new string('y', 4096 - KeyWorld.Token("a").Length);
        var tooLong = valid + "y";

        var refused = run.Do(tooLong, "add", "--label", "one");
        var accepted = run.Do(valid, "add", "--label", "one");

        refused.Code.Should().Be(65);
        refused.Err.Should().Contain("4096");
        (refused.Out + refused.Err).Should().NotContain(KeyWorld.Sentinel);
        accepted.Code.Should().Be(65, "4096 bytes is READ; the shape check then refuses a text longer than 512 characters");
        accepted.Err.Should().Contain("512").And.NotContain(KeyWorld.Sentinel);
        run.List().GetProperty("rows").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void D2_NothingOnStdin_IsRefused()
    {
        using var world = new KeyWorld();

        var (code, _, said) = new KeysRun(world).Do("\n", "add", "--label", "one");

        code.Should().Be(65);
        said.Should().Contain("ONE line");
    }

    [Fact]
    public void D2_OnATerminal_ItPromptsOnTheErrorStream_AndReadsWithNoEcho()
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world) { Terminal = true, Typed = KeyWorld.Token("a") };

        var (code, output, said) = run.Do(KeyWorld.Token("zzz-not-this"), "add", "--label", "typed");

        code.Should().Be(0, said);
        run.Prompts.Should().Contain("not shown");
        run.Prompts.Should().NotContain(KeyWorld.Sentinel);
        (output + said).Should().NotContain(KeyWorld.Sentinel);
        File.Exists(world.KeyFile("a")).Should().BeTrue("the typed text was added; the bytes on stdin were not read");
    }

    [Theory]
    [InlineData("add", "--label", "one", "--token", "{T}")]
    [InlineData("add", "--label", "one", "--key", "{T}")]
    [InlineData("add", "--label", "one", "--token-file", "/tmp/anything")]
    [InlineData("add", "--label", "one", "--file", "/tmp/anything")]
    [InlineData("add", "--label", "one", "--from-env", "SOME_VARIABLE")]
    [InlineData("add", "--label", "one", "--secret", "{T}")]
    [InlineData("add", "--label", "one", "{T}")]
    [InlineData("add", "{T}")]
    [InlineData("import", "--token", "{T}")]
    public void D2_NoVerbTakesTheTextAsAnArgument_FromAFile_OrFromTheEnvironment(params string[] arguments)
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world);
        run.Variables["SOME_VARIABLE"] = KeyWorld.Token("a");
        var args = arguments.Select(argument => argument.Replace("{T}", KeyWorld.Token("a"), StringComparison.Ordinal)).ToArray();

        var (code, output, said) = run.Do(string.Empty, args);

        code.Should().Be(65);
        (output + said).Should().NotContain(KeyWorld.Sentinel, "an answer never echoes an argument it does not understand");
        run.List().GetProperty("rows").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void D2_ATextGivenAsTheLabel_IsRefused_AndNotEchoed()
    {
        using var world = new KeyWorld();

        var (code, output, said) = new KeysRun(world).Do(KeyWorld.Token("a"), "add", "--label", KeyWorld.Token("a"));

        code.Should().Be(65);
        (output + said).Should().NotContain(KeyWorld.Sentinel);
        new KeysRun(world).List().GetProperty("rows").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void D2_TheEnvironmentIsNotASourceOfAnAdd()
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world);
        run.Variables["COAI_CLAUDE_KEY_alpha"] = KeyWorld.Token("a");
        run.Variables["CLAUDE_CODE_OAUTH_TOKEN"] = KeyWorld.Token("b");
        run.Variables["ANTHROPIC_API_KEY"] = KeyWorld.Token("c");

        var (code, _, _) = run.Do(string.Empty, "add", "--label", "one");

        code.Should().Be(65, "an add with nothing on stdin has nothing to add, whatever the environment holds");
        run.List().GetProperty("rows").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void D2_TheRealBinary_TakesTheTextOnStdin_AndPrintsNoPartOfIt()
    {
        using var data = TempDir.For("coai-keysmode-");

        var added = RealBinary(data.Path, KeyWorld.Token("a") + "\n", "--keys", "add", "--label", "real", "--json");
        var tooLong = RealBinary(data.Path, new string('s', 5000), "--keys", "add", "--label", "long");

        added.Code.Should().Be(0, added.Err);
        (added.Out + added.Err).Should().NotContain(KeyWorld.Sentinel);
        tooLong.Code.Should().Be(65);
        (tooLong.Out + tooLong.Err).Should().NotContain(new string('s', 100));
        File.ReadAllText(Path.Combine(data.Path, "keys", "claude", KeyWorld.IdOf(KeyWorld.Token("a")) + ".key")).Trim().Should().Be(KeyWorld.Token("a"));
        File.GetUnixFileMode(Path.Combine(data.Path, "keys", "claude", KeyWorld.IdOf(KeyWorld.Token("a")) + ".key")).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    // ---------- d3: the shape check ----------

    [Theory]
    [InlineData("sk-ant-api03-{F}", "API key")]
    [InlineData("sk-other-{F}", "does not have the shape")]
    [InlineData("hello", "does not have the shape")]
    public void D3_ATextThatIsNotASetupToken_IsRefused_WithTheRuleAndTheLength_NeverACharacter(string template, string rule)
    {
        using var world = new KeyWorld();
        var text = template.Replace("{F}", KeyWorld.Sentinel + new string('x', 40), StringComparison.Ordinal);

        var (code, output, said) = new KeysRun(world).Do(text, "add", "--label", "one");

        code.Should().Be(65);
        said.Should().Contain(rule);
        (output + said).Should().NotContain(KeyWorld.Sentinel);
        (output + said).Should().NotContain("xxxxxxxx");
    }

    [Fact]
    public void D3_TheLimitsOfTheShape_Are40And512Characters_AndTheCharacterSet()
    {
        using var world = new KeyWorld();
        var prefix = string.Concat("sk-ant-", "oat01-");

        KeyShape.Refusal(prefix + new string('a', 40 - prefix.Length - 1)).Should().Contain("40 to 512").And.Contain("length 39");
        KeyShape.Refusal(prefix + new string('a', 40 - prefix.Length)).Should().BeEmpty("40 characters is the least");
        KeyShape.Refusal(prefix + new string('a', 512 - prefix.Length)).Should().BeEmpty("512 characters is the most");
        KeyShape.Refusal(prefix + new string('a', 513 - prefix.Length)).Should().Contain("length 513");
        KeyShape.Refusal(prefix + new string('a', 50) + " " + new string('b', 20)).Should().Contain("outside A-Z a-z 0-9 _ -").And.NotContain("b");
    }

    [Fact]
    public void D3_AnAddMakesNoVendorCall_NothingInTheKeysFolderStartsAProcessOrOpensTheNetwork()
    {
        var folder = Path.Combine(RepoPaths.Root(), "src_mcp", "src", "Server", "Keys");
        var sources = Directory.GetFiles(folder, "*.cs").ToDictionary(path => Path.GetFileName(path), File.ReadAllText);

        var callers = sources.Where(file => System.Text.RegularExpressions.Regex.IsMatch(file.Value, @"HttpClient|Process\.Start|ProcessLauncher|new Socket|TcpClient|WebRequest")).Select(file => file.Key);

        callers.Should().BeEmpty("a wrong key is found by its first launch, not by a call at add");
        System.Text.RegularExpressions.Regex.IsMatch("var c = new HttpClient();", @"HttpClient|Process\.Start").Should().BeTrue("the scan must be able to see what it looks for");
        sources.Should().NotBeEmpty();
    }

    // ---------- d4: import ----------

    private static string Line(string label, string tag) => $$"""{"label":"{{label}}","token":"{{KeyWorld.Token(tag)}}"}""";

    [Fact]
    public void D4_Import_ReadsOneJsonObjectPerLine_PrintsCountsOnly_AndASecondRunAddsNothing()
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world);
        var input = string.Join('\n', Line("one", "a"), Line("two", "b"), Line("three", "c")) + "\n";

        var first = run.Do(input, "import", "--json");
        var second = run.Do(input, "import", "--json");

        first.Code.Should().Be(0, first.Err);
        using var counts = JsonDocument.Parse(first.Out);
        counts.RootElement.GetProperty("added").GetInt32().Should().Be(3);
        counts.RootElement.GetProperty("alreadyThere").GetInt32().Should().Be(0);
        using var again = JsonDocument.Parse(second.Out);
        again.RootElement.GetProperty("added").GetInt32().Should().Be(0, "a second boot adds nothing: the same ids");
        again.RootElement.GetProperty("alreadyThere").GetInt32().Should().Be(3);
        (first.Out + first.Err + second.Out + second.Err).Should().NotContain(KeyWorld.Sentinel);
        run.List().GetProperty("rows").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public void D4_Import_RefusesALineByItsNumberAndTheRule_NeverByItsContent()
    {
        using var world = new KeyWorld();
        var input = string.Join('\n', Line("one", "a"), "not json at all " + KeyWorld.Token("x"), $$"""{"label":"extra","token":"{{KeyWorld.Token("y")}}","other":"1"}""", $$"""{"label":"short","token":"sk-ant-oat01-tiny"}""", Line("two", "b"));

        var (code, output, said) = new KeysRun(world).Do(input, "import", "--json");

        code.Should().Be(0, said);
        using var counts = JsonDocument.Parse(output);
        counts.RootElement.GetProperty("added").GetInt32().Should().Be(2);
        var refused = counts.RootElement.GetProperty("refused").EnumerateArray().Select(line => line.GetProperty("line").GetInt32()).ToList();
        refused.Should().BeEquivalentTo([2, 3, 4]);
        (output + said).Should().NotContain(KeyWorld.Sentinel);
    }

    [Fact]
    public void D4_Import_TakesAtMost200Lines_AndAddsNothingOfAnImportOfMore()
    {
        using var world = new KeyWorld();
        var input = string.Join('\n', Enumerable.Range(0, 201).Select(number => Line($"key {number}", $"n{number}")));

        var (code, _, said) = new KeysRun(world).Do(input, "import");

        code.Should().Be(65);
        said.Should().Contain("200");
        new KeysRun(world).List().GetProperty("rows").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void D4_Import_OfOnlyRefusedLines_Exits65_AndStillPrintsTheCounts()
    {
        using var world = new KeyWorld();

        var (code, output, _) = new KeysRun(world).Do("nonsense\n", "import", "--json");

        code.Should().Be(65);
        output.Should().Contain("\"refused\"");
    }

    // ---------- d5: list, relabel, disable, enable, remove, the revision ----------

    [Fact]
    public void D5_ListAnswersTheMetadataRows_TheUsableCount_TheRevision_AndTheSwitch_NeverAText()
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world);
        run.Do(KeyWorld.Token("a"), "add", "--label", "one");
        run.Do(KeyWorld.Token("b"), "add", "--label", "two");
        var disabled = run.Do(string.Empty, "disable", "--id", KeyWorld.IdOf(KeyWorld.Token("b")), "--json");
        run.Settings["COAI_ROLE_CLAUDE_PERSON_SIGN_IN"] = "1";

        var listed = run.Do(string.Empty, "list", "--json");

        disabled.Code.Should().Be(0, disabled.Err);
        listed.Out.Should().NotContain(KeyWorld.Sentinel);
        using var answer = JsonDocument.Parse(listed.Out);
        answer.RootElement.GetProperty("usable").GetInt32().Should().Be(1);
        answer.RootElement.GetProperty("personSignInSwitch").GetBoolean().Should().BeTrue();
        answer.RootElement.GetProperty("adminRevision").GetInt32().Should().Be(3, "two adds and a disable");
        var rows = answer.RootElement.GetProperty("rows").EnumerateArray().ToList();
        rows.Should().HaveCount(2);
        rows[0].GetProperty("id").GetString().Should().Be(KeyWorld.IdOf(KeyWorld.Token("a")));
        rows[1].GetProperty("state").GetString().Should().Be("disabled");
        rows.SelectMany(row => row.EnumerateObject().Select(property => property.Name)).Should().NotContain(["token", "secret", "text"]);
        run.Do(string.Empty, "list").Out.Should().Contain("2 key(s), 1 usable").And.NotContain(KeyWorld.Sentinel);
    }

    [Fact]
    public void D5_Relabel_Disable_Enable_Remove_AnswerTheRow_OrTheRemovedId()
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world);
        run.Do(KeyWorld.Token("a"), "add", "--label", "one");
        var id = KeyWorld.IdOf(KeyWorld.Token("a"));

        using var relabelled = JsonDocument.Parse(run.Do(string.Empty, "relabel", "--id", id, "--label", "renamed", "--json").Out);
        using var off = JsonDocument.Parse(run.Do(string.Empty, "disable", "--id", id, "--json").Out);
        using var on = JsonDocument.Parse(run.Do(string.Empty, "enable", "--id", id, "--json").Out);
        using var gone = JsonDocument.Parse(run.Do(string.Empty, "remove", "--id", id, "--json").Out);

        relabelled.RootElement.GetProperty("label").GetString().Should().Be("renamed");
        off.RootElement.GetProperty("state").GetString().Should().Be("disabled");
        on.RootElement.GetProperty("state").GetString().Should().Be("enabled");
        gone.RootElement.GetProperty("removed").GetString().Should().Be(id);
        run.Do(string.Empty, "remove", "--id", id).Code.Should().Be(65);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("k-zzzzzzzzzzzz")]
    [InlineData("k-0123456789ab/../x")]
    [InlineData("")]
    public void D5_AnIdThatIsNotTheExactShape_IsRefused_BeforeAnyPathIsBuilt(string id)
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world);
        run.Do(KeyWorld.Token("a"), "add", "--label", "one");

        foreach (var verb in new[] { "disable", "enable", "remove" })
        {
            run.Do(string.Empty, verb, "--id", id).Code.Should().Be(65, verb);
        }

        run.Do(string.Empty, "relabel", "--id", id, "--label", "x").Code.Should().Be(65);
        run.List().GetProperty("rows").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void D5_AWriteWithAStaleRevision_AnswersChangedElsewhere_AndChangesNothing()
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world);
        run.Do(KeyWorld.Token("a"), "add", "--label", "one");
        var id = KeyWorld.IdOf(KeyWorld.Token("a"));
        var revision = run.List().GetProperty("adminRevision").GetInt32();
        run.Do(string.Empty, "relabel", "--id", id, "--label", "somebody else did this");

        var stale = run.Do(string.Empty, "disable", "--id", id, "--expect-revision", revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var fresh = run.Do(string.Empty, "disable", "--id", id, "--expect-revision", (revision + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));

        stale.Code.Should().Be(65);
        stale.Err.Should().Contain("changed elsewhere");
        fresh.Code.Should().Be(0, fresh.Err);
        run.List().GetProperty("rows")[0].GetProperty("label").GetString().Should().Be("somebody else did this");
    }

    // ---------- the real binary, with stdin ----------

    private static (int Code, string Out, string Err) RealBinary(string dataDir, string stdin, params string[] args)
    {
        var info = OneShotProcess.StartInfo(dataDir, args);
        info.RedirectStandardInput = true;
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using (var input = process.StandardInput)
        {
            var bytes = Encoding.UTF8.GetBytes(stdin);
            try
            {
                input.BaseStream.Write(bytes);
            }
            catch (IOException)
            {
                // The child refused a long line and closed its stdin first: that is the answer under test.
            }
        }

        process.WaitForExit(TimeSpan.FromMinutes(1)).Should().BeTrue();
        Task.WaitAll([output, error], 10_000).Should().BeTrue();

        return (process.ExitCode, output.Result, error.Result);
    }
}

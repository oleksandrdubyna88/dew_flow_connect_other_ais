using System.Text.Json;
using CoaiMcp.Roles;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary><c>--roles [--format json|claude-hook]</c> (v2 E3.S7 t1): one object on stdout, 65 for a wrong argument, never the data directory, never 64.</summary>
public sealed class TheRolesModeTests : IDisposable
{
    private readonly TempDir _root = TempDir.For("coai-roles-");

    private (int Code, string Out, string Err) Run(Func<string, string?>? env, params string[] args)
    {
        var output = new StringWriter();
        var errors = new List<string>();
        var code = RolesMode.Run(args, env ?? (name => name == "COAI_DATA_DIR" ? _root.At("data") : null), output, errors.Add);

        return (code, output.ToString(), string.Join('\n', errors));
    }

    [Fact]
    public void Plain_PrintsOneJsonObject()
    {
        var (code, said, err) = Run(null);

        code.Should().Be(0, err);
        said.TrimEnd('\n').Should().NotContain("\n", "one object, one line");
        JsonDocument.Parse(said).RootElement.GetProperty("roles").GetProperty("coder").GetProperty("models")[0].GetProperty("model").GetString().Should().Be("sonnet");
    }

    [Fact]
    public void FormatJson_IsThePlainAnswer()
    {
        Run(null, "--format", "json").Out.Should().Be(Run(null).Out);
    }

    [Fact]
    public void ClaudeHook_PrintsTheSessionStartHookOutput()
    {
        var (code, said, err) = Run(null, "--format", "claude-hook");

        code.Should().Be(0, err);
        var root = JsonDocument.Parse(said).RootElement;
        var hook = root.GetProperty("hookSpecificOutput");
        hook.GetProperty("hookEventName").GetString().Should().Be("SessionStart");
        hook.GetProperty("additionalContext").GetString().Should().Contain("orchestrator: opus").And.Contain("Work method:");
        root.EnumerateObject().Select(one => one.Name).Should().Equal("hookSpecificOutput");
    }

    [Theory]
    [InlineData("--format")]
    [InlineData("--format", "xml")]
    [InlineData("--nope")]
    [InlineData("--format", "json", "extra")]
    public void AWrongArgument_Is65_WithNothingOnStdout(params string[] args)
    {
        var (code, said, err) = Run(null, args);

        code.Should().Be(65, "64 is how a caller recognises a binary too old for the mode");
        said.Should().BeEmpty();
        err.Should().Contain("--roles takes only");
    }

    [Fact]
    public void ASettingsFileInTheDataDirectory_IsReadUnderTheEnvironment()
    {
        var data = _root.At("data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"), """{"COAI_ROLES_HAIKU_KINDS":["from-file"],"COAI_ROLES_WORK_METHOD":{"maxEpicsPerEpoch":3}}""");
        Func<string, string?> env = name => name switch
        {
            "COAI_DATA_DIR" => data,
            "COAI_ROLES_WORK_METHOD" => """{"maxEpicsPerEpoch":4}""",
            _ => null,
        };

        var answer = JsonDocument.Parse(Run(env).Out).RootElement;

        answer.GetProperty("haikuKinds").EnumerateArray().Select(kind => kind.GetString()).Should().Equal("from-file");
        answer.GetProperty("workMethod").GetProperty("maxEpicsPerEpoch").GetInt32().Should().Be(4, "the environment is more specific than the file");
    }

    [Fact]
    public void ASettingThatDoesNotRead_IsSaidOnStderr_AndStdoutStaysOneObject()
    {
        var (code, said, err) = Run(name => name == "COAI_ROLES_BANDS" ? "[" : name == "COAI_DATA_DIR" ? _root.At("data") : null);

        code.Should().Be(0);
        err.Should().Contain("COAI_ROLES_BANDS");
        JsonDocument.Parse(said).RootElement.GetProperty("unrecognised")[0].GetProperty("key").GetString().Should().Be("COAI_ROLES_BANDS");
    }

    [Fact]
    public void TheMode_IsKnown_AndNotAWriter()
    {
        Program.Classify(["--roles"]).Should().Be(Program.Startup.Roles, "an unknown flag exits 64, and a hook's caller takes 64 for an old binary");
        Program.WriterModes.Should().NotContain(Program.Startup.Roles, "it writes nothing, so a missing named data folder must not stop it — it never opens one");
    }

    [Fact]
    public void AMissingDataDirectory_IsNotMade()
    {
        Run(null);
        Run(null, "--format", "claude-hook");

        Directory.Exists(_root.At("data")).Should().BeFalse("a reader never makes the folder it reads");
    }

    public void Dispose() => _root.Dispose();
}

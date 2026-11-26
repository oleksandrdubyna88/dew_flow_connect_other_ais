using CoaiMcp.Core.Toolchain;
using CoaiMcp.Core.Work;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The toolchain file of a repository (epoch 3 E2 s1 t1): <c>.coai/toolchain.yaml</c> read through the strict YAML reader. A right file gives the projects with their commands as lists; a wrong one is
/// refused with a sentence that names the key; no file is <c>not_configured</c> — never an error and never an empty list of projects.
/// </summary>
public sealed class AToolchainFileTests
{
    /// <summary>The shape this repository's own file has (t4): block lists, because the strict subset has no flow list but <c>[]</c>.</summary>
    internal const string RightFile = """
        version: 1
        projects:
          - path: src_mcp
            adapter: dotnet
            build:
              - dotnet
              - build
              - dew_flow_connect_other_ais.slnx
              - -c
              - Debug
              - "-m:{jobs}"
            test:
              - ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests
            memoryMb: 3000
          - path: src_vs_code
            adapter: node
        skip:
          - audit
          - coverage
        timeouts:
          build: 15m
          test: 90s
        """;

    private static ToolchainRead Read(string text) => ToolchainFile.Read(new WorkFile.Text(text.ReplaceLineEndings("\n") + "\n"));

    private static string RefusedBy(string text) => Read(text).Should().BeOfType<ToolchainRead.Refused>($"the file is wrong: {text}").Subject.Sentence;

    private static RepoToolchain Right(string text) => Read(text).Should().BeOfType<ToolchainRead.Configured>().Subject.Toolchain;

    /// <summary>One project with these lines after <c>path</c> and <c>adapter</c>.</summary>
    private static string OneProject(string lines) => $"version: 1\nprojects:\n  - path: src_mcp\n    adapter: dotnet\n{lines}";

    [Fact]
    public void AValidFile_GivesTheProjectsWithTheirBuildAndTestArgvAsLists()
    {
        var toolchain = Right(RightFile);

        toolchain.Projects.Should().HaveCount(2);
        var mcp = toolchain.Projects[0];
        mcp.Path.Should().Be("src_mcp");
        mcp.Adapter.Should().Be("dotnet");
        mcp.Build.Argv.Should().Equal("dotnet", "build", "dew_flow_connect_other_ais.slnx", "-c", "Debug", "-m:{jobs}");
        mcp.Test.Argv.Should().Equal("./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests");
        mcp.MemoryMb.Should().Be(3000);
        toolchain.Projects[1].Should().BeEquivalentTo(new ToolchainProject("src_vs_code", "node", ToolchainCommand.NotStated, ToolchainCommand.NotStated, null));
        toolchain.Skip.Should().Equal("audit", "coverage");
        toolchain.Timeouts.Should().Be(new ToolchainTimeouts(TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(90)));
    }

    [Theory]
    [InlineData("secuirty: true\n", 8)]
    [InlineData("skipp:\n  - audit\n", 5)]
    public void AnUnknownKey_AtTheTop_IsRefused_NamingItsLineAndLength_NotItsText(string extra, int length)
    {
        var sentence = RefusedBy(OneProject(string.Empty) + extra);

        sentence.Should().Contain($"line 5: a key of {length} characters is not a key of toolchain").And.Contain("(the keys are version, projects, skip, timeouts)");
        sentence.Should().NotContain("secuirty").And.NotContain("skipp", "a typed key is never spelled: a short word is the shape of a secret too");
    }

    [Fact]
    public void AnUnknownKey_InAProject_IsRefused_NamingItsLineAndLength_AndTheLegalKeys()
    {
        var sentence = RefusedBy(OneProject("    buidl:\n      - dotnet\n"));

        sentence.Should().Contain("line 5: a key of 5 characters is not a key of project 1").And.Contain("(the keys are path, adapter, build, test, memoryMb)").And.NotContain("buidl");
    }

    [Fact]
    public void AnUnknownKey_InTimeouts_IsRefused_NamingItsLineAndLength_AndTheLegalKeys()
    {
        var sentence = RefusedBy(OneProject(string.Empty) + "timeouts:\n  lint: 5m\n");

        sentence.Should().Contain("line 6: a key of 4 characters is not a key of timeouts (the keys are build, test)").And.NotContain("lint");
    }

    public static TheoryData<string, string> ArgvsThatAreNotListsOfStrings => new()
    {
        { "build", "    build: dotnet build\n" },
        { "build", "    build:\n      - dotnet\n      - 4\n" },
        { "build", "    build:\n      - dotnet\n      - true\n" },
        { "build", "    build:\n      - dotnet\n      - key: value\n" },
        { "build", "    build:\n      - dotnet\n      -\n" },
        { "test", "    test: ./run-tests\n" },
        { "test", "    test:\n      - ./run-tests\n      - 7\n" },
    };

    [Theory]
    [MemberData(nameof(ArgvsThatAreNotListsOfStrings))]
    public void AnArgvThatIsNotAListOfStrings_IsRefused_NamingTheKey(string key, string lines)
    {
        RefusedBy(OneProject(lines)).Should().Contain($"'{key}'");
    }

    [Theory]
    [InlineData("/etc")]
    [InlineData("\\\\server\\share")]
    [InlineData("C:/work")]
    [InlineData("C:\\work")]
    [InlineData("..")]
    [InlineData("src/../../outside")]
    [InlineData("")]
    public void AProjectPath_OutsideTheRepository_IsRefused_NamingTheKey(string path)
    {
        var text = $"version: 1\nprojects:\n  - path: {Quoted(path)}\n    adapter: dotnet\n";

        RefusedBy(text).Should().Contain("'path'");
    }

    [Theory]
    [InlineData(".")]
    [InlineData("src_mcp")]
    [InlineData("src/mcp")]
    public void AProjectPath_InsideTheRepository_IsRead(string path)
    {
        Right($"version: 1\nprojects:\n  - path: {Quoted(path)}\n    adapter: dotnet\n").Projects.Single().Path.Should().Be(path);
    }

    private static string Quoted(string value) => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal)}\"";

    [Theory]
    [InlineData("version: 2\n")]
    [InlineData("version: 0\n")]
    [InlineData("version: \"1\"\n")]
    [InlineData("version: true\n")]
    [InlineData("")]
    public void AVersionOtherThanOne_OrNone_IsRefused_NamingTheKey(string versionLine)
    {
        RefusedBy(versionLine + "projects:\n  - path: src_mcp\n    adapter: dotnet\n").Should().Contain("'version'");
    }

    [Fact]
    public void NoFile_IsNotConfigured_NotAnError_AndNotAnEmptyProjectList()
    {
        Read(string.Empty).Should().BeOfType<ToolchainRead.Refused>("an EMPTY file is a file, and it is wrong");
        ToolchainFile.Read(new WorkFile.Missing()).Should().BeOfType<ToolchainRead.NotConfigured>();
    }

    [Fact]
    public void AFileWithNoProject_IsRefused_NamingTheKey_NotReadAsAnEmptyList()
    {
        RefusedBy("version: 1\nprojects: []\n").Should().Contain("'projects'");
    }

    [Fact]
    public void AFileThatCannotBeRead_IsRefused_NotNotConfigured()
    {
        ToolchainFile.Read(new WorkFile.TooLarge(70_000)).Should().BeOfType<ToolchainRead.Refused>().Which.Sentence.Should().Contain("70000");
        ToolchainFile.Read(new WorkFile.NotUtf8()).Should().BeOfType<ToolchainRead.Refused>().Which.Sentence.Should().Contain("UTF-8");
    }

    [Fact]
    public void AStatedEmptyList_IsStatedAndRunsNothing_AnAbsentCommandIsNotStated()
    {
        var project = Right(OneProject("    test: []\n")).Projects.Single();

        project.Test.Should().BeEquivalentTo(new ToolchainCommand(true, []));
        project.Test.Runs.Should().BeFalse();
        project.Build.Should().BeEquivalentTo(ToolchainCommand.NotStated);
    }

    [Fact]
    public void AFlowListThatIsNotEmpty_IsRefusedByTheReader_ItIsNotGuessedAt()
    {
        RefusedBy(OneProject("    build: [dotnet, build]\n")).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("--token=ghp_0123456789abcdef0123456789abcdef0123")]
    [InlineData("password=hunter2hunter2")]
    public void ASecretInAnArgv_IsRefused_NamingTheKey(string argument)
    {
        var sentence = RefusedBy(OneProject($"    build:\n      - tool\n      - \"{argument}\"\n"));

        sentence.Should().Contain("'build'").And.NotContain(argument, "the refusal must not repeat the secret it refuses");
    }

    [Fact]
    public void AnAdapterOutsideTheTable_IsRefused_NamingTheLegalOnes()
    {
        var sentence = RefusedBy("version: 1\nprojects:\n  - path: a\n    adapter: cobol\n");

        sentence.Should().Contain("'adapter'");
        foreach (var adapter in ToolchainTables.Adapters)
        {
            sentence.Should().Contain(adapter);
        }
    }

    [Theory]
    [InlineData("build")]
    [InlineData("test")]
    [InlineData("nonsense")]
    public void ASkipOfAStepThatCannotBeSkipped_IsRefused_NamingTheLegalOnes(string step)
    {
        var sentence = RefusedBy(OneProject(string.Empty) + $"skip:\n  - {step}\n");

        sentence.Should().Contain("'skip'").And.NotContain($"'{step}'", "a value the person typed is not echoed");
        foreach (var legal in ToolchainTables.SkippableSteps)
        {
            sentence.Should().Contain(legal);
        }
    }

    [Theory]
    [InlineData("15", "'build'")]
    [InlineData("0m", "'build'")]
    [InlineData("25h", "'build'")]
    [InlineData("1d", "'build'")]
    [InlineData("-5m", "'build'")]
    public void ATimeoutThatIsNotAWholeNumberAndAUnit_OrIsPastADay_IsRefused_NamingTheKey(string value, string key)
    {
        RefusedBy(OneProject(string.Empty) + $"timeouts:\n  build: {value}\n").Should().Contain(key);
    }

    [Fact]
    public void NoTimeouts_GiveTheDefaults_AndOneNamedKeyKeepsTheOther()
    {
        Right(OneProject(string.Empty)).Timeouts.Should().Be(ToolchainTimeouts.Default);
        Right(OneProject(string.Empty) + "timeouts:\n  test: 2h\n").Timeouts.Should().Be(new ToolchainTimeouts(ToolchainTimeouts.Default.Build, TimeSpan.FromHours(2)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("2000000")]
    [InlineData("big")]
    public void AMemoryThatIsNotAPositiveReasonableNumber_IsRefused_NamingTheKey(string value)
    {
        RefusedBy(OneProject($"    memoryMb: {value}\n")).Should().Contain("'memoryMb'");
    }

    [Fact]
    public void TwoProjectsWithOnePath_AreRefused_NamingTheKey()
    {
        RefusedBy(OneProject(string.Empty) + "  - path: src_mcp\n    adapter: node\n").Should().Contain("'path'").And.NotContain("src_mcp", "a path the person typed is not echoed");
    }

    /// <summary>A token-shaped string: what a person may paste into the wrong place. The refusal is printed and recorded, so it must never carry it.</summary>
    private const string Pasted = "ghp_0123456789abcdefghijklmnopqrstuvwxyzAB";
    private const string Part = "ghp_012345";

    [Theory]
    [InlineData("version: {0}\nprojects:\n  - path: src_mcp\n    adapter: dotnet\n")]
    [InlineData("version: 1\nprojects: {0}\n")]
    [InlineData("version: 1\nprojects:\n  - {0}\n")]
    [InlineData("version: 1\nprojects:\n  - path: {0}\n    adapter: dotnet\n    build: {0}\n")]
    [InlineData("version: 1\nprojects:\n  - path: src_mcp\n    adapter: {0}\n    test: {0}\n")]
    [InlineData("version: 1\nprojects:\n  - path: src_mcp\n    adapter: dotnet\n    memoryMb: {0}\n")]
    [InlineData("version: 1\nprojects:\n  - path: src_mcp\n    adapter: dotnet\nskip:\n  - {0}\n")]
    [InlineData("version: 1\nprojects:\n  - path: src_mcp\n    adapter: dotnet\nskip: {0}\n")]
    [InlineData("version: 1\nprojects:\n  - path: src_mcp\n    adapter: dotnet\ntimeouts: {0}\n")]
    [InlineData("version: 1\nprojects:\n  - path: src_mcp\n    adapter: dotnet\ntimeouts:\n  build: {0}\n")]
    [InlineData("version: 1\nprojects:\n  - path: {0}\n    adapter: dotnet\n  - path: {0}\n    adapter: dotnet\n")]
    public void ATokenShapedValue_InTheWrongPlace_IsRefused_AndNeverEchoed(string template)
    {
        var read = Read(template.Replace("{0}", Pasted, StringComparison.Ordinal));

        var refused = read.Should().BeOfType<ToolchainRead.Refused>(template).Subject;
        refused.Sentence.Should().NotContain(Pasted, "a refusal is printed and recorded");
        refused.Faults.Should().NotContain(fault => fault.Contains(Pasted, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{0}: x\n")]
    [InlineData("timeouts:\n  {0}: 5m\n")]
    public void AnUnknownKey_IsNeverEchoed_WhateverItsShape_OnlyItsLineAndLengthAreSaid(string template)
    {
        string Put(string key) => OneProject(template.Replace("{0}", key, StringComparison.Ordinal));

        // A token pasted as a key, and a short word that has the shape of a real key AND of a secret (a password typed where a key goes).
        foreach (var typed in new[] { Pasted, "MySecretToken123", "buidl" })
        {
            var refused = Read(Put(typed)).Should().BeOfType<ToolchainRead.Refused>(typed.Length.ToString()).Subject;

            refused.Sentence.Should().NotContain(typed, "a key is typed by a person too, and a refusal is printed and recorded");
            refused.Faults.Should().NotContain(fault => fault.Contains(typed, StringComparison.Ordinal));
            refused.Sentence.Should().Contain($"a key of {typed.Length} characters is not a key of");
        }
    }

    [Fact]
    public void ATokenShapedKey_InAProject_IsNeverEchoed()
    {
        var refused = Read($"version: 1\nprojects:\n  - path: src_mcp\n    adapter: dotnet\n    {Pasted}: x\n").Should().BeOfType<ToolchainRead.Refused>().Subject;

        refused.Sentence.Should().NotContain(Pasted);
    }

    [Theory]
    [InlineData("2147483647h")]
    [InlineData("2147483647m")]
    [InlineData("9223372036854775807s")]
    [InlineData("99999999999999999999h")]
    public void ATimeoutThatPassesTheIntegerLimitButNotTheMaximum_IsRefused_NotThrown(string value)
    {
        var read = () => Read(OneProject(string.Empty) + $"timeouts:\n  build: {value}\n");

        read.Should().NotThrow().Which.Should().BeOfType<ToolchainRead.Refused>().Which.Sentence.Should().Contain("'build'");
    }

    [Fact]
    public void EveryFault_IsReported_NotOnlyTheFirst()
    {
        var refused = Read("version: 3\nprojects:\n  - path: /abs\n    adapter: cobol\n    buidl: x\n").Should().BeOfType<ToolchainRead.Refused>().Subject;

        refused.Faults.Should().HaveCountGreaterThanOrEqualTo(4);
        refused.Sentence.Should().Contain("'version'").And.Contain("'path'").And.Contain("'adapter'").And.Contain("line 5: a key of 5 characters is not a key of project 1");
    }

    [Fact]
    public void ARightFile_IsNotRefusedForItsCommentsOrItsLeadingDocumentMarker()
    {
        Right("---\n# the checks of this repository\n" + OneProject("    # the build\n    build:\n      - dotnet\n")).Projects.Single().Build.Argv.Should().Equal("dotnet");
    }
}

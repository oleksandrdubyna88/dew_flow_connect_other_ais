using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E4 s2 t1: <c>shared/toolchains.json</c> — the defaults of the detected toolchains — is embedded, read strictly, equal to the reader's tables, and held to the strict rules of SPEC §A3 by a scan that finds each planted bad line.
/// </summary>
public sealed class AToolchainDefaultsTests
{
    private static string SharedFile => Path.Combine(RepoPaths.Root(), "shared", "toolchains.json");

    private static string Shipped => File.ReadAllText(SharedFile);

    private static JsonObject Edited(Action<JsonObject> edit)
    {
        var root = JsonNode.Parse(Shipped)!.AsObject();
        edit(root);

        return root;
    }

    private static string Text(JsonObject root) => root.ToJsonString();

    private static JsonObject Stack(JsonObject root, string id) => root["stacks"]![id]!.AsObject();

    private static JsonObject Step(JsonObject root, string stack, string step) => Stack(root, stack)["steps"]![step]!.AsObject();

    [Fact]
    public void TheEmbeddedFile_IsTheFileOfShared_ByteForByte()
    {
        using var stream = typeof(ToolchainDefaults).Assembly.GetManifestResourceStream(ToolchainDefaults.ResourceName);
        stream.Should().NotBeNull("the binary reads only the embedded copy");
        using var reader = new StreamReader(stream!);

        reader.ReadToEnd().Should().Be(Shipped);
    }

    [Fact]
    public void TheStacks_AreTheAdaptersOfTheToolchainTables_AndOnlyDotnetAndNodeAreBuilt()
    {
        var table = ToolchainDefaults.Current;

        table.Ids.Should().BeEquivalentTo(ToolchainTables.Adapters);
        table.Built.Select(stack => stack.Id).Should().Equal("dotnet", "node");
        table.Stacks["php"].Manifests.Should().Equal("composer.json");
        table.Stacks["rust"].Manifests.Should().Equal("Cargo.toml");
        table.Stacks["python"].Manifests.Should().Contain(["pyproject.toml", "requirements.txt"]);
        table.Stacks["java"].Manifests.Should().Contain(["pom.xml", "build.gradle"]);
        table.Stacks.Values.Where(stack => !stack.Built).Should().OnlyContain(stack => stack.Steps.Count == 0 && stack.Switches.Count == 0);
    }

    [Fact]
    public void ABuiltStack_HasTheArgvOfEveryStepAsStoryS1MeasuredThem()
    {
        var dotnet = ToolchainDefaults.Current.Stacks["dotnet"];
        var node = ToolchainDefaults.Current.Stacks["node"];

        dotnet.Steps["restore"]!.Argv.Should().Equal("dotnet", "restore", "{unit}");
        dotnet.Steps["build"]!.Argv.Should().Equal("dotnet", "build", "{unit}", "-c", "Debug", "--no-restore", "-m:{jobs}");
        dotnet.Steps["typecheck"].Should().BeNull("dotnet has no typecheck step");
        dotnet.Steps["test"]!.ByRunner["vstest"].Should().Equal("dotnet", "test", "{unit}", "--no-build", "-c", "Debug");
        dotnet.Steps["test"]!.ByRunner["mtp"].Should().Equal("dotnet", "test", "--project", "{unit}", "--no-build", "-c", "Debug", "--no-launch-profile");
        dotnet.Steps["format"]!.Argv.Should().Equal("dotnet", "format", "whitespace", ".", "--folder", "--include", "{file}");
        dotnet.Steps["format"]!.Extensions.Should().Equal(".cs");
        dotnet.Switches.Keys.Should().Equal("NO_COLOR", "DOTNET_NOLOGO", "DOTNET_CLI_TELEMETRY_OPTOUT", "MSBUILDDISABLENODEREUSE");
        dotnet.NoTestRan["mtp"].ExitCodes.Should().Equal(8);
        dotnet.NoTestRan["vstest"].ExitCodes.Should().BeEmpty("VSTest exits 0 for a run in which no test matched: only its text says so");
        dotnet.NoTestRan["vstest"].Markers.Should().Equal("No test matches the given testcase filter");

        node.Steps["restore"]!.Argv.Should().Equal("npm", "ci", "--ignore-scripts");
        node.Steps["build"]!.Argv.Should().Equal("npm", "run", "build");
        node.Steps["typecheck"]!.Argv.Should().Equal("npm", "run", "typecheck");
        node.Steps["typecheck"]!.Fallback.Should().Equal("npx", "--no-install", "tsc", "--noEmit");
        node.Steps["test"]!.ByRunner.Keys.Should().BeEquivalentTo("jest", "vitest", "node-test", "unknown");
        node.Steps["format"]!.Argv.Should().Equal("npx", "--no-install", "prettier", "--write", "--ignore-unknown", "{file}");
        node.Steps["format"]!.Extensions.Should().Contain([".js", ".json", ".ts"]);
        node.Switches.Keys.Should().Equal("NO_COLOR", "CI");
        node.NoTestRan["jest"].Markers.Should().Equal("No tests found, exiting with code 1");
        node.NoTestRan["vitest"].Markers.Should().Equal("No test files found, exiting with code 1");
        node.NoTestRan["node-test"].Markers.Should().BeEmpty("node:test counts a file with no test as a passing test: nothing says that no test ran");
    }

    [Fact]
    public void ThereIsNoDefaultTestFor()
    {
        Shipped.Should().NotContain("\"testFor\"").And.NotContain("\"testFiles\"");
    }

    [Fact]
    public void TheNetworkOfEachStep_IsTheMeasuredOne()
    {
        var table = ToolchainDefaults.Current;

        table.Built.SelectMany(stack => stack.Steps.Where(step => step.Value is { Network: DefaultNetwork.Host }).Select(step => $"{stack.Id}.{step.Key}"))
            .Should().BeEquivalentTo("dotnet.restore", "node.restore");
    }

    [Theory]
    [InlineData("an unknown member of a stack")]
    [InlineData("an unknown member of a step")]
    [InlineData("an unknown member at the top")]
    [InlineData("an unknown step name")]
    [InlineData("an unknown placeholder")]
    [InlineData("{file} in a build step")]
    [InlineData("{file} inside an item")]
    [InlineData("a host step with no measure")]
    [InlineData("extensions on a step that is not a formatter")]
    [InlineData("a step with no argv")]
    [InlineData("a built stack that is not built but has steps")]
    [InlineData("a network word nobody knows")]
    public void AnythingTheStrictReaderDoesNotKnow_IsAFaultThatNamesWhereItIs(string plant)
    {
        var root = Edited(r =>
        {
            switch (plant)
            {
                case "an unknown member of a stack": Stack(r, "dotnet")["extra"] = 1; break;
                case "an unknown member of a step": Step(r, "dotnet", "build")["extra"] = 1; break;
                case "an unknown member at the top": r["extra"] = 1; break;
                case "an unknown step name": Stack(r, "dotnet")["steps"]!.AsObject()["lint"] = JsonNode.Parse("""{"argv":["x"],"network":"none"}"""); break;
                case "an unknown placeholder": Step(r, "dotnet", "build")["argv"] = JsonNode.Parse("""["dotnet","build","{project}"]"""); break;
                case "{file} in a build step": Step(r, "dotnet", "build")["argv"] = JsonNode.Parse("""["dotnet","build","{file}","--no-restore"]"""); break;
                case "{file} inside an item": Step(r, "node", "format")["argv"] = JsonNode.Parse("""["npx","--no-install","prettier","--write","x{file}"]"""); break;
                case "a host step with no measure": Step(r, "node", "restore").Remove("measuredIn"); break;
                case "extensions on a step that is not a formatter": Step(r, "node", "build")["extensions"] = JsonNode.Parse("""[".js"]"""); break;
                case "a step with no argv": Step(r, "node", "build").Remove("argv"); break;
                case "a built stack that is not built but has steps": Stack(r, "php")["steps"] = new JsonObject(); break;
                case "a network word nobody knows": Step(r, "node", "build")["network"] = "lan"; break;
                default: throw new InvalidOperationException(plant);
            }
        });

        var thrown = Record.Exception(() => ToolchainDefaults.Parse(Text(root)));

        thrown.Should().BeOfType<ToolchainDefaultsException>();
        thrown!.Message.Should().NotBeEmpty();
    }

    [Fact]
    public void AFault_NamesThePathInTheFile()
    {
        var root = Edited(r => Step(r, "dotnet", "build")["argv"] = JsonNode.Parse("""["dotnet","build","{project}"]"""));

        var thrown = Record.Exception(() => ToolchainDefaults.Parse(Text(root)));

        thrown!.Message.Should().Contain("stacks.dotnet.steps.build.argv[2]").And.Contain("{project}");
    }

    [Fact]
    public void TextThatIsNotJson_IsAFault()
    {
        Record.Exception(() => ToolchainDefaults.Parse("{ nope")).Should().BeOfType<ToolchainDefaultsException>();
    }

    // ---- The scan: the strict rules of SPEC §A3, each with a planted line that it finds ----

    private static readonly string[] Shells = ["sh", "bash", "zsh", "dash", "ash", "fish", "cmd", "cmd.exe", "powershell", "pwsh", "/bin/sh", "/bin/bash", "env"];

    /// <summary>Every rule of the strict list, over every argv of a defaults file; the sentences it finds, one per fault.</summary>
    internal static IReadOnlyList<string> Violations(ToolchainDefaults table, string repoRoot)
    {
        var found = new List<string>();
        foreach (var stack in table.Built)
        {
            foreach (var (name, step) in stack.Steps.Where(step => step.Value is not null))
            {
                var where = $"{stack.Id}.{name}";
                foreach (var argv in Argvs(step!))
                {
                    if (Shells.Contains(argv[0], StringComparer.OrdinalIgnoreCase))
                    {
                        found.Add($"{where}: a shell program ({argv[0]}); no shell anywhere");
                    }

                    if (argv.Count > 1 && argv[0] == "npm" && argv[1] is "install" or "i" or "add")
                    {
                        found.Add($"{where}: npm install; the restore is `npm ci`");
                    }

                    if (argv[0] == "npx" && !argv.Contains("--no-install") && !argv.Contains("--no"))
                    {
                        found.Add($"{where}: npx without --no-install would download a tool at check time");
                    }

                    if (argv.Count > 1 && argv[0] == "npm" && argv[1] == "ci" && !argv.Contains("--ignore-scripts"))
                    {
                        found.Add($"{where}: npm ci without --ignore-scripts runs install scripts");
                    }

                    if (argv.Count > 1 && argv[0] == "dotnet" && argv[1] == "test" && !argv.Contains("--no-build"))
                    {
                        found.Add($"{where}: dotnet test without --no-build builds, outside the build step");
                    }
                }

                if (step!.Network == DefaultNetwork.Host && step.MeasuredIn is { } measured && !HeadingExists(repoRoot, measured))
                {
                    found.Add($"{where}: the host network names '{measured.Heading}' of {measured.File}, which is not a heading there");
                }
            }
        }

        return found;
    }

    private static IEnumerable<IReadOnlyList<string>> Argvs(DefaultStep step) => [.. new[] { step.Argv, step.Fallback }.Concat(step.ByRunner.Values).Where(argv => argv.Count > 0)];

    private static bool HeadingExists(string repoRoot, MeasuredIn measured)
    {
        var path = Path.Combine(repoRoot, measured.File);

        return File.Exists(path) && File.ReadLines(path).Any(line => line.StartsWith('#') && line.TrimStart('#').Trim() == measured.Heading);
    }

    [Fact]
    public void TheShippedFile_HoldsEveryStrictRule()
    {
        Violations(ToolchainDefaults.Current, RepoPaths.Root()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("a shell program", "a shell program")]
    [InlineData("npm install", "npm install")]
    [InlineData("npx without --no-install", "npx without --no-install")]
    [InlineData("npm ci without --ignore-scripts", "npm ci without --ignore-scripts")]
    [InlineData("dotnet test without --no-build", "dotnet test without --no-build")]
    [InlineData("a heading that is not there", "is not a heading there")]
    [InlineData("a file that is not there", "is not a heading there")]
    public void EachRuleOfTheScan_FindsItsPlantedLine(string plant, string sentence)
    {
        var root = Edited(r =>
        {
            switch (plant)
            {
                case "a shell program": Step(r, "node", "build")["argv"] = JsonNode.Parse("""["sh","-c","npm run build"]"""); break;
                case "npm install": Step(r, "node", "restore")["argv"] = JsonNode.Parse("""["npm","install","--ignore-scripts"]"""); break;
                case "npx without --no-install": Step(r, "node", "typecheck")["fallback"] = JsonNode.Parse("""["npx","tsc","--noEmit"]"""); break;
                case "npm ci without --ignore-scripts": Step(r, "node", "restore")["argv"] = JsonNode.Parse("""["npm","ci"]"""); break;
                case "dotnet test without --no-build": Step(r, "dotnet", "test")["byRunner"]!.AsObject()["vstest"] = JsonNode.Parse("""["dotnet","test","{unit}"]"""); break;
                case "a heading that is not there": Step(r, "node", "restore")["measuredIn"]!.AsObject()["heading"] = "A heading nobody wrote"; break;
                case "a file that is not there": Step(r, "dotnet", "restore")["measuredIn"]!.AsObject()["file"] = "v2/epoch-3/e4-adapters-coder-hardening/RESULTS_nobody.md"; break;
                default: throw new InvalidOperationException(plant);
            }
        });

        var found = Violations(ToolchainDefaults.Parse(Text(root)), RepoPaths.Root());

        found.Should().ContainSingle(line => line.Contains(sentence), "the planted line must be found, and only it: " + string.Join(" | ", found));
    }

    [Fact]
    public void TheScanItself_FindsSomethingOnAFileThatBreaksEveryRule()
    {
        // The companion of the scan: a file that breaks all of them gives all of them — a scan that matched nothing would be green on a bad file.
        var root = Edited(r =>
        {
            Step(r, "node", "build")["argv"] = JsonNode.Parse("""["bash","-c","x"]""");
            Step(r, "node", "restore")["argv"] = JsonNode.Parse("""["npm","install"]""");
            Step(r, "node", "typecheck")["fallback"] = JsonNode.Parse("""["npx","tsc"]""");
            Step(r, "dotnet", "test")["byRunner"]!.AsObject()["vstest"] = JsonNode.Parse("""["dotnet","test","{unit}"]""");
            Step(r, "dotnet", "restore")["measuredIn"]!.AsObject()["heading"] = "nope";
        });

        Violations(ToolchainDefaults.Parse(Text(root)), RepoPaths.Root()).Should().HaveCountGreaterThanOrEqualTo(5);
    }

    [Fact]
    public void TheManifestNamesOfEveryStack_AreNamesAndNeverPaths()
    {
        ToolchainDefaults.Current.AllManifestNames.Should().OnlyContain(name => !name.Contains('/') && !name.Contains('\\') && !name.Contains(".."));
    }
}

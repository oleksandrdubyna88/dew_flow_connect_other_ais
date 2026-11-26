using CoaiMcp.Runners.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E4 s1 t1 (M1, M2, M3, M10): the .NET commands of a detected project, run INSIDE the check unit of E2 exactly as a coder job's steps are — the product's own <see cref="CheckBoundary"/> and <see cref="BoundaryJob"/>, no property changed — on a clone of this
/// repository and on scratch projects (a VSTest project, an MTP project with the runner in <c>global.json</c>, one without). The numbers and the recorded consoles are in <c>v2/epoch-3/e4-adapters-coder-hardening/RESULTS_s1_dotnet.md</c> and
/// <c>src_mcp/tests/fixtures/toolchain</c>.
/// </summary>
/// <remarks>
/// Skipped unless <c>COAI_RECORD_DOTNET</c> names an ABSENT folder on a real disk with a short path. It needs the network (a cold restore), builds this repository once, and takes about 10 minutes. It uses the stand-in home of <see cref="MeasureBed"/>, never the real one,
/// and starts no model.
/// </remarks>
[Collection("fakecli-env")]
public sealed class ADotnetStepsAreMeasuredTests
{
    private const string RecordVariable = "COAI_RECORD_DOTNET";

    private const string Sdk = """{ "sdk": { "version": "10.0.100", "rollForward": "latestFeature" } }""";

    private const string SdkWithMtp = """{ "sdk": { "version": "10.0.100", "rollForward": "latestFeature" }, "test": { "runner": "Microsoft.Testing.Platform" } }""";

    private const string VsTestProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <IsPackable>false</IsPackable>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
            <PackageReference Include="xunit" Version="2.9.3" />
            <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
          </ItemGroup>
        </Project>
        """;

    private const string MtpProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="xunit.v3" Version="4.0.2" />
          </ItemGroup>
        </Project>
        """;

    private const string BadRestoreProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Coai.Scratch.NoSuchPackage.Anywhere" Version="1.2.3" />
          </ItemGroup>
        </Project>
        """;

    // One passing and two failing tests, one of them failing by an assertion and one by an exception: the three lines a digest reader must tell apart.
    private const string Tests = """
        using Xunit;

        namespace Scratch;

        public class SampleTests
        {
            [Fact]
            public void PassesAlways() => Assert.Equal(2, 1 + 1);

            [Fact]
            public void FailsOnTheValue() => Assert.Equal(3, 1 + 1);

            [Fact]
            public void FailsByThrowing() => throw new InvalidOperationException("boom from the scratch test");
        }

        public class OtherTests
        {
            [Fact]
            public void AlsoPasses() => Assert.True(true);
        }
        """;

    private static IReadOnlyDictionary<string, string> Scratch() => new Dictionary<string, string>
    {
        [".gitignore"] = "bin/\nobj/\n",
        ["vstest/global.json"] = Sdk,
        ["vstest/Scratch.Vs.csproj"] = VsTestProject,
        ["vstest/SampleTests.cs"] = Tests,
        ["mtp-global/global.json"] = SdkWithMtp,
        ["mtp-global/Scratch.Mtp.csproj"] = MtpProject,
        ["mtp-global/SampleTests.cs"] = Tests,
        ["mtp-plain/global.json"] = Sdk,
        ["mtp-plain/Scratch.Mtp.csproj"] = MtpProject,
        ["mtp-plain/SampleTests.cs"] = Tests,
        ["badrestore/global.json"] = Sdk,
        ["badrestore/Scratch.Bad.csproj"] = BadRestoreProject,
        ["badrestore/Program.cs"] = "System.Console.WriteLine(\"x\");\n",
    };

    private static string Root => Environment.GetEnvironmentVariable(RecordVariable) ?? string.Empty;

    [Fact]
    public async Task ARecordedRunOfTheDotnetCommandsInsideTheUnit()
    {
        Assert.SkipWhen(Root.Length == 0, $"set {RecordVariable} to a new folder on a real disk with a short path to measure the dotnet commands inside the check unit (needs the network, about 10 minutes)");
        Assert.SkipUnless(SystemdUser.Works(out var why), why);
        Directory.Exists(Root).Should().BeFalse($"{Root} must not exist: the run makes it");

        await ThisRepositoryAsync(Path.Combine(Root, "repo"));
        await ScratchProjectsAsync(Path.Combine(Root, "scratch"));
        await RootedProjectsAsync(Path.Combine(Root, "rooted"));
    }

    private static async Task ThisRepositoryAsync(string folder)
    {
        await using var bed = await MeasureBed.CreateAsync(folder, _ => Task.CompletedTask, withNode: false, cloneOf: RepoPaths.Root());
        await bed.StepAsync("r00-sdk", host: false, ["dotnet", "--version"]);
        await bed.StepAsync("r00-sdks", host: false, ["dotnet", "--list-sdks"]);
        var sln = "dew_flow_connect_other_ais.slnx";

        // M1: a cold restore on the host network, then the build with only a loopback device.
        await bed.StepAsync("r01-restore-host", host: true, ["dotnet", "restore", sln]);
        var build = await bed.StepAsync("r02-build-no-restore-private", host: false, ["dotnet", "build", sln, "-c", "Debug", "--no-restore", "-m:4"]);
        await bed.StepAsync("r03-build-with-restore-private-control", host: false, ["dotnet", "build", sln, "-c", "Debug", "-m:4"], timeout: TimeSpan.FromMinutes(10));
        Note(bed, "git status after the build", await bed.StatusAsync());

        if (build.ExitCode == 0)
        {
            // M3 and M10: the test executable of this repository. Two named classes, then a name that matches nothing, then the detailed listing.
            var exe = "./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests";
            await bed.StepAsync("r10-exe-two-classes", host: false, [exe, "--filter-class", "CoaiMcp.Tests.AFailureDigestTests", "--filter-class", "CoaiMcp.Tests.AToolchainFileTests"]);
            await bed.StepAsync("r11-exe-two-classes-detailed", host: false, [exe, "--output", "Detailed", "--filter-class", "CoaiMcp.Tests.AFailureDigestTests", "--filter-class", "CoaiMcp.Tests.AToolchainFileTests"]);
            await bed.StepAsync("r12-exe-one-class-wildcard", host: false, [exe, "--filter-class", "*.AFailureDigestTests"]);
            await bed.StepAsync("r13-exe-filter-matches-nothing", host: false, [exe, "--filter-class", "CoaiMcp.Tests.NoSuchClassAnywhere"]);
            await bed.StepAsync("r14-exe-list-tests-one-class", host: false, [exe, "--list-tests", "--filter-class", "CoaiMcp.Tests.AFailureDigestTests"]);
            await bed.StepAsync("r15-exe-help", host: false, [exe, "--help"]);
            // The form the detector would use for this repository (an MTP project of a solution whose global.json opts in), narrowed to one class so that it takes seconds: the instructions of this repository say `dotnet test` does not work here, and nothing had measured it.
            await bed.StepAsync("r17-dotnet-test-project-form-this-repository", host: false, ["dotnet", "test", "--project", "src_mcp/tests/CoaiMcp.Tests.csproj", "--no-build", "-c", "Debug", "--filter-class", "CoaiMcp.Tests.AFailureDigestTests"], timeout: TimeSpan.FromMinutes(5));
            await bed.StepAsync("r16-exe-two-classes-minimum-expected", host: false, [exe, "--minimum-expected-tests", "1000", "--filter-class", "CoaiMcp.Tests.AFailureDigestTests"]);
        }

        Note(bed, "git status after the test runs", await bed.StatusAsync());
        await bed.FinishAsync();
        build.ExitCode.Should().Be(0, "the build of this repository inside the unit is the measure everything after it depends on; see the log");
    }

    private static async Task ScratchProjectsAsync(string folder)
    {
        await using var bed = await MeasureBed.CreateAsync(folder, main => MeasureBed.WriteAsync(main, Scratch()), withNode: false);
        foreach (var project in new[] { "vstest", "mtp-global", "mtp-plain", "badrestore" })
        {
            await bed.StepAsync($"s0-{project}-sdk", host: false, ["dotnet", "--version"], project);
        }

        // M1 on a scratch VSTest project, and the restores of the others (host network).
        await bed.StepAsync("s1-vstest-restore-host", host: true, ["dotnet", "restore"], "vstest");
        await bed.StepAsync("s1-mtp-global-restore-host", host: true, ["dotnet", "restore"], "mtp-global");
        await bed.StepAsync("s1-mtp-plain-restore-host", host: true, ["dotnet", "restore"], "mtp-plain");
        var bad = await bed.StepAsync("s1-badrestore-restore-host", host: true, ["dotnet", "restore"], "badrestore");
        await bed.SaveFixtureAsync("dotnet-restore-error.txt", bad.Text);
        await bed.StepAsync("s2-vstest-build-no-restore-private", host: false, ["dotnet", "build", "-c", "Debug", "--no-restore"], "vstest");
        await bed.StepAsync("s2-mtp-global-build-no-restore-private", host: false, ["dotnet", "build", "-c", "Debug", "--no-restore"], "mtp-global");
        await bed.StepAsync("s2-mtp-plain-build-no-restore-private", host: false, ["dotnet", "build", "-c", "Debug", "--no-restore"], "mtp-plain");

        // M2: dotnet test, private network, after the build. The project is named by the form the default argv would use: a path, and the working folder.
        var vsFailed = await bed.StepAsync("t1-vstest-test-no-build", host: false, ["dotnet", "test", "--no-build", "-c", "Debug"], "vstest");
        await bed.SaveFixtureAsync("dotnet-vstest-failed.txt", vsFailed.Text);
        var vsPath = await bed.StepAsync("t1-vstest-test-project-path-from-root", host: false, ["dotnet", "test", "vstest/Scratch.Vs.csproj", "--no-build", "-c", "Debug"]);
        await bed.SaveFixtureAsync("dotnet-vstest-failed-from-root.txt", vsPath.Text);
        var vsZero = await bed.StepAsync("t1-vstest-zero-tests", host: false, ["dotnet", "test", "--no-build", "-c", "Debug", "--filter", "FullyQualifiedName~NoSuchTestAnywhere"], "vstest");
        await bed.SaveFixtureAsync("dotnet-vstest-zero-tests.txt", vsZero.Text);
        await bed.StepAsync("t1-vstest-passed-only", host: false, ["dotnet", "test", "--no-build", "-c", "Debug", "--filter", "FullyQualifiedName~OtherTests"], "vstest");
        var vsNames = await bed.StepAsync("t1-vstest-detailed-logger", host: false, ["dotnet", "test", "--no-build", "-c", "Debug", "--logger", "console;verbosity=detailed"], "vstest");
        await bed.SaveFixtureAsync("dotnet-vstest-detailed.txt", vsNames.Text);
        await bed.StepAsync("t1-vstest-trx-logger-to-job-folder", host: false, ["dotnet", "test", "--no-build", "-c", "Debug", "--logger", $"trx;LogFileName={BoundaryUnit.UnitTemp}/scratch.trx", "--results-directory", $"{BoundaryUnit.UnitTemp}/trx"], "vstest");

        var mtpFailed = await bed.StepAsync("t2-mtp-global-test-no-build", host: false, ["dotnet", "test", "--no-build", "-c", "Debug"], "mtp-global");
        await bed.SaveFixtureAsync("dotnet-mtp-global-failed.txt", mtpFailed.Text);
        var mtpPath = await bed.StepAsync("t2-mtp-global-test-project-from-root", host: false, ["dotnet", "test", "--project", "mtp-global/Scratch.Mtp.csproj", "--no-build", "-c", "Debug"]);
        await bed.SaveFixtureAsync("dotnet-mtp-global-failed-from-root.txt", mtpPath.Text);
        var mtpZero = await bed.StepAsync("t2-mtp-global-zero-tests", host: false, ["dotnet", "test", "--no-build", "-c", "Debug", "--filter-class", "Scratch.NoSuchClass"], "mtp-global");
        await bed.SaveFixtureAsync("dotnet-mtp-global-zero-tests.txt", mtpZero.Text);
        await bed.StepAsync("t2-mtp-global-passed-only", host: false, ["dotnet", "test", "--no-build", "-c", "Debug", "--filter-class", "Scratch.OtherTests"], "mtp-global");
        var mtpNames = await bed.StepAsync("t2-mtp-global-detailed", host: false, ["dotnet", "test", "--no-build", "-c", "Debug", "--output", "Detailed"], "mtp-global");
        await bed.SaveFixtureAsync("dotnet-mtp-global-detailed.txt", mtpNames.Text);
        await bed.StepAsync("t2-mtp-global-test-help", host: false, ["dotnet", "test", "--help"], "mtp-global");

        var plain = await bed.StepAsync("t3-mtp-plain-test-no-build", host: false, ["dotnet", "test", "--no-build", "-c", "Debug"], "mtp-plain");
        await bed.SaveFixtureAsync("dotnet-mtp-plain-error.txt", plain.Text);
        await bed.StepAsync("t3-mtp-plain-test-project-from-root", host: false, ["dotnet", "test", "--project", "mtp-plain/Scratch.Mtp.csproj", "--no-build", "-c", "Debug"]);
        await bed.StepAsync("t3-mtp-plain-run-the-exe-directly", host: false, ["./mtp-plain/bin/Debug/net10.0/Scratch.Mtp"]);

        Note(bed, "git status after the runs", await bed.StatusAsync());
        await bed.FinishAsync();
    }

    /// <summary>A global.json at the REPOSITORY ROOT names the MTP runner (as this repository's does): the forms of `dotnet test` that name a project from the root, and a VSTest project under that setting.</summary>
    private static async Task RootedProjectsAsync(string folder)
    {
        var files = new Dictionary<string, string>
        {
            [".gitignore"] = "bin/\nobj/\n",
            ["global.json"] = SdkWithMtp,
            ["app/Scratch.Mtp.csproj"] = MtpProject,
            ["app/SampleTests.cs"] = Tests,
            ["vs/Scratch.Vs.csproj"] = VsTestProject,
            ["vs/SampleTests.cs"] = Tests,
        };
        await using var bed = await MeasureBed.CreateAsync(folder, main => MeasureBed.WriteAsync(main, files), withNode: false);
        await bed.StepAsync("x0-sdk-at-root", host: false, ["dotnet", "--version"]);
        await bed.StepAsync("x1-restore-app", host: true, ["dotnet", "restore", "app/Scratch.Mtp.csproj"]);
        await bed.StepAsync("x1-restore-vs", host: true, ["dotnet", "restore", "vs/Scratch.Vs.csproj"]);
        await bed.StepAsync("x2-build-app", host: false, ["dotnet", "build", "app/Scratch.Mtp.csproj", "-c", "Debug", "--no-restore"]);
        await bed.StepAsync("x2-build-vs", host: false, ["dotnet", "build", "vs/Scratch.Vs.csproj", "-c", "Debug", "--no-restore"]);
        var project = await bed.StepAsync("x3-mtp-test-project-from-root", host: false, ["dotnet", "test", "--project", "app/Scratch.Mtp.csproj", "--no-build", "-c", "Debug"]);
        await bed.SaveFixtureAsync("dotnet-mtp-rooted-project-failed.txt", project.Text);
        await bed.StepAsync("x3-mtp-test-project-from-root-zero", host: false, ["dotnet", "test", "--project", "app/Scratch.Mtp.csproj", "--no-build", "-c", "Debug", "--filter-class", "Scratch.NoSuchClass"]);
        await bed.StepAsync("x3-mtp-test-project-from-root-detailed", host: false, ["dotnet", "test", "--project", "app/Scratch.Mtp.csproj", "--no-build", "-c", "Debug", "--output", "Detailed", "--filter-class", "Scratch.OtherTests"]);
        await bed.StepAsync("x3-mtp-test-no-project-at-root", host: false, ["dotnet", "test", "--no-build", "-c", "Debug"]);
        await bed.StepAsync("x3-mtp-test-old-form-positional", host: false, ["dotnet", "test", "app/Scratch.Mtp.csproj", "--no-build", "-c", "Debug"]);
        var vs = await bed.StepAsync("x4-vstest-project-under-an-mtp-global-json", host: false, ["dotnet", "test", "--project", "vs/Scratch.Vs.csproj", "--no-build", "-c", "Debug"]);
        await bed.SaveFixtureAsync("dotnet-vstest-under-mtp-global-json.txt", vs.Text);
        await bed.StepAsync("x4-vstest-project-under-an-mtp-global-json-in-its-folder", host: false, ["dotnet", "test", "--no-build", "-c", "Debug"], "vs");
        Note(bed, "git status after the runs", await bed.StatusAsync());
        await bed.FinishAsync();
    }

    private static void Note(MeasureBed bed, string title, string text)
    {
        bed.Note($"{title}: {(text.Length == 0 ? "(clean)" : text.Replace('\n', '|'))}");
    }
}

using System.Text.Json;
using CoaiMcp.Install;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>agy answers <c>unmeasured</c> and writes nothing, until its measurement is complete on Linux AND Windows and recorded in <c>shared/install-clients.json</c>.</summary>
public sealed class AgyIsUnmeasuredTests
{
    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task AFoundAgy_IsReportedUnmeasured_AndItsFileIsNotMade()
    {
        using var world = InstallWorld.Create(claude: false, codex: false);
        var agy = Path.Combine(world.Bin, "agy");
        world.Put(agy, "#!/bin/sh\necho 1.5.14\n");
        File.SetUnixFileMode(agy, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the agy stand-in here is a shell file");

        var (code, output, _) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(0, "an unmeasured CLI is reported, not refused, and it is a CLI that was found");
        output.Result("agy").Should().Be("unmeasured");
        output.Client("agy").GetProperty("version").GetString().Should().Be("1.5.14");
        output.Action("agy").Should().Be("none");
        output.Client("agy").GetProperty("reason").GetString().Should().Contain("unmeasured");
        Directory.Exists(Path.Combine(world.Home, ".gemini")).Should().BeFalse();
        world.BackupFolders.Should().BeEmpty();
    }

    [Fact]
    public void TheRecordSaysUnmeasured_WhileTheCodeDoes_AndTheMeasurementIsInTheRecord()
    {
        var record = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root(), "shared", "install-clients.json"))).RootElement;

        record.GetProperty("agy").GetProperty("status").GetString().Should().Be("unmeasured", "the code answers unmeasured; flipping this is a decision with the Windows measurement behind it");
        record.GetProperty("agy").GetProperty("version").GetString().Should().Be("1.5.14");
        record.GetProperty("agy").GetProperty("measured").GetProperty("linux").GetProperty("file").GetString().Should().Be("~/.gemini/config/mcp_config.json");
        record.GetProperty("agy").GetProperty("measured").GetProperty("windows").GetProperty("status").GetString().Should().Be("not measured");
        AgyMcp.Unmeasured.Should().Contain("Windows is not measured");
    }
}

using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A settings file that is THERE and gave no values is told apart from one that is not there, and from one held open by its writer
/// (<c>todo/PLAN_auto_never_asks_a_person_E1_S1_spec.md</c>, § S1.5.2).
/// </summary>
/// <remarks>
/// Why it matters: the default a failed read gives is policy auto, which ends rounds and holds the gate for nobody. A server that cannot
/// tell "the operator wrote nothing" from "the operator's file could not be read" cannot refuse to act on the second. The read is driven
/// through the <c>readText</c> seam, as the host's own tests drive it, so the busy and the denied rows need no real file lock or ACL.
/// </remarks>
public sealed class ASettingsReadThatFailedTests
{
    private const string Path = "settings.json";

    public static TheoryData<string> Values() => ["{}", "an object with one value"];

    public static TheoryData<string, string> Unusable() => new()
    {
        { "an empty file", "it is not JSON this server can read" },
        { "text that is not JSON", "it is not JSON this server can read" },
        { "an array", "it is not a JSON object" },
        { "null", "it is not a JSON object" },
        { "a denied read", "access to it was denied" },
        { "a plain IOException", "it could not be opened" },
    };

    public static TheoryData<string> Busy() => ["a sharing violation", "a lock violation"];

    public static TheoryData<string> Missing() => ["FileNotFoundException", "DirectoryNotFoundException"];

    private static Func<string, string> Seam(string row) => row switch
    {
        "{}" => _ => "{}",
        "an object with one value" => _ => """{"COAI_ON_EXHAUSTED":"human"}""",
        "an empty file" => _ => string.Empty,
        "text that is not JSON" => _ => "not json",
        "an array" => _ => "[]",
        "null" => _ => "null",
        "a denied read" => p => throw new UnauthorizedAccessException($"Access to the path '{p}' is denied."),
        "a plain IOException" => _ => throw new IOException("the device is not ready"),
        "a sharing violation" => _ => throw new IOException("in use", unchecked((int)0x80070020)),
        "a lock violation" => _ => throw new IOException("locked", unchecked((int)0x80070021)),
        "FileNotFoundException" => p => throw new FileNotFoundException("not there", p),
        "DirectoryNotFoundException" => _ => throw new DirectoryNotFoundException("no folder"),
        _ => throw new ArgumentOutOfRangeException(nameof(row), row, null),
    };

    [Theory]
    [MemberData(nameof(Values))]
    public void TryRead_GivesTheValuesOfAnObject(string row)
    {
        var read = SettingsFile.TryRead(Path, Seam(row));

        read.Should().BeOfType<SettingsRead.Values>().Which.Settings.Should().HaveCount(row == "an object with one value" ? 1 : 0);
        ReadAsTheOneShotModesDo(row).Should().Be(row == "an object with one value" ? 1 : 0, row);
    }

    [Theory]
    [MemberData(nameof(Unusable))]
    public void TryRead_TellsAFileThatGaveNothing_WithTheReason(string row, string why)
    {
        SettingsFile.TryRead(Path, Seam(row)).Should().Be(new SettingsRead.Unusable(why), row);
        ReadAsTheOneShotModesDo(row).Should().Be(0, row);
    }

    [Theory]
    [MemberData(nameof(Busy))]
    public void TryRead_TellsAFileHeldByItsWriter_AsBusy(string row)
    {
        SettingsFile.TryRead(Path, Seam(row)).Should().BeOfType<SettingsRead.Busy>(row);
        ReadAsTheOneShotModesDo(row).Should().Be(0, row);
    }

    [Theory]
    [MemberData(nameof(Missing))]
    public void TryRead_TellsAFileThatIsNotThere_AsMissing(string row)
    {
        SettingsFile.TryRead(Path, Seam(row)).Should().BeOfType<SettingsRead.Missing>(row);
        ReadAsTheOneShotModesDo(row).Should().Be(0, row);
    }

    // The one-shot modes read as they always did: nothing but the second Values row has a value to give.
    private static int ReadAsTheOneShotModesDo(string row) => SettingsFile.Read(Path, Seam(row)).Count;
}

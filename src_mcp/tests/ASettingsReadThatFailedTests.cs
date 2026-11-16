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

    public static TheoryData<string> Rows() =>
    [
        "{}",
        "an object with one value",
        "an empty file",
        "text that is not JSON",
        "an array",
        "null",
        "a denied read",
        "a plain IOException",
        "a sharing violation",
        "a lock violation",
        "FileNotFoundException",
        "DirectoryNotFoundException",
    ];

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
    [MemberData(nameof(Rows))]
    public void TryRead_TellsAFileThatGaveNothing_FromOneThatIsNotThere(string row)
    {
        var read = SettingsFile.TryRead(Path, Seam(row));

        switch (row)
        {
            case "{}":
                read.Should().BeOfType<SettingsRead.Values>().Which.Settings.Should().BeEmpty();
                break;
            case "an object with one value":
                read.Should().BeOfType<SettingsRead.Values>().Which.Settings.Should().HaveCount(1);
                break;
            case "an empty file" or "text that is not JSON":
                read.Should().Be(new SettingsRead.Unusable("it is not JSON this server can read"), row);
                break;
            case "an array" or "null":
                read.Should().Be(new SettingsRead.Unusable("it is not a JSON object"), row);
                break;
            case "a denied read":
                read.Should().Be(new SettingsRead.Unusable("access to it was denied"), row);
                break;
            case "a plain IOException":
                read.Should().Be(new SettingsRead.Unusable("it could not be opened"), row);
                break;
            case "a sharing violation" or "a lock violation":
                read.Should().BeOfType<SettingsRead.Busy>(row);
                break;
            default:
                read.Should().BeOfType<SettingsRead.Missing>(row);
                break;
        }

        // The one-shot modes read as they always did: nothing but the second row has a value to give.
        SettingsFile.Read(Path, Seam(row)).Count.Should().Be(row == "an object with one value" ? 1 : 0, row);
    }
}

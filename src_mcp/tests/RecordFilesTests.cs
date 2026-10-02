using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The record-directory mechanics the four stores share (S4b item 13): the id guard, the stamp and its parse, the read
/// that a torn file answers with nothing, and the delete that says when it could not delete.
/// </summary>
public sealed class RecordFilesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-recordfiles-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { }
    }

    [Fact]
    public void AnIdIsAFileNameOnlyWhenItIsOneOfOurs()
    {
        RecordFiles.IsRecordId(Guid.NewGuid().ToString("N")).Should().BeTrue();
        foreach (var hostile in (string?[])[null, "", "../../settings", "a/b", new string('f', 31), new string('f', 33), new string('F', 32)])
        {
            RecordFiles.IsRecordId(hostile).Should().BeFalse($"'{hostile}' is not a record id");
        }
    }

    [Fact]
    public void AStampRoundTripsAsUtc_AndWhatIsNoInstantFallsBack()
    {
        var at = new DateTime(2026, 10, 2, 12, 34, 56, DateTimeKind.Utc);
        var fallback = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        RecordFiles.Parse(RecordFiles.Stamp(at), fallback).Should().Be(at);
        RecordFiles.Parse("2026-10-02T14:34:56+02:00", fallback).Should().Be(at, "an offset is read back as the same instant in UTC");
        RecordFiles.Parse("not a time", fallback).Should().Be(fallback);
        RecordFiles.Parsed(null).Should().BeNull();
        RecordFiles.Parsed(string.Empty).Should().BeNull();
    }

    [Fact]
    public void AReadAnswersTheRecord_AndNothingForAnAbsentOrTornFile()
    {
        var path = Path.Combine(_dir, "x.json");
        RecordFiles.Read(path, EscalationJsonContext.Default.EscalationAnswer).Should().BeNull("absent");

        File.WriteAllText(path, """{"id":"x","answer":"yes","answeredUtc":"2026-10-02T12:00:00Z"}""");
        RecordFiles.Read(path, EscalationJsonContext.Default.EscalationAnswer)!.Answer.Should().Be("yes");

        File.WriteAllText(path, "{ torn");
        RecordFiles.Read(path, EscalationJsonContext.Default.EscalationAnswer).Should().BeNull("torn is not a record");
    }

    [Fact]
    public void ADeleteThatIsRefused_IsSaid_NamingWhatItWas()
    {
        var path = Path.Combine(_dir, "held.json");
        File.WriteAllText(path, "{}");
        var said = new List<string>();

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var deleted = RecordFiles.Delete(path, said.Add, "question held");

            if (OperatingSystem.IsWindows())
            {
                deleted.Should().BeFalse("Windows refuses to delete a file another handle holds unshared");
                said.Should().ContainSingle().Which.Should().StartWith("question held is past retention and could not be removed: ");
            }
        }

        RecordFiles.Delete(Path.Combine(_dir, "gone.json"), said.Add, "gone").Should().BeTrue("an absent file is already deleted");
    }
}

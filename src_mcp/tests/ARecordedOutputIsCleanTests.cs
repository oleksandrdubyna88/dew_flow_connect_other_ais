using System.Text.RegularExpressions;
using CoaiMcp.Core.Notices;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E4 s4 t4 (R5): the recordings of story s1 under <c>fixtures/toolchain/</c> are real output with the checkout path and the user name replaced. A scan fails for a recording that holds a home folder path, the user name of the box that recorded it, or a line the Redaction pass would change; a companion proves
/// that the scan finds a planted line of each kind.
/// </summary>
public sealed partial class ARecordedOutputIsCleanTests
{
    private static readonly string[] BoxUserNames = ["jinx", "strug", Environment.UserName];

    /// <summary>What is wrong with a recording, one sentence each; empty when it is clean.</summary>
    private static List<string> Problems(string name, string text)
    {
        var problems = new List<string>();
        var number = 0;
        foreach (var line in text.Split('\n'))
        {
            number++;
            if (HomePath().IsMatch(line))
            {
                problems.Add($"{name}:{number} holds a home folder path");
            }

            if (BoxUserNames.Where(user => user.Length >= 3).Any(user => Regex.IsMatch(line, $@"(?<![A-Za-z0-9_]){Regex.Escape(user)}(?![A-Za-z0-9_])", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1))))
            {
                problems.Add($"{name}:{number} holds the user name of the recording box");
            }

            if (!string.Equals(Redaction.SafeSource(line), line, StringComparison.Ordinal))
            {
                problems.Add($"{name}:{number} is a line the Redaction pass would change");
            }
        }

        return problems;
    }

    [Fact]
    public void R5_NoRecording_HoldsAHomePath_TheBoxUserName_OrALineTheRedactionPassWouldChange()
    {
        var found = DigestFixtures.All().SelectMany(recording => Problems(recording.Name, recording.Text)).ToList();

        found.Should().BeEmpty("a recording is committed: nothing of the person or the box may be in it");
        DigestFixtures.All().Should().HaveCountGreaterThan(40);
    }

    [Theory]
    [InlineData("see /home/somebody/project/File.cs for the rest", "home folder path")]
    [InlineData("opened C:\\Users\\somebody\\project\\File.cs", "home folder path")]
    [InlineData("the file is under /Users/somebody/work", "home folder path")]
    [InlineData("ran as jinx in the checkout", "user name of the recording box")]
    [InlineData("the key ghp_0123456789abcdef0123456789abcdef0123 was used", "Redaction pass")]
    public void TheCompanion_TheScanFindsAPlantedLineOfEachKind(string planted, string expected)
    {
        Problems("planted.txt", "a clean line\n" + planted + "\nanother clean line\n").Should().ContainSingle(problem => problem.Contains(expected, StringComparison.Ordinal) && problem.StartsWith("planted.txt:2", StringComparison.Ordinal));
    }

    [Fact]
    public void TheReplacedCheckoutPathOfTheRecordings_IsNotAHomePath()
    {
        Problems("path.txt", "at X() in /home/dev/coai/src_mcp/File.cs:24\n/work/c/coai-coder-m1/File.cs\n").Should().BeEmpty();
    }

    [GeneratedRegex(@"(?:/home/(?!dev/coai(?:/|\b))[^\s/]+/|[A-Za-z]:\\Users\\|/Users/[^\s/]+/|/mnt/[a-z]/Users/)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HomePath();
}

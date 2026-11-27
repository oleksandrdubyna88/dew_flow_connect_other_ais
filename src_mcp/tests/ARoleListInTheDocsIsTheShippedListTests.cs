using System.Text.RegularExpressions;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s8 t5 d1: the list of roles that <c>README.md</c> and <c>ARCHITECTURE.md</c> name after "Today:" and "Roles today:" is <see cref="RoleDescriptors.Shipped"/>, no more and no fewer. A role that ships and is not named in a doc, or a name in a doc that ships no role, is a red test.
/// </summary>
public sealed partial class ARoleListInTheDocsIsTheShippedListTests
{
    [GeneratedRegex("`([a-z]+)`")]
    private static partial Regex Quoted();

    /// <summary>The words in code quotes between the marker and the first <c>).</c> after it: the end of the sentence that lists the roles.</summary>
    private static IReadOnlyList<string> RolesAfter(string file, string marker)
    {
        var text = File.ReadAllText(Path.Combine(RepoPaths.Root(), file));
        var from = text.IndexOf(marker, StringComparison.Ordinal);
        from.Should().BeGreaterThan(-1, $"{file} has the sentence that starts with \"{marker}\"");
        var to = text.IndexOf(").", from, StringComparison.Ordinal);
        to.Should().BeGreaterThan(from);

        return [.. Quoted().Matches(text[from..to]).Select(match => match.Groups[1].Value)];
    }

    [Theory]
    [InlineData("README.md", "Today: `ba`")]
    [InlineData("ARCHITECTURE.md", "Roles today: `ba`")]
    public void TheRolesAFileNames_AreTheRolesThatShip(string file, string marker)
    {
        var named = RolesAfter(file, marker);

        named.Should().BeEquivalentTo(RoleDescriptors.Shipped.Select(descriptor => descriptor.Role), $"{file} names every role of RoleDescriptors.Shipped and no other");
    }

    [Fact]
    public void TheScan_SeesAnExtraAndAMissingRole()
    {
        // The control: the same scan over a sentence with a role too many and one too few gives a list that differs.
        var sentence = "Today: `ba`, `coder`, `scout` and `wizard` (a made-up role).";
        var named = Quoted().Matches(sentence[..sentence.IndexOf(").", StringComparison.Ordinal)]).Select(match => match.Groups[1].Value).ToList();

        named.Should().NotBeEquivalentTo(RoleDescriptors.Shipped.Select(descriptor => descriptor.Role));
    }
}

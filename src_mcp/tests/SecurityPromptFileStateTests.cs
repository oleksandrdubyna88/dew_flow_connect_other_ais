using System.Text;
using System.Text.Json;
using CoaiMcp.Core.Security;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What a security prompt's override FILE counts as when this server reads it — the answer
/// <c>--security-prompt-text</c> prints, which the seam compares with the Security lane tab's own reading
/// (research/PLAN_the_security_tab_reads_at_a_glance.md, epic 2). The shared vectors pin the rule on text; these pin the
/// reading of bytes: the byte-order mark and the encoding it names.
/// </summary>
public sealed class SecurityPromptFileStateTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-security-text-").FullName;

    public void Dispose() => Directory.Delete(_data, true);

    private string StateOf(string id, byte[] bytes)
    {
        var prompts = new RolePrompts(_data);
        Directory.CreateDirectory(Path.GetDirectoryName(prompts.FileToWrite(id))!);
        File.WriteAllBytes(prompts.FileToWrite(id), bytes);
        return SecurityPromptTextReadMode.StateOf(prompts, id);
    }

    private const string Placeholder = "<!-- OPERATOR: write it -->";

    [Fact]
    public void A_file_is_read_through_its_byte_order_mark()
    {
        StateOf("redteam-u16", [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(Placeholder)]).Should().Be("placeholder");
        StateOf("redteam-u16-blank", [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("\r\n")]).Should().Be("blank");
        StateOf("redteam-u8bom", [0xEF, 0xBB, 0xBF]).Should().Be("blank");
        StateOf("redteam-u8", Encoding.UTF8.GetBytes("Review the change.")).Should().Be("written");
    }

    [Fact]
    public void No_file_is_none_and_a_file_over_the_limit_is_oversized_before_it_is_read()
    {
        SecurityPromptTextReadMode.StateOf(new RolePrompts(_data), "redteam-absent").Should().Be("none");
        StateOf("redteam-big", Encoding.UTF8.GetBytes(new string('a', SecurityContext.MaxPromptBytes + 1))).Should().Be("oversized");
    }

    [Fact]
    public void The_lane_caps_are_the_ones_the_shared_catalogue_declares()
    {
        using var catalogue = JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "shared", "security-lane.json"))));
        var limits = catalogue.RootElement.GetProperty("limits");
        SecurityCatalog.MostPrompts.Should().Be(limits.GetProperty("mostPrompts").GetInt32());
        SecurityCatalog.MostRuns.Should().Be(limits.GetProperty("mostRuns").GetInt32());
    }
}

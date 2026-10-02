using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The base prompts a row may name (PLAN_question_consultant.md, §0 item 3): three shipped, one per
/// capability, embedded from <c>shared/question-prompts.json</c>; a person's own beside them; edited through
/// the prompt override layer and restored by deleting the override.
/// </summary>
public sealed class QuestionPromptSetTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-qprompts-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException) { }
    }

    [Fact]
    public void TheShippedThree_AreTheOperatorsExamples_OnePerCapability()
    {
        var shipped = QuestionPromptSet.Shipped.Prompts;

        shipped.Select(p => p.Id).Should().Equal("question-disk", "question-web", "question-opinion");
        shipped.Select(p => p.Capability).Should().Equal(Capability.Disk, Capability.Web, Capability.None);
        shipped.Should().OnlyContain(p => p.Shipped && p.Text.Length > 0 && p.Title.Length > 0);
        shipped[0].Text.Should().Contain("other projects");
        shipped[1].Text.Should().Contain("Search the internet");
        shipped[2].Text.Should().Contain("best developer in the world");
    }

    /// <summary>The embedded copy IS the shared file — the role seed's own contract, so an edit to one side goes red on this side.</summary>
    [Fact]
    public void TheEmbeddedCatalog_IsTheSharedFile()
    {
        using var file = JsonDocument.Parse(File.ReadAllText(SharedFile()));
        var rows = file.RootElement.GetProperty("prompts").EnumerateArray().ToList();

        rows.Should().HaveCount(QuestionPromptSet.Shipped.Prompts.Count);
        foreach (var (row, prompt) in rows.Zip(QuestionPromptSet.Shipped.Prompts))
        {
            row.GetProperty("id").GetString().Should().Be(prompt.Id);
            row.GetProperty("title").GetString().Should().Be(prompt.Title);
            row.GetProperty("capability").GetString().Should().Be(prompt.Capability.Spelled());
            row.GetProperty("text").GetString()!.Trim().Should().Be(prompt.Text);
        }
    }

    [Fact]
    public void AnIdIsFound_WithoutCase_AndAnUnknownOneIsNot()
    {
        QuestionPromptSet.Shipped.Find("Question-Web")!.Capability.Should().Be(Capability.Web);
        QuestionPromptSet.Shipped.Find("question-nothing").Should().BeNull();
        QuestionPromptSet.Shipped.Spelled.Should().Be("question-disk, question-web, question-opinion");
    }

    [Fact]
    public void APersonsOwnPrompt_JoinsTheCatalog_AfterTheShippedOnes()
    {
        var custom = QuestionPromptSet.ParseCustom("""
            [{"id":"ask-the-architect","title":"The architect","capability":"none","text":"You are the team's architect. Answer for the boundaries."}]
            """);

        custom.Complaints.Should().BeEmpty();
        var catalog = QuestionPromptSet.With(custom.Prompts);
        catalog.Prompts.Should().HaveCount(4);
        var own = catalog.Find("ask-the-architect")!;
        own.Shipped.Should().BeFalse();
        own.Capability.Should().Be(Capability.None);
        own.Title.Should().Be("The architect");
    }

    [Theory]
    [InlineData("""[{"id":"Bad Id","title":"x","capability":"none","text":"t"}]""", "not one an override file can be named by")]
    [InlineData("""[{"id":"question-web","title":"x","capability":"web","text":"t"}]""", "already taken")]
    [InlineData("""[{"id":"fine","title":"x","capability":"telepathy","text":"t"}]""", "not one of: none, disk, web")]
    [InlineData("""[{"id":"fine","title":"x","capability":"none","text":"   "}]""", "has no text")]
    public void ACustomPromptThatCannotBeUsed_IsRefusedByName_WithItsCure(string json, string complaint)
    {
        var custom = QuestionPromptSet.ParseCustom(json);

        custom.Prompts.Should().BeEmpty();
        custom.Complaints.Should().ContainSingle().Which.Should().Contain(complaint).And.Contain("COAI_QCONSULT_PROMPTS");
        custom.Unreadable.Should().BeFalse("a refused row is not an unreadable list");
    }

    [Fact]
    public void AValueThatIsNotJson_IsUnreadable_AndTheShippedThreeStillAre()
    {
        var custom = QuestionPromptSet.ParseCustom("[{\"id\":");

        custom.Unreadable.Should().BeTrue();
        custom.Complaints.Should().ContainSingle().Which.Should().Contain("could not be read").And.Contain("the shipped three still are");
        QuestionPromptSet.With(custom.Prompts).Prompts.Should().HaveCount(3);
    }

    [Fact]
    public void ACustomPromptWithoutATitle_IsCalledByItsId()
    {
        QuestionPromptSet.ParseCustom("""[{"id":"terse","capability":"web","text":"search"}]""")
            .Prompts.Single().Title.Should().Be("terse");
    }

    /// <summary>
    /// File-backed through <see cref="RolePrompts"/>: an edit is an override file, restore-default deletes it,
    /// and the text a row then runs is the shipped one — the role-prompt pattern, applied to a prompt with no role.
    /// </summary>
    [Fact]
    public void AShippedPrompt_IsEditedAsAnOverride_AndRestoredByDeletingIt()
    {
        var prompts = new RolePrompts(_data);
        var shipped = QuestionPromptSet.Shipped.Find("question-web")!;

        prompts.Written(shipped.Id).Should().BeEmpty("nothing is overridden on a fresh data directory");
        prompts.Override(shipped.Id, "Search only the vendor's own documentation.");
        prompts.Written(shipped.Id).Should().Be("Search only the vendor's own documentation.");
        File.Exists(Path.Combine(_data, "prompts", "question-web.md")).Should().BeTrue("the override is a file beside the role prompts' overrides");

        prompts.RestoreDefault(shipped.Id);

        prompts.Written(shipped.Id).Should().BeEmpty("restore-default is deleting the override");
        shipped.Text.Should().Contain("Search the internet", "and the shipped text is what a row runs again");
    }

    private static string SharedFile()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here is not null && !File.Exists(Path.Combine(here.FullName, "shared", "question-prompts.json")))
        {
            here = here.Parent;
        }

        here.Should().NotBeNull("shared/question-prompts.json was not found above the test binary");

        return Path.Combine(here!.FullName, "shared", "question-prompts.json");
    }
}

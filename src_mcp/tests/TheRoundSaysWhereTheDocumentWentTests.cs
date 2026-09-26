using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// When a document leaves this machine, the round says so.
/// </summary>
/// <remarks>
/// <para>Plan 4's architecture note read <i>"a document never leaves this machine"</i>, and said in
/// the same breath why that mattered: the tool reads a file and ships its contents to other vendors'
/// models. Plan 5 makes it leave — to the company's own box, on the company's own subscription, and
/// only when somebody ticked for it. Both halves of that are fine and neither is obvious from a
/// round that simply reports three reviewers answering.</para>
/// <para>So the fact is stated where the person asking for the review will see it, once, in the reply
/// they are already reading. Not on a code round: a diff going to a Team server is what a Team server
/// IS, and a clause on every round is a clause nobody reads by the third one — the argument
/// <c>ReviewerSummary.Sentence</c>'s own remarks make about appending to a sentence everybody sees.</para>
/// </remarks>
public sealed class TheRoundSaysWhereTheDocumentWentTests
{
    private const string One = "https://coai.example.com";
    private const string Two = "https://coai.other.example";

    private static PanelService Service(params ProviderSettings[] providers) =>
        new(
            new PanelSettings { DataDir = Path.GetTempPath(), Providers = providers },
            VaultKeys.None("no vault"),
            default,
            new Runners.Processes.ProcessLauncher(),
            Serilog.Core.Logger.None, Noticing.None);

    private static ProviderSettings Remote(string id, string server) =>
        new(id) { Enabled = true, Runtime = "remote", RemoteVendor = "codex", BaseUrl = server, Documents = DocumentReviews.Yes };

    private static ProviderSettings Local(string id = "local") =>
        new(id) { Enabled = true, Runtime = "local", Model = "qwen", Documents = DocumentReviews.Yes };

    [Fact]
    public void ADocumentRoundCarriedByATeamServer_NamesIt()
    {
        var said = Service(Local(), Remote("team-codex", One))
            .Engine.WhereTheDocumentWent(Stage.DocumentReview, ["local", "team-codex"]);

        said.Should().Contain(One).And.Contain("shared subscription");
    }

    /// <summary>
    /// It is a SECOND statement, and it starts on its own line.
    /// </summary>
    /// <remarks>
    /// The clause is appended to the reviewer count, which is a sentence of its own; joined by a
    /// space the two read as one run-on, and a client rendering that field compactly gets a server
    /// URL glued to the last reviewer's name. (gemini, the code round.)
    /// </remarks>
    [Fact]
    public void TheClauseBeginsOnItsOwnLine()
    {
        var said = Service(Remote("team-codex", One))
            .Engine.WhereTheDocumentWent(Stage.DocumentReview, ["team-codex"]);

        said.Should().StartWith(Environment.NewLine);
        said.TrimStart('\r', '\n').Should()
            .StartWith("The document", "one separator, not a separator and a space");
    }

    /// <summary>Two servers are two facts, and one of them being named is the other being hidden.</summary>
    [Fact]
    public void TwoTeamServersAreBothNamed()
    {
        var said = Service(Remote("a-codex", One), Remote("b-claude", Two))
            .Engine.WhereTheDocumentWent(Stage.DocumentReview, ["a-codex", "b-claude"]);

        said.Should().Contain(One).And.Contain(Two);
    }

    /// <summary>Two vendors on ONE server are one server.</summary>
    [Fact]
    public void TwoVendorsOnOneServerNameItOnce()
    {
        var said = Service(Remote("team-codex", One), Remote("team-claude", One))
            .Engine.WhereTheDocumentWent(Stage.DocumentReview, ["team-codex", "team-claude"]);

        said.Split(One).Should().HaveCount(2, "the server appears exactly once");
    }

    [Fact]
    public void ADocumentRoundThatStayedHere_SaysNothing()
    {
        var said = Service(Local()).Engine.WhereTheDocumentWent(Stage.DocumentReview, ["local"]);

        said.Should().BeEmpty("a round that told the truth about nothing new has nothing to add");
    }

    /// <summary>
    /// A vendor that was CONFIGURED but carried nothing is not named as having received the document.
    /// </summary>
    /// <remarks>
    /// This is the shape of the mistake: the settings are the easy list to read and the wrong one. A
    /// Team server can be configured, ticked, and still carry nothing — no credential, a role it does
    /// not run, a round whose deal fell elsewhere — and telling somebody their document went to a box
    /// it never reached is worse than saying nothing, because it is the one claim here they cannot
    /// check.
    /// </remarks>
    [Fact]
    public void AConfiguredServerThatCarriedNothing_IsNotNamed()
    {
        var said = Service(Local(), Remote("team-codex", One))
            .Engine.WhereTheDocumentWent(Stage.DocumentReview, ["local"]);

        said.Should().BeEmpty("only the vendors that were actually given work received anything");
    }

    [Theory]
    [InlineData(Stage.CodeReview)]
    [InlineData(Stage.PlanReview)]
    public void NoOtherStageSaysAnything(Stage stage)
    {
        var said = Service(Remote("team-codex", One)).Engine.WhereTheDocumentWent(stage, ["team-codex"]);

        said.Should().BeEmpty("a diff going to a Team server is what a Team server is for");
    }
}

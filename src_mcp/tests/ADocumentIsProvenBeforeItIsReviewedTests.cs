using System.Text;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What <c>review_document</c> refuses, and what it says when it does.
/// </summary>
/// <remarks>
/// <para><b>The confinement rule is a security rule.</b> Two vendors refused the plan's first draft
/// over it independently: a tool that reads any path on the disk and ships its contents to three
/// other vendors' APIs is an exfiltration primitive with a friendly name, and an AI that has been
/// prompt-injected — or has simply misread an instruction — is all it takes.</para>
/// <para>Every refusal here is an INSTRUCTION rather than a diagnosis. The calling AI is the thing
/// that has to act on it, and "binary file refused" makes it retry with the same bytes until the
/// session stalls.</para>
/// </remarks>
public sealed class ADocumentIsProvenBeforeItIsReviewedTests : IDisposable
{
    private readonly string _repo = Directory.CreateTempSubdirectory("coai-doc-repo-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_repo, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string Write(string relative, string text)
    {
        var path = Path.Combine(_repo, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>No link resolution at all — the identity function, for the ordinary case.</summary>
    private DocumentOutcome Read(DocumentRequest request, Func<string, string>? followLink = null) =>
        DocumentReader.Read(_repo, request, followLink ?? (p => p));

    private static string Refusal(DocumentOutcome outcome) =>
        outcome.Should().BeOfType<DocumentOutcome.Refused>().Subject.Sentence;

    private static DocumentOutcome.Ready Ready(DocumentOutcome outcome) =>
        outcome.Should().BeOfType<DocumentOutcome.Ready>().Subject;

    private const string Body = "# The spec\n\nIt must be fast.\n";

    // ---- which argument ----

    /// <summary>
    /// Two different mistakes, two different sentences. "You sent both" and "you sent neither" send
    /// a caller to opposite fixes, and one message for both sends half of them the wrong way.
    /// </summary>
    [Fact]
    public void BothOrNeither_AreDifferentRefusals()
    {
        var both = Refusal(Read(new DocumentRequest(Path: Write("spec.md", Body), Text: Body, Name: "Spec")));
        var neither = Refusal(Read(new DocumentRequest()));

        both.Should().NotBe(neither);
        both.Should().Contain("documentPath").And.Contain("documentText");
        neither.Should().Contain("documentPath").And.Contain("documentText");
    }

    /// <summary>
    /// Raw text has no identity of its own, and without one round 2 of an edited document cannot
    /// find round 1 — which is the whole finding the plan round turned on.
    /// </summary>
    [Fact]
    public void RawText_NeedsAName() =>
        Refusal(Read(new DocumentRequest(Text: Body))).Should().Contain("documentName");

    [Theory]
    [InlineData("my-spec")]
    [InlineData("2spec")]
    [InlineData("a spec")]
    [InlineData("../etc/passwd")]
    public void ANameThatIsNotAName_IsRefused(string name) =>
        Refusal(Read(new DocumentRequest(Text: Body, Name: name))).Should().Contain(name);

    [Fact]
    public void ANameAtTheBound_IsAName() =>
        Ready(Read(new DocumentRequest(Text: Body, Name: new string('R', DocumentRules.MaxNameLength))))
            .Should().NotBeNull();

    [Fact]
    public void ANameOverTheBound_IsRefused() =>
        Refusal(Read(new DocumentRequest(Text: Body, Name: new string('R', DocumentRules.MaxNameLength + 1))))
            .Should().Contain(DocumentRules.MaxNameLength.ToString());

    /// <summary>A path already names the document; a second name would be a second identity.</summary>
    [Fact]
    public void APathWithAName_IsRefused() =>
        Refusal(Read(new DocumentRequest(Path: Write("spec.md", Body), Name: "Spec")))
            .Should().Contain("documentName");

    // ---- confinement ----

    [Fact]
    public void APathInsideTheRepository_IsRead()
    {
        var ready = Ready(Read(new DocumentRequest(Path: Write("docs/spec.md", Body))));

        ready.Text.Should().Be(Body);
        ready.Id.Should().Be("docs/spec.md");
        ready.Name.Should().Be("spec.md");
    }

    [Fact]
    public void APathOutsideTheRepository_IsRefused()
    {
        var outside = Path.Combine(Path.GetTempPath(), "coai-not-in-the-repo.md");
        File.WriteAllText(outside, Body);

        try
        {
            Refusal(Read(new DocumentRequest(Path: outside)))
                .Should().Contain("inside").And.Contain("documentText");
        }
        finally
        {
            File.Delete(outside);
        }
    }

    /// <summary>
    /// A SYMLINK inside the repository pointing outside it is the case a prefix check misses, and
    /// the one an attacker would reach for. The resolver is injected so the rule is a unit test
    /// rather than a privilege the CI runner may not have.
    /// </summary>
    [Fact]
    public void ASymlinkEscapingTheRepository_IsRefused()
    {
        var link = Write("docs/innocent.md", Body);
        var elsewhere = Path.Combine(Path.GetTempPath(), "secrets.env");

        Refusal(Read(new DocumentRequest(Path: link), followLink: _ => elsewhere))
            .Should().Contain("inside");
    }

    /// <summary>
    /// The separator test, not a prefix test: <c>/repo-secrets</c> starts with <c>/repo</c> and is
    /// not in it.
    /// </summary>
    [Fact]
    public void ASiblingDirectoryWithTheSamePrefix_IsNotInside()
    {
        DocumentId.Of("/repo", "/repo-secrets/x.md").Should().BeEmpty();
        DocumentId.Of("/repo", "/repo/x.md").Should().Be("x.md");
    }

    [Fact]
    public void AMissingPath_IsRefusedByName()
    {
        var missing = Path.Combine(_repo, "nothing-here.md");

        Refusal(Read(new DocumentRequest(Path: missing))).Should().Contain("nothing-here.md");
    }

    [Fact]
    public void ADirectory_IsRefused()
    {
        Directory.CreateDirectory(Path.Combine(_repo, "docs"));

        Refusal(Read(new DocumentRequest(Path: Path.Combine(_repo, "docs"))))
            .Should().Contain("directory");
    }

    // ---- text, proven rather than guessed at ----

    /// <summary>
    /// Refused BEFORE a byte is read, because naming the format is what makes the refusal
    /// actionable: an AI told "not text" retries, and an AI told "spec.docx is not text, convert it"
    /// converts.
    /// </summary>
    [Theory]
    [InlineData("spec.docx")]
    [InlineData("spec.pdf")]
    [InlineData("sheet.xlsx")]
    [InlineData("deck.pptx")]
    [InlineData("shot.png")]
    public void AKnownBinaryExtension_IsRefusedByName(string name)
    {
        var path = Write(name, Body);

        Refusal(Read(new DocumentRequest(Path: path)))
            .Should().Contain(name).And.Contain("documentText");
    }

    /// <summary>
    /// A whole-payload decode, not a NUL byte in a prefix. UTF-16 has NULs in alternate positions
    /// and a binary file with none in the first bytes decodes to mojibake that three reviewers then
    /// read in earnest. (codex and gemini, the plan round, separately.)
    /// </summary>
    [Fact]
    public void BytesThatAreNotUtf8_AreRefused()
    {
        var path = Path.Combine(_repo, "spec.md");
        File.WriteAllBytes(path, [.. Encoding.UTF8.GetBytes("# fine so far\n"), 0xC3, 0x28, 0xFF, 0xFE]);

        Refusal(Read(new DocumentRequest(Path: path))).Should().Contain("text");
    }

    [Fact]
    public void ValidUtf8BeyondAscii_IsText() =>
        Ready(Read(new DocumentRequest(Path: Write("spec.md", "# Требования\n\nБыстро.\n"))))
            .Text.Should().Contain("Требования");

    // ---- bounds ----

    [Fact]
    public void OverTheBound_IsRefusedWithBothNumbers()
    {
        var big = new string('x', DocumentRules.MaxBytes + 1);

        Refusal(Read(new DocumentRequest(Text: big, Name: "Big")))
            .Should().Contain((DocumentRules.MaxBytes + 1).ToString("N0"))
            .And.Contain(DocumentRules.MaxBytes.ToString("N0"));
    }

    [Fact]
    public void AtTheBound_IsAccepted() =>
        Ready(Read(new DocumentRequest(Text: new string('x', DocumentRules.MaxBytes), Name: "Big")))
            .Should().NotBeNull();

    // ---- the artifact id ----

    /// <summary>
    /// The content hash is what identifies the SNAPSHOT, not the session — which is exactly the
    /// distinction the plan round forced.
    /// </summary>
    [Fact]
    public void TheArtifactId_IsTheContent()
    {
        var one = Ready(Read(new DocumentRequest(Text: Body, Name: "Spec")));
        var same = Ready(Read(new DocumentRequest(Text: Body, Name: "Other")));
        var changed = Ready(Read(new DocumentRequest(Text: Body + "\n", Name: "Spec")));

        same.ArtifactId.Should().Be(one.ArtifactId, "the text decides, not the name");
        changed.ArtifactId.Should().NotBe(one.ArtifactId);
        one.ArtifactId.Should().MatchRegex("^[0-9a-f]{16}$");
    }

    /// <summary>Raw text's identity is the name the caller gave it, and it survives every edit.</summary>
    [Fact]
    public void RawTexts_IdentityIsItsName()
    {
        var first = Ready(Read(new DocumentRequest(Text: Body, Name: "Spec")));
        var edited = Ready(Read(new DocumentRequest(Text: $"{Body}\nAnd cheap.\n", Name: "Spec")));

        edited.Id.Should().Be(first.Id);
        edited.ArtifactId.Should().NotBe(first.ArtifactId, "the snapshot changed; the document did not");
    }
}

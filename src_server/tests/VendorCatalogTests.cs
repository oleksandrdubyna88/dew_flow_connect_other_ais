using System.Text;
using CoaiServer;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// <c>vendors.json</c> is the only thing between a caller and an arbitrary model on somebody else's
/// subscription, and it is edited by hand on a live box. These are the states that edit can be in.
/// </summary>
public sealed class VendorCatalogTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "coai-catalog-" + Guid.NewGuid().ToString("N"));

    public VendorCatalogTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Path_ => System.IO.Path.Combine(_dir, "vendors.json");

    private void WriteFile(string json) => File.WriteAllText(Path_, json, Encoding.UTF8);

    private const string Good = """
        [{ "id": "codex", "runtime": "codex", "models": ["gpt-5.6-luna"], "slots": ["a"] }]
        """;

    [Fact]
    public void AGoodFileLoads()
    {
        WriteFile(Good);

        var catalog = new VendorCatalogHost(_dir).Current;

        catalog.Error.Should().BeEmpty();
        catalog.Vendors.Should().ContainSingle();
        catalog.Allows("codex", "gpt-5.6-luna").Should().BeTrue();
        catalog.Allows("codex", "gpt-5.6-sol").Should().BeFalse("only the listed models are paid for");
    }

    [Fact]
    public void AMissingFileIsAnEmptyCatalogAndNotAnError()
    {
        var catalog = new VendorCatalogHost(_dir).Current;

        catalog.Vendors.Should().BeEmpty();
        catalog.Error.Should().BeEmpty("a fresh install has no vendors yet — that is not a fault");
    }

    [Theory]
    [InlineData("""[{ "id": "x", "runtime": "nope", "models": ["m"], "slots": ["a"] }]""", "nope")]
    [InlineData("""[{ "id": "x", "runtime": "codex", "models": [], "slots": ["a"] }]""", "models")]
    [InlineData("""[{ "id": "x", "runtime": "codex", "models": ["m"], "slots": [] }]""", "slots")]
    [InlineData("""[{ "id": "x", "runtime": "codex", "models": ["m"], "slots": ["../etc"] }]""", "../etc")]
    [InlineData("""[{ "id": "x", "runtime": "local", "models": ["m"], "slots": ["a"] }]""", "local")]
    // An id becomes a directory under <DataDir>/accounts, so an id that can climb out of it is the
    // same defect as a slot name that can — and only the slot name was checked at first.
    [InlineData("""[{ "id": "../shared", "runtime": "codex", "models": ["m"], "slots": ["a"] }]""", "../shared")]
    [InlineData("""[{ "id": "a/b", "runtime": "codex", "models": ["m"], "slots": ["a"] }]""", "a/b")]
    public void EachBadEntryIsRefusedByName(string json, string mentioned)
    {
        WriteFile(json);

        var catalog = new VendorCatalogHost(_dir).Current;

        catalog.Error.Should().Contain(mentioned);
        catalog.Vendors.Should().BeEmpty();
    }

    [Fact]
    public void ADuplicateIdIsRefused()
    {
        WriteFile("""
            [{ "id": "codex", "runtime": "codex", "models": ["m"], "slots": ["a"] },
             { "id": "CODEX", "runtime": "claude", "models": ["m"], "slots": ["a"] }]
            """);

        new VendorCatalogHost(_dir).Current.Error.Should()
            .Contain("appears 2 times", "an id is how a client names a vendor, so two of them is ambiguous");
    }

    [Fact]
    public void ABadEditKeepsTheVendorsThatWereAlreadyServing()
    {
        WriteFile(Good);
        var host = new VendorCatalogHost(_dir);
        host.Current.Vendors.Should().ContainSingle();

        WriteFile("""[{ "id": "codex", "runtime": "typo", "models": ["m"], "slots": ["a"] }]""");

        var after = host.Current;
        after.Vendors.Should().ContainSingle("an empty allowlist refuses every review, which looks like an outage");
        after.Error.Should().Contain("typo", "and the disagreement between disk and behaviour must be visible");
    }

    [Fact]
    public void UnparseableJsonIsRefusedWithoutTakingTheServerDown()
    {
        WriteFile(Good);
        var host = new VendorCatalogHost(_dir);
        host.Current.Vendors.Should().ContainSingle();

        WriteFile("{ not json at all");

        host.Current.Error.Should().Contain("not valid JSON");
        host.Current.Vendors.Should().ContainSingle();
    }

    [Fact]
    public void AnEditOfTheSameLengthIsStillNoticed()
    {
        // The reason the reload token is a content hash rather than a timestamp: a replacement of
        // the same size can land inside the filesystem's timestamp resolution, and a metadata stamp
        // would then keep serving the old allowlist while promising hot reload.
        WriteFile("""[{ "id": "aaaaa", "runtime": "codex", "models": ["m"], "slots": ["a"] }]""");
        var host = new VendorCatalogHost(_dir);
        host.Current.Vendors.Single().Id.Should().Be("aaaaa");

        WriteFile("""[{ "id": "bbbbb", "runtime": "codex", "models": ["m"], "slots": ["a"] }]""");

        host.Current.Vendors.Single().Id.Should().Be("bbbbb");
    }

    [Fact]
    public void AnUnchangedFileIsNotReparsed()
    {
        WriteFile(Good);
        var host = new VendorCatalogHost(_dir);

        var first = host.Current;

        host.Current.Should().BeSameAs(first, "re-reading a file that did not move is work for nothing");
    }
}

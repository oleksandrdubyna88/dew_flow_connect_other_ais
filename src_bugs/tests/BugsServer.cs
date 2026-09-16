using Microsoft.AspNetCore.Mvc.Testing;

namespace CoaiBugs.Tests;

/// <summary>
/// Hosts the real ingest server in-process on a throwaway data directory.
/// </summary>
/// <remarks>
/// <para>Copied from `CoaiServer.Tests.TeamServer` with its lesson: configuration goes through
/// PROCESS ENVIRONMENT VARIABLES rather than `WithWebHostBuilder`, because `Program` reads the
/// environment before `Build()` and anything a factory adds during `ConfigureWebHost` lands too
/// late. The consequence is that the variables are process-global, so these tests do not run in
/// parallel with each other.</para>
/// <para><b>Why an HTTP test at all, when the decisions are already unit-tested.</b> `Ingest.Take` is
/// pure and covered; what it cannot see is the route — the bearer header, the 401, the batch cap, and
/// above all the AOT JSON binding. This repository has a contract suite precisely because an AOT
/// binding failure once made a released `coai-server` answer 500 to everything, and `BugsJson` is new
/// code of exactly that kind.</para>
/// </remarks>
internal sealed class BugsServer : WebApplicationFactory<Program>
{
    /// <summary>The secret keys are hashed with. Any value; it only has to be the same one.</summary>
    public const string Secret = "coai-bugs-test-secret-not-a-real-one";

    private readonly Dictionary<string, string?> _restore = [];

    public string DataDir { get; }

    public BugsServer()
    {
        DataDir = Path.Combine(Path.GetTempPath(), "coai-bugs-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DataDir);

        Set("COAI_BUGS_SECRET", Secret);
        Set("COAI_BUGS_DATA", DataDir);
    }

    /// <summary>A key this server will accept, and the id it was issued under.</summary>
    /// <remarks>
    /// Issued through the same `Corpus` the server reads, rather than by inserting a row by hand: a
    /// fixture that wrote its own hash would pass while the real hashing was wrong.
    /// </remarks>
    public (string Key, string Id) IssueKey(string note = "a test")
    {
        using var corpus = Corpus.Open(Path.Combine(DataDir, "coai-bugs.db"));
        var key = "test-key-" + Guid.NewGuid().ToString("N");
        var id = Guid.NewGuid().ToString("N")[..16];
        corpus.Issue(id, Corpus.HashOf(key, Secret), note, DateTime.UtcNow.ToString("O"));

        return (key, id);
    }

    /// <summary>What the corpus holds right now, read straight from the file.</summary>
    public Corpus Reading() => Corpus.Open(Path.Combine(DataDir, "coai-bugs.db"));

    private void Set(string name, string? value)
    {
        _restore[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
        }

        foreach (var (name, was) in _restore)
        {
            Environment.SetEnvironmentVariable(name, was);
        }

        Scratch.Delete(DataDir);
    }
}

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

    /// <summary>
    /// Every line the running server logged, so a test can assert on what it SAID.
    /// </summary>
    /// <remarks>
    /// <para>The edge warning has no other observable: it changes no status code, no body and no
    /// row — deliberately, because refusing the request would break a deployment over a header the
    /// contributor did not send. The log IS the behaviour, so the log is what a test at the right
    /// layer has to read.</para>
    /// <para>The server clears its own providers and installs Serilog before this fixture gets a
    /// say, so the capture is added in <c>ConfigureWebHost</c> — which runs after that and before
    /// the host is built, and is therefore the one thing a factory CAN inject into this server.</para>
    /// </remarks>
    public IReadOnlyList<string> Said
    {
        get
        {
            lock (_said)
            {
                return [.. _said.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)];
            }
        }
    }

    private readonly StringWriter _said = new();

    /// <summary>
    /// Adds a logging provider AFTER the server has cleared its own and installed Serilog.
    /// </summary>
    /// <remarks>
    /// The class note above says configuration must go through process environment variables because
    /// `Program` reads them before `Build()`. Logging providers are the exception and for the
    /// opposite reason: they are resolved when the host is built, which is after this runs, so a
    /// provider added here survives the `ClearProviders()` the server does on its way there.
    /// </remarks>
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureLogging(logging => logging.AddProvider(new Capture(_said)));

    /// <summary>A provider that keeps every line, because the log IS the behaviour under test.</summary>
    private sealed class Capture(StringWriter said) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Line(said);

        public void Dispose()
        {
        }

        private sealed class Line(StringWriter said) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var line = formatter(state, exception);
                lock (said)
                {
                    said.WriteLine(line);
                }
            }
        }
    }

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

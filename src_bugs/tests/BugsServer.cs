using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
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

    /// <summary>
    /// The clock the server reads — frozen, so a test can sit either side of a month boundary and
    /// walk a rate-limit window forward by the second.
    /// </summary>
    /// <remarks>
    /// The one thing a factory CAN hand this server: `TimeProvider` is a service, resolved when the
    /// host is built, so a registration added in <see cref="ConfigureWebHost"/> replaces the
    /// system clock the server registers first. Everything else is environment variables.
    /// </remarks>
    public FrozenClock Clock { get; } = new(new DateTimeOffset(2026, 9, 17, 10, 0, 0, TimeSpan.Zero));

    /// <param name="ratePerMinute">
    /// The limit to serve with; unset means the server's default. Set explicitly to nothing
    /// otherwise, so a variable in the machine's environment cannot leak into a test.
    /// </param>
    /// <param name="adminKeys">
    /// The administrator list, newline-separated as the operator writes it — this fixture ENCODES it
    /// the way the deploy does, so a test never has to think about the wire. **Null means the
    /// variable is ABSENT**, which is a legitimate way to run this server: every `/admin/*` call then
    /// answers 401, indistinguishably from a wrong credential, and a test asserts exactly that.
    /// </param>
    /// <param name="adminRatePerMinute">
    /// The ADMIN limit, which is its own setting. Unset means the server's default of 120; the
    /// contributor number would make a paging test unrunnable, which is why they are separate.
    /// </param>
    public BugsServer(int? ratePerMinute = null, string? adminKeys = null, int? adminRatePerMinute = null)
    {
        DataDir = Path.Combine(Path.GetTempPath(), "coai-bugs-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DataDir);

        Set("COAI_BUGS_SECRET", Secret);
        Set("COAI_BUGS_DATA", DataDir);
        Set(RatePerMinute.Variable, ratePerMinute?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        // ENCODED, because that is what a host carries: the list is newline-separated and an
        // EnvironmentFile assignment cannot hold a newline. A fixture setting the raw text would
        // test a shape no deployment produces.
        Set(AdminKeys.Variable, Delivery.AdminKeys(adminKeys));
        Set(
            RatePerMinute.Surface.Administrator.Variable,
            adminRatePerMinute?.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Every <c>/admin</c> route the running server actually serves, with its method.
    /// </summary>
    /// <remarks>
    /// <para>Read from the host's own <see cref="EndpointDataSource"/>, never retyped. A test that
    /// repeats a list the code also holds will not notice the third entry, and that is exactly what
    /// happened: the gate's checks named the three GET routes and silently left both POSTs — one of
    /// them the issuance — out of every shared assertion. (Code round, codex.) Derived like this, a
    /// sixth admin route is covered by those assertions on the day it is added.</para>
    /// <para>A <c>{id}</c> segment is filled with a syntactically valid id, because the assertions
    /// these feed are about the GATE: a request must be refused before routing ever looks at the
    /// parameter, and an id that could not exist would test the same thing by accident.</para>
    /// </remarks>
    public IReadOnlyList<(string Method, string Path)> AdminRoutes()
    {
        var endpoints = Services.GetRequiredService<EndpointDataSource>().Endpoints;
        var admin = endpoints
            .OfType<RouteEndpoint>()
            .Where(route => route.RoutePattern.RawText?.StartsWith("/admin", StringComparison.Ordinal) == true)
            .SelectMany(route => Methods(route).Select(method =>
                (method, route.RoutePattern.RawText!.Replace("{id}", "0123456789abcdef", StringComparison.Ordinal))))
            .Distinct()
            .OrderBy(route => route.Item2, StringComparer.Ordinal)
            .ThenBy(route => route.method, StringComparer.Ordinal)
            .ToList();

        admin.Should().HaveCountGreaterThan(
            4, "this server maps five admin routes; a catalogue that found fewer is reading the wrong thing");

        return admin;
    }

    private static IEnumerable<string> Methods(RouteEndpoint route) =>
        route.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"];

    /// <summary>An HTTP client presenting a bearer credential.</summary>
    public HttpClient Bearing(string key)
    {
        var http = CreateClient();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);

        return http;
    }

    /// <summary>A key this server will accept, and the id it was issued under.</summary>
    /// <remarks>
    /// Issued through the same `Corpus` the server reads, rather than by inserting a row by hand: a
    /// fixture that wrote its own hash would pass while the real hashing was wrong. Audited as the
    /// CLI would audit it, on the frozen clock, so a test can read the audit back exactly.
    /// </remarks>
    public (string Key, KeyId Id) IssueKey(string note = "a test")
    {
        using var corpus = Corpus.Open(Path.Combine(DataDir, "coai-bugs.db"));
        var key = "test-key-" + Guid.NewGuid().ToString("N");
        var id = new KeyId(Guid.NewGuid().ToString("N")[..16]);
        corpus.Issue(id, Corpus.HashOf(key, Secret), note, Audit.By(AdminId.Cli, Clock));

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
    /// Adds a logging provider and the frozen clock AFTER the server has registered its own.
    /// </summary>
    /// <remarks>
    /// The class note above says configuration must go through process environment variables because
    /// `Program` reads them before `Build()`. Services are the exception and for the opposite
    /// reason: they are resolved when the host is built, which is after this runs, so a provider
    /// added here survives the `ClearProviders()` the server does on its way there, and a
    /// `TimeProvider` registered here is the one the server's limiter and month stamp resolve.
    /// </remarks>
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder
            .ConfigureLogging(logging => logging.AddProvider(new Capture(_said)))
            .ConfigureServices(services => services.AddSingleton<TimeProvider>(Clock));

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

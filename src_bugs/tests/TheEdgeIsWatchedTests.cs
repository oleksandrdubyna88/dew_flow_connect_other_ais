using System.Net.Http.Headers;
using CoaiMcp.Core.Collecting;
using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// The server notices when its edge is misconfigured, instead of trusting a document.
/// </summary>
/// <remarks>
/// <para><b>The plan round's strongest theme, from two providers.</b> Everything that keeps a
/// contributor's address out of this system lives in `deploy/nginx/coai-bugs`: `access_log off`, the
/// forwarding headers cleared. A reviewer put it plainly — the binary has no way to verify the
/// environment it runs in matches the contract it promises, so an operator who copies a stale vhost,
/// or whose location block inherits a distro `proxy_params`, gets a server that records everybody and
/// says nothing.</para>
/// <para>One reviewer proposed a mode that reads the nginx file and checks it. That is the wrong
/// instrument: this process cannot know where that file is, `include` and templating mean the file on
/// disk is not the effective config, and a check that passes on a file nginx never loaded is worse
/// than no check at all. <b>The request itself is the evidence.</b></para>
/// <para><b>These run through the REAL SERVER, and the code round is why.</b> The first version
/// called <c>Program.EdgeSentAnAddress</c> directly, and a reviewer applied the repository's own test
/// rule to it: delete the production line whose absence a user would notice and watch THAT go red.
/// Deleting the whole warning left every one of those tests green, because they never reached it. A
/// test at the wrong layer is a test of the wrong thing.</para>
/// </remarks>
[Collection("the-server")]
public sealed class TheEdgeIsWatchedTests
{
    /// <summary>
    /// Every header the production catalog holds, read FROM the catalog.
    /// </summary>
    /// <remarks>
    /// A list retyped in a test is a list that will not notice its sixth entry — the code round said
    /// so, and it was right: the first version spelled all five out again.
    /// </remarks>
    public static TheoryData<string> EveryForwardingHeader()
    {
        var data = new TheoryData<string>();
        foreach (var header in Program.Forwarding)
        {
            data.Add(header);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryForwardingHeader))]
    public async Task AForwardingHeaderIsNoticed(string header)
    {
        using var server = new BugsServer();
        using var http = server.CreateClient();
        http.DefaultRequestHeaders.TryAddWithoutValidation(header, "203.0.113.7");

        await http.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        server.Said.Should().ContainMatch(
            $"*the edge sent*{header}*",
            "the edge is sending an address, whatever the vhost on disk says");
    }

    /// <summary>The name is the report. The value is the thing being protected.</summary>
    [Fact]
    public async Task TheAddressItselfIsNeverPartOfTheReport()
    {
        using var server = new BugsServer();
        using var http = server.CreateClient();
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.7");

        await http.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        server.Said.Should().NotContainMatch("*203.0.113.7*", "a warning quoting the address IS the leak");
    }

    /// <summary>
    /// It watches EVERY route, not the one it was first written inside.
    /// </summary>
    /// <remarks>
    /// The check began in the `/ingest` handler, and two reviewers made the same point: a header
    /// arriving at `/health`, at an unknown path, or at any endpoint added later would never be seen.
    /// `/health` is the cheapest proof that it moved to the boundary — it is a different route, and
    /// it needs no key.
    /// </remarks>
    [Fact]
    public async Task EvenARouteThatNeedsNoKeyIsWatched()
    {
        using var server = new BugsServer();
        using var http = server.CreateClient();
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-Real-IP", "203.0.113.7");

        var reply = await http.GetAsync(
            new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        reply.IsSuccessStatusCode.Should().BeTrue("the warning does not refuse the request");
        server.Said.Should().ContainMatch("*the edge sent*X-Real-IP*");
    }

    /// <summary>A header spelled as the wire spells it is reported as the catalog spells it.</summary>
    /// <remarks>
    /// HTTP header names are case-insensitive, so a proxy may send `x-real-ip`. Reporting it verbatim
    /// would make the warning fail to match the name in the vhost and in these tests.
    /// </remarks>
    [Fact]
    public async Task ALowercasedHeaderIsReportedByTheCatalogsName()
    {
        using var server = new BugsServer();
        using var http = server.CreateClient();
        http.DefaultRequestHeaders.TryAddWithoutValidation("x-real-ip", "203.0.113.7");

        await http.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        server.Said.Should().ContainMatch("*X-Real-IP*");
    }

    /// <summary>An ordinary request says nothing, so the warning means something when it appears.</summary>
    [Fact]
    public async Task ARequestFromACorrectEdgeIsSilent()
    {
        using var server = new BugsServer();
        using var http = server.CreateClient();
        var (key, _) = server.IssueKey();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        await http.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        server.Said.Should().NotContainMatch("*the edge sent*");
    }

    /// <summary>
    /// The vhost clears every header the server watches for, and nothing holds those two together
    /// except this.
    /// </summary>
    /// <remarks>
    /// <para>The boundary has two halves in two languages: a C# catalog and an nginx file. A reviewer
    /// pointed out that adding a sixth header to one leaves the other silently wrong in whichever
    /// direction was forgotten — nginx forwarding a header the server cannot report, or a server
    /// watching for one nginx never clears.</para>
    /// <para><b>It asserts the VALUE, not just the directive name, and that distinction is the whole
    /// test.</b> The first version looked for <c>proxy_set_header X-Real-IP</c>, which is a substring
    /// of <c>proxy_set_header X-Real-IP $remote_addr;</c> — the one spelling this file exists to
    /// forbid. It would have passed on a vhost that forwarded every contributor's address. The
    /// matcher is <see cref="ClearsHeader"/> so that the two spellings can be held against it
    /// directly, rather than by editing the shipped vhost to see a test go red. (CodeRabbit, #328.)
    /// </para>
    /// </remarks>
    [Fact]
    public void TheVhostClearsEveryHeaderThisServerWatchesFor()
    {
        var vhost = File.ReadAllText(Path.Combine(Repository(), "deploy", "nginx", "coai-bugs"));

        foreach (var header in Program.Forwarding)
        {
            ClearsHeader(vhost, header).Should().BeTrue(
                "the server warns about {0}, so the vhost this ships with must clear it — "
                + "`proxy_set_header {0} \"\";` and nothing else", header);
        }
    }

    /// <summary>
    /// The matcher itself, held against both spellings, so its teeth are permanent.
    /// </summary>
    /// <remarks>
    /// A test that reads a file can only be proven to have teeth by breaking that file, and the file
    /// here is the one carrying this server's no-client-address promise — not something to weaken on
    /// disk, even briefly, to watch an assertion fire. So the condition is a function and the
    /// dangerous spelling is an INPUT.
    /// </remarks>
    [Fact]
    public void ClearingAHeaderIsNotTheSameAsMentioningIt()
    {
        ClearsHeader("proxy_set_header X-Real-IP        \"\";", "X-Real-IP").Should().BeTrue();

        ClearsHeader("proxy_set_header X-Real-IP        $remote_addr;", "X-Real-IP").Should().BeFalse(
            "that directive forwards the address, which is the one thing the vhost must not do");
        ClearsHeader("proxy_set_header X-Real-IP $proxy_add_x_forwarded_for;", "X-Real-IP")
            .Should().BeFalse();
        ClearsHeader("# proxy_set_header X-Real-IP \"\";", "X-Real-IP").Should().BeFalse(
            "a commented-out directive clears nothing");
        ClearsHeader("proxy_set_header X-Forwarded-For \"\";", "X-Real-IP").Should().BeFalse(
            "a different header being cleared says nothing about this one");

        // nginx takes the LAST directive for a header. A check that stopped at the first match
        // would call this file safe while the server forwarded every address.
        ClearsHeader(
            "proxy_set_header X-Real-IP \"\";\nproxy_set_header X-Real-IP $remote_addr;",
            "X-Real-IP").Should().BeFalse("the last directive is the one nginx applies");
        ClearsHeader(
            "proxy_set_header X-Real-IP $remote_addr;\nproxy_set_header X-Real-IP \"\";",
            "X-Real-IP").Should().BeTrue("and the last one here clears it");
    }

    /// <summary>Does this vhost set <paramref name="header"/> to the empty string, and only that?</summary>
    /// <remarks>
    /// The LAST directive for a header wins, which is nginx's rule and not an implementation
    /// detail: a first version took the first match, so a file that cleared the header and then set
    /// it to <c>$remote_addr</c> further down would have been called safe while nginx forwarded the
    /// address.
    /// </remarks>
    private static bool ClearsHeader(string vhost, string header)
    {
        var prefix = $"proxy_set_header {header}";
        var effective = vhost.Split('\n')
            .Select(line => line.Trim())
            .Where(directive => directive.StartsWith(prefix, StringComparison.Ordinal))
            .Select(directive => directive[prefix.Length..].Trim())
            .LastOrDefault();

        return IsEmpty(effective);
    }

    /// <summary>An nginx value that is the empty string, in either quoting.</summary>
    private static bool IsEmpty(string? value) => value is "\"\";" or "'';";

    /// <summary>And it writes no log that could hold an address.</summary>
    /// <remarks>
    /// `limit_req` emits `client: &lt;address&gt;` at ERROR level, so `error_log … warn` keeps a dated
    /// list of exactly the contributors the server throttled. The plan round caught it before the
    /// first deployment; this is what keeps it caught.
    /// </remarks>
    [Fact]
    public void TheVhostWritesNoLogThatCouldHoldAnAddress()
    {
        var vhost = File.ReadAllText(Path.Combine(Repository(), "deploy", "nginx", "coai-bugs"));

        vhost.Should().Contain("access_log off;");
        vhost.Should().Contain(
            "error_log /dev/null crit;",
            "limit_req writes the client address at ERROR level, so any real destination is a list");
    }

    private static string Repository()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "deploy", "nginx")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("no deploy/nginx above the test binary");
    }
}

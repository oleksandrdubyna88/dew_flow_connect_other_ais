using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The prompt is bounded HERE, by this server, and not only by whoever sent it.
/// </summary>
/// <remarks>
/// <para><see cref="AcceptedRoles"/> already says the rule this exists to keep: <i>"the client applies
/// the same rules so it never offers an id this server would refuse, but a client is not a boundary —
/// anything reaching the endpoints is checked here."</i> The role was checked that way and the prompt
/// was not: <c>Refusal</c> asked whether it was empty and nothing else, so the only thing standing
/// between this box and a 30 MB body was nginx — which refuses with an HTML page the shim reads as an
/// unparseable vendor failure. A wall, on the one endpoint whose whole design is that a refusal
/// explains itself.</para>
/// <para><b>Two limits, because they answer different questions.</b> Kestrel's
/// <c>MaxRequestBodySize</c> refuses an oversized body WITHOUT READING IT, which is what protects a
/// 1.5 GB box from a payload it was never going to accept; <c>Refusal</c> measures the prompt and
/// produces the sentence a person can act on. A single check after deserialization would have
/// buffered the whole thing first, which is a bound that protects nothing. (Two reviewers,
/// independently, on plan 5's plan round.)</para>
/// </remarks>
[Collection(ServerCollection.Name)]
public sealed class APromptHasABoundHereTests
{
    private const string Vendors = """
        [{ "id": "codex", "runtime": "codex", "models": ["gpt-5.6-luna"], "slots": ["a"] }]
        """;

    private static TeamServer WithVendors()
    {
        var server = new TeamServer();
        File.WriteAllText(Path.Combine(server.DataDir, "vendors.json"), Vendors);

        return server;
    }

    private static ReviewRequestDto With(string prompt) =>
        new("codex", "gpt-5.6-luna", prompt, "Architecture", 60);

    [Fact]
    public async Task APromptAtTheBoundIsAccepted()
    {
        using var server = WithVendors();

        var response = await server.ClientFor($"dev@{TeamServer.Domain}")
            .PostAsJsonAsync("/api/reviews", With(new string('x', ReviewEndpoints.MaxPromptBytes)));

        response.StatusCode.Should().Be(
            HttpStatusCode.Accepted,
            "the bound is the largest prompt that works, not the smallest that fails");
    }

    [Fact]
    public async Task OneByteOverTheBoundIsRefusedWithBothNumbers()
    {
        using var server = WithVendors();

        var response = await server.ClientFor($"dev@{TeamServer.Domain}")
            .PostAsJsonAsync("/api/reviews", With(new string('x', ReviewEndpoints.MaxPromptBytes + 1)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<ErrorDto>();
        error!.Error.Should()
            .Contain($"{ReviewEndpoints.MaxPromptBytes + 1:N0}", "the size somebody actually sent")
            .And.Contain($"{ReviewEndpoints.MaxPromptBytes:N0}", "and the limit they met");
    }

    /// <summary>
    /// The measurement is in UTF-8 BYTES, which is what travels.
    /// </summary>
    /// <remarks>
    /// A character count lets a document of Cyrillic, Greek or CJK through at two or three times the
    /// size it claims — and the transport limits above this one are all in bytes, so the refusal and
    /// the thing it is protecting would be counting different quantities. This prompt is comfortably
    /// UNDER the bound in characters and over it in bytes; reverting the check to <c>Length</c>
    /// accepts it. (gemini, plan round.)
    /// </remarks>
    [Fact]
    public async Task TheBoundIsBytesAndNotCharacters()
    {
        using var server = WithVendors();
        // Three bytes each in UTF-8, so two thirds of the bound in characters is over it in bytes.
        var prompt = new string('д', (ReviewEndpoints.MaxPromptBytes / 2) + 1);
        prompt.Length.Should().BeLessThan(ReviewEndpoints.MaxPromptBytes, "under it as characters");
        Encoding.UTF8.GetByteCount(prompt).Should()
            .BeGreaterThan(ReviewEndpoints.MaxPromptBytes, "and over it as the bytes that travel");

        var response = await server.ClientFor($"dev@{TeamServer.Domain}")
            .PostAsJsonAsync("/api/reviews", With(prompt));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// A document-sized prompt is nowhere near the bound.
    /// </summary>
    /// <remarks>
    /// The bound exists to protect the boundary, never to refuse the feature it arrived with. A
    /// document is capped at <c>DocumentRules.MaxBytes</c> (256 KB) before it is ever composed, and
    /// what reaches here is that plus a purpose and a role prompt — an order of magnitude under this.
    /// Written as a test rather than as arithmetic in a comment, because arithmetic in a comment does
    /// not fail when somebody lowers the bound.
    /// </remarks>
    [Fact]
    public async Task ADocumentSizedPromptIsFarBelowTheBound()
    {
        using var server = WithVendors();
        var document = new string('x', CoaiMcp.Core.Rounds.DocumentRules.MaxBytes);
        var prompt = $"## What this document is for\n\nSomething\n\n## The document\n\n{document}";
        Encoding.UTF8.GetByteCount(prompt).Should().BeLessThan(ReviewEndpoints.MaxPromptBytes / 4);

        var response = await server.ClientFor($"dev@{TeamServer.Domain}")
            .PostAsJsonAsync("/api/reviews", With(prompt));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    /// <summary>
    /// The TRANSPORT limit is configured, so a body past it never reaches the handler at all.
    /// </summary>
    /// <remarks>
    /// <para>This is the half <c>Refusal</c> cannot do: by the time a handler can measure a prompt,
    /// the body is already in memory, and a bound that protects a 1.5 GB box has to refuse before
    /// that. The limit is on the REQUEST rather than on the field, so it carries an envelope
    /// allowance over the prompt bound.</para>
    /// <para><b>Asserted on the configured option rather than over the wire, and that is a real
    /// limitation of this test.</b> <c>WebApplicationFactory</c> hosts on <c>TestServer</c>, which is
    /// not Kestrel and enforces none of its limits — an HTTP round trip here would prove the
    /// opposite of what it looked like it proved. What can be checked honestly is that the option is
    /// wired and holds the value this file names; the enforcement itself is Kestrel's, and it is the
    /// same code path every ASP.NET Core deployment uses.</para>
    /// </remarks>
    [Fact]
    public void TheTransportLimitIsConfiguredOnTheServer()
    {
        using var server = WithVendors();

        var limits = server.Services
            .GetRequiredService<IOptions<KestrelServerOptions>>().Value.Limits;

        limits.MaxRequestBodySize.Should().Be(
            ReviewEndpoints.MaxBodyBytes,
            "the 30 MB default is thirty times what this box will ever be asked to review");
    }

    /// <summary>
    /// The three bounds are a LADDER, and the top rung is read from the site file that sets it.
    /// </summary>
    /// <remarks>
    /// <para>Prompt &lt; body &lt; edge. Each rung refuses something the one above it would have let
    /// through, and each refusal is worse than the one below: the prompt bound answers a 400 with a
    /// sentence, the body bound answers without reading the request, and nginx answers an HTML page
    /// the shim reads as an unparseable vendor failure. A change that inverts any pair silently moves
    /// every refusal up a rung.</para>
    /// <para><b>`client_max_body_size` is read from `deploy/nginx/coai` rather than written here.</b>
    /// It was a literal `4 * 1024 * 1024` in this test, which is an assumption about a file in another
    /// directory wearing an assertion's clothes: somebody lowering the edge to `2m` for a reason that
    /// has nothing to do with this would leave the test green and the ladder inverted. Two reviewers
    /// raised the relationship on plan 5's code round; this is what makes it checked rather than
    /// asserted.</para>
    /// </remarks>
    [Fact]
    public void TheThreeBoundsAreALadderFromTheSentenceToTheWall()
    {
        var nginx = NginxBodyLimit();

        ReviewEndpoints.MaxPromptBytes.Should().BeLessThan(
            (int)ReviewEndpoints.MaxBodyBytes,
            "a prompt AT the bound travels inside a JSON envelope with a vendor, a model and a role, "
            + "and its own escaping — so the body limit must never refuse a prompt this server "
            + "intended to answer with a sentence");
        ReviewEndpoints.MaxBodyBytes.Should().BeLessThan(
            nginx,
            "past client_max_body_size the edge answers with HTML and this server never hears about "
            + "it, so the application's refusal must be the one a caller meets first");
    }

    /// <summary>
    /// The headroom the prompt bound has for JSON escaping, stated as a number somebody can argue with.
    /// </summary>
    /// <remarks>
    /// A prompt is escaped into a JSON string on the way here, and escaping only grows it. Plain text
    /// grows by a fraction of a percent — quotes, backslashes and newlines — so a sixth is generous
    /// for anything a review actually carries. It is NOT total, and cannot be: a prompt of nothing but
    /// control characters escapes sixfold, and covering that would need a body limit above the edge.
    /// What the number buys is that every realistic payload meets the sentence rather than the wall.
    /// </remarks>
    [Fact]
    public void ThePromptBoundLeavesAtLeastASixthForEscaping()
    {
        var headroom = (ReviewEndpoints.MaxBodyBytes - ReviewEndpoints.MaxPromptBytes)
            / (double)ReviewEndpoints.MaxPromptBytes;

        headroom.Should().BeGreaterThanOrEqualTo(1 / 6d);
    }

    /// <summary>
    /// `client_max_body_size 4m;` from the deployed site file, in bytes.
    /// </summary>
    /// <remarks>
    /// nginx's `m` is a MEBIbyte, which is why this multiplies by 1024 twice — reading it as a
    /// megabyte would understate the edge by five per cent and make this test pass a ladder that is
    /// tighter than the real one.
    /// </remarks>
    private static long NginxBodyLimit()
    {
        var site = Path.Combine(RepoRoot(), "deploy", "nginx", "coai");
        File.Exists(site).Should().BeTrue($"the site file is what sets the edge's limit ({site})");
        var said = System.Text.RegularExpressions.Regex.Match(
            File.ReadAllText(site), @"^\s*client_max_body_size\s+(\d+)m;",
            System.Text.RegularExpressions.RegexOptions.Multiline);
        said.Success.Should().BeTrue("deploy/nginx/coai no longer sets client_max_body_size in MiB");

        return long.Parse(said.Groups[1].Value) * 1024 * 1024;
    }

    /// <summary>Up from the test binary until the folder holding `deploy/` is found.</summary>
    private static string RepoRoot()
    {
        var at = AppContext.BaseDirectory;
        while (at is { Length: > 0 } && !Directory.Exists(Path.Combine(at, "deploy")))
        {
            at = Path.GetDirectoryName(at);
        }

        return at ?? AppContext.BaseDirectory;
    }
}

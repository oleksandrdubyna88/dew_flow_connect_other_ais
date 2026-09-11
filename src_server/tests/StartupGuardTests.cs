using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The states this server refuses to start in. Each one would otherwise start, report healthy, and
/// answer 401 or 403 to everything with nothing in the log connecting the two.
/// </summary>
public sealed class StartupGuardTests
{
    private static readonly List<string> Company = ["example.com"];

    private static Action Guard(
        string? tenant = null,
        IReadOnlyCollection<string>? audiences = null,
        bool google = false,
        IReadOnlyCollection<string>? googleAudiences = null,
        string? localKey = "coai-server-test-signing-key-32b!!",
        IReadOnlyCollection<string>? domains = null,
        bool allowAny = false,
        string? dataDir = null,
        int ttlDays = 7) =>
        () => Startup.Guard(
            tenant,
            audiences ?? [],
            google,
            googleAudiences ?? [],
            !string.IsNullOrWhiteSpace(localKey),
            localKey,
            domains ?? Company,
            allowAny,
            dataDir ?? Directory.CreateTempSubdirectory("coai-guard-").FullName,
            ttlDays);

    [Fact]
    public void NoAuthenticationSchemeAtAll_RefusesToStart()
    {
        Guard(localKey: null).Should().Throw<InvalidOperationException>()
            .WithMessage("*would 401 every request*");
    }

    [Fact]
    public void AnEmptyDomainList_RefusesUnlessTheOverrideIsExplicit()
    {
        Guard(domains: []).Should().Throw<InvalidOperationException>()
            .WithMessage("*AllowedDomains is empty*");

        Guard(domains: [], allowAny: true).Should().NotThrow(
            "running without a boundary is allowed, but only as a decision somebody wrote down");
    }

    [Fact]
    public void AShortLocalSigningKey_RefusesRatherThan401ingEverything()
    {
        Guard(localKey: "too-short").Should().Throw<InvalidOperationException>()
            .WithMessage("*HMAC-SHA256 requires at least 32*");
    }

    /// <summary>
    /// Stricter than the vault server, and this is the reason: without an audience, ANY token from
    /// the tenant — including one minted for a different application entirely — would buy a
    /// seven-day pass to the company's paid subscriptions.
    /// </summary>
    /// <remarks>Raised on this story's plan round; the app registration exists from day one.</remarks>
    [Fact]
    public void AMicrosoftTenantWithNoAudience_RefusesToStart()
    {
        Guard(tenant: "34cedc64-a7d3-4e3b-a7c2-93858254abaa").Should().Throw<InvalidOperationException>()
            .WithMessage("*Audiences is empty*");

        // `localKey: null` is the point of this line rather than noise: a real provider and the
        // local scheme together are refused by the guard below, so leaving the helper's default
        // key here would pass for the wrong reason.
        Guard(tenant: "34cedc64-a7d3-4e3b-a7c2-93858254abaa", audiences: ["api://something"], localKey: null)
            .Should().NotThrow();
    }

    /// <summary>
    /// The local scheme signs identities with a shared secret: whoever holds the key can present
    /// any email an allowed domain covers. That is exactly what makes it right for the tests and
    /// for an air-gapped box — and an identity BYPASS beside a real provider, because the
    /// company's own sign-in becomes optional for anyone with the key.
    /// </summary>
    /// <remarks>
    /// <para>Raised by gemini's reviewer on the plan round of the client/server contract suite,
    /// which is what made this scheme a routine, documented path rather than a test detail.</para>
    /// <para>Until now it was a sentence in <c>Auth.cs</c> — "it is empty wherever a real identity
    /// provider exists" — and a sentence does not fail. The `coai.remsoft.dev` deployment was
    /// checked when this was written and carries no local key, so nothing was open; this closes
    /// the door rather than an incident.</para>
    /// </remarks>
    /// <summary>
    /// The same defect as the Microsoft one above, one identity provider over.
    /// </summary>
    /// <remarks>
    /// <para>Found by the product audit of 2026-09-09 (finding 9) and not by anybody using it. The
    /// paragraph justifying the Microsoft guard was written, agreed and never carried across, so
    /// <c>Auth.cs</c> read <c>ValidateAudience = googleAudiences.Count > 0</c> — a configuration
    /// mistake silently turning the check off instead of stopping the server.</para>
    /// <para>A Google ID token is handed to EVERY application a person signs into with Google, so
    /// without an audience check any third-party app a colleague ever used could present that
    /// colleague here. Issuer, signature, lifetime, the allowed domain and <c>email_verified</c> are
    /// all still checked, which is why this is a privilege path rather than anonymous access — and
    /// why it read as safe for as long as it did.</para>
    /// <para><c>coai.remsoft.dev</c> has Google disabled and Microsoft only (the operator, 2026-09-11),
    /// so this closes a door for the next deployment rather than an open one on that box.</para>
    /// </remarks>
    [Fact]
    public void GoogleEnabledWithNoAudience_RefusesToStart()
    {
        Guard(google: true, localKey: null).Should().Throw<InvalidOperationException>()
            .WithMessage("*Auth:Google:Audiences is empty*")
            .WithMessage("*minted for ANY application*");

        // `localKey: null` for the reason the Microsoft test gives: a real provider beside the local
        // scheme is refused by the guard below, so the helper's default key would pass this for the
        // wrong reason.
        Guard(google: true, googleAudiences: ["1234.apps.googleusercontent.com"], localKey: null)
            .Should().NotThrow();
    }

    [Fact]
    public void TheLocalSchemeBesideARealProvider_RefusesToStart()
    {
        Guard(tenant: "34cedc64-a7d3-4e3b-a7c2-93858254abaa", audiences: ["api://something"])
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*alongside a real identity provider*");

        // Audiences given, and they have to be: since 2026-09-11 a Google-enabled server with none
        // is refused BEFORE this check, so without them this half would pass on the wrong sentence —
        // and its subject is the local scheme beside a REAL provider, which a misconfigured one is
        // not. The same reason the Microsoft half above carries its audiences.
        Guard(google: true, googleAudiences: ["1234.apps.googleusercontent.com"])
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*alongside a real identity provider*");
    }

    [Fact]
    public void ASessionTtlOfNothing_RefusesToStart()
    {
        Guard(ttlDays: 0).Should().Throw<InvalidOperationException>()
            .WithMessage("*must outlive its own issuing*");
    }

    /// <summary>
    /// A data directory that cannot be written is a server that hands out credentials it will
    /// refuse a second later — and on a fresh host it is the ORDINARY first-boot state, because a
    /// bind mount belongs to root while this runs as an unprivileged uid.
    /// </summary>
    [Fact]
    public void ADataDirectoryItCannotWriteTo_RefusesToStart()
    {
        // A path whose PARENT is a file: creating a directory there is impossible on every OS,
        // which is what makes this a portable stand-in for an unwritable mount.
        var file = Path.Combine(Directory.CreateTempSubdirectory("coai-guard-").FullName, "a-file");
        File.WriteAllText(file, "");

        Guard(dataDir: Path.Combine(file, "data")).Should().Throw<InvalidOperationException>()
            .WithMessage("*is not writable*");
    }
}

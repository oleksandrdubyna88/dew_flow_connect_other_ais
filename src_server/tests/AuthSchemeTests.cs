using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// What each identity provider's token is actually checked against.
/// </summary>
/// <remarks>
/// <para><b>These assert what is CONFIGURED, not what is enforced, and the difference is the whole
/// caveat.</b> This repository's own security rule says a delegated option is a request until its
/// effect has been observed, and nothing here presents a token and watches it refused — the handler,
/// the key resolution and Google's own metadata are all outside what a test in this process can
/// reach without a loopback OIDC document.</para>
/// <para>What they DO catch is the regression that produced the defect in the first place. Until
/// 2026-09-11 `Auth.cs` read <c>ValidateAudience = googleAudiences.Count > 0</c>: a configuration
/// mistake silently turning the check off rather than stopping the server. `StartupGuardTests` now
/// refuses that configuration, but a later edit could reintroduce the conditional and startup would
/// still succeed — codex raised exactly that on this change's plan round, and it is a different
/// question from today's exposure. This is the cheapest thing that answers it.</para>
/// <para>The gap that remains is written down with its trigger rather than implied: a token carrying
/// the wrong <c>aud</c>, answered 401, is owed before Google is enabled on any deployment.
/// <c>coai.remsoft.dev</c> has it disabled (the operator, 2026-09-11).</para>
/// </remarks>
public sealed class AuthSchemeTests
{
    private const string GoogleClientId = "1234.apps.googleusercontent.com";

    private static JwtBearerOptions OptionsFor(
        string scheme, bool googleEnabled = true, IReadOnlyCollection<string>? googleAudiences = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        Auth.AddSchemes(
            services,
            msTenant: "34cedc64-a7d3-4e3b-a7c2-93858254abaa",
            msAudiences: ["api://something"],
            googleEnabled: googleEnabled,
            googleAudiences: googleAudiences ?? [GoogleClientId],
            localKey: null,
            localEnabled: false);

        return services.BuildServiceProvider()
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(scheme);
    }

    [Fact]
    public void TheGoogleSchemeChecksWhoTheTokenWasMintedFor()
    {
        var google = OptionsFor("Google");

        google.TokenValidationParameters.ValidateAudience.Should().BeTrue(
            "a Google ID token is handed to every application a person signs into with Google, so "
            + "without this any third-party app a colleague ever used could present that colleague here");
        google.TokenValidationParameters.ValidAudiences.Should().Equal([GoogleClientId]);
    }

    /// <summary>
    /// The check does not DEPEND on the list, which is the defect rather than its symptom.
    /// </summary>
    /// <remarks>
    /// <para>An empty audience list is a state `Startup.Guard` refuses, so this configuration cannot
    /// reach a running server — and that is exactly why the assertion is worth making here. The line
    /// that shipped for months was <c>ValidateAudience = googleAudiences.Count > 0</c>: a
    /// configuration mistake quietly turning the check OFF instead of stopping the server, which is
    /// the shape of every silent auth bypass.</para>
    /// <para><b>The first version of this test could not have caught that</b>, and the fact is worth
    /// keeping: it passed a NON-empty list, so the conditional was true and the assertion held with
    /// the defect restored. Measured on 2026-09-11 by putting the conditional back — four tests,
    /// zero failures. The teeth are here, in the one case where the two spellings differ.</para>
    /// </remarks>
    [Fact]
    public void TheGoogleAudienceCheckIsNotConditionalOnTheListBeingFilled()
    {
        var google = OptionsFor("Google", googleAudiences: []);

        google.TokenValidationParameters.ValidateAudience.Should().BeTrue(
            "a guard somewhere else refusing this configuration is not a reason for this line to "
            + "depend on it — that dependency IS the defect, and a later edit could remove the guard");
    }

    [Fact]
    public void TheGoogleSchemeStillChecksEverythingElseItAlwaysDid()
    {
        // The companion the assertion above needs: a scheme that validated ONLY the audience would
        // pass that test and accept a token this server has no other reason to trust.
        var google = OptionsFor("Google");

        google.TokenValidationParameters.ValidateIssuer.Should().BeTrue();
        google.TokenValidationParameters.ValidIssuers.Should().Contain("https://accounts.google.com");
        google.TokenValidationParameters.ValidateLifetime.Should().BeTrue();
    }

    [Fact]
    public void TheMicrosoftSchemeChecksTheSameThing()
    {
        // Not duplication: the Google defect existed because a decision made for Microsoft was never
        // carried across, and one of these two going quiet while the other holds is how that happens
        // again. They are asserted side by side for that reason.
        var microsoft = OptionsFor("Microsoft");

        microsoft.TokenValidationParameters.ValidateAudience.Should().BeTrue();
        microsoft.TokenValidationParameters.ValidAudiences.Should().Equal(["api://something"]);
    }

    [Fact]
    public void AServerWithGoogleDisabledRegistersNoGoogleSchemeAtAll()
    {
        // Which is why "Google disabled but the code path remains" is not a state this server has:
        // the scheme is registered inside the `if`, so there is nothing to misconfigure.
        var schemes = new ServiceCollection();
        schemes.AddLogging();
        Auth.AddSchemes(
            schemes,
            msTenant: "34cedc64-a7d3-4e3b-a7c2-93858254abaa",
            msAudiences: ["api://something"],
            googleEnabled: false,
            googleAudiences: [],
            localKey: null,
            localEnabled: false);

        var registered = schemes.BuildServiceProvider()
            .GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.AuthenticationOptions>>()
            .CurrentValue.Schemes.Select(s => s.Name);

        registered.Should().NotContain("Google");
        registered.Should().Contain("Microsoft");
    }
}

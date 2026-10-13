using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The capability an adapter declares to ask for a sanitised snapshot (D6: "which runtimes need it is a capability the
/// ADAPTER declares, never an <c>if</c> on a vendor name"). S1 adds the member; nothing sets it true until the Vibe and
/// Kimi adapters of S2/S4 exist — so today every shipped runtime still runs where it always did.
/// </summary>
public sealed class NoRuntimeAsksForASanitisedSnapshotYetTests
{
    [Fact]
    public void EveryShippedReviewerRuntime_StillRunsInTheTreeItself()
    {
        var runtimes = ReviewerRuntimeSelector.Default.Providers.Select(provider => ReviewerRuntimeSelector.Default.Find(provider)!).ToList();

        runtimes.Should().NotBeEmpty();
        runtimes.Should().OnlyContain(runtime => !runtime.NeedsASanitisedSnapshot, "no shipped adapter asks for a snapshot yet");
    }

    [Fact]
    public void EveryShippedConsultant_StillRunsInTheCheckout()
    {
        var consultants = ConsultantResolution.Consulting
            .Select(runtime => ConsultantResolution.For(new VendorIdentity(runtime, runtime, string.Empty)))
            .Where(consultant => consultant is not null)
            .Select(consultant => consultant!)
            .ToList();

        consultants.Should().NotBeEmpty();
        consultants.Should().OnlyContain(consultant => !consultant.NeedsASanitisedSnapshot);
    }
}

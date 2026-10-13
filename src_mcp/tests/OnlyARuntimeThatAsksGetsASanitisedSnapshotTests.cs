using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Vibe;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The capability an adapter declares to ask for a sanitised snapshot (D6: "which runtimes need it is a capability the
/// ADAPTER declares, never an <c>if</c> on a vendor name"). S1 added the member; S2's <see cref="VibeRuntime"/> is the
/// first to set it — every other shipped runtime still runs where it always did.
/// </summary>
public sealed class OnlyARuntimeThatAsksGetsASanitisedSnapshotTests
{
    [Fact]
    public void OnlyVibe_AmongTheShippedReviewerRuntimes_AsksForASnapshot()
    {
        var runtimes = ReviewerRuntimeSelector.Default.Providers.Select(provider => ReviewerRuntimeSelector.Default.Find(provider)!).ToList();

        runtimes.Should().NotBeEmpty();
        runtimes.Where(runtime => runtime.NeedsASanitisedSnapshot).Should().ContainSingle()
            .Which.Should().BeOfType<VibeRuntime>("Vibe reads only its cwd and loads its configuration from it (D6)");
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
        consultants.Should().OnlyContain(consultant => !consultant.NeedsASanitisedSnapshot, "the Vibe consultant is S3's");
    }
}

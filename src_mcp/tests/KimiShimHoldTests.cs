using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What the shim says about the lock's answer (decision 10 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>): a tree held, another tree still alive, or a hold that cannot
/// be taken at all — the last two are refusals, because the alternative to a refused turn is two refreshers on a rotating token.
/// </summary>
public sealed class KimiShimHoldTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimishimhold-");

    public void Dispose() => _dir.Dispose();

    private TreeHold AHold() => new(
        new FileStream(_dir.At("a.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None),
        new FileStream(_dir.At("b.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None));

    [Fact]
    public void ATakenHold_IsHeld_AndTheShimGivesItBackByDisposingIt()
    {
        var hold = AHold();

        var held = KimiShimHold.Held(new TreeHolding.Taken(hold), @"C:\persons\credentials");

        held.Should().BeOfType<AcpHolding.Held>().Subject.Hold.Dispose();
        new FileInfo(_dir.At("a.lock")).Invoking(file => file.Delete()).Should().NotThrow("the hold's files are free once it is disposed");
    }

    [Fact]
    public void ABusyHold_NamesTheFileAndSaysTheTurnIsGivenUp()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = KimiCredentialsLock.RootFor(_dir.At("locks"));
        Directory.CreateDirectory(_dir.At("persons", "credentials"));
        var held = KimiShimHold.Held(new TreeHolding.Busy(), _dir.At("persons", "credentials"));

        var sentence = held.Should().BeOfType<AcpHolding.Busy>().Subject.Sentence;

        sentence.Should().Contain("another Kimi started on this sign-in is still running").And.Contain(KimiCredentialsLock.TreePathFor(_dir.At("persons", "credentials"))).And.Contain("the turn is given up");
    }

    [Fact]
    public void AHoldThatCannotBeTaken_IsRefused_WithTheCauseAndTheReason()
    {
        var held = KimiShimHold.Held(new TreeHolding.Unusable(new IOException("the disk said no")), @"C:\persons\credentials");

        var sentence = held.Should().BeOfType<AcpHolding.Unusable>().Subject.Sentence;

        sentence.Should().Contain("the disk said no").And.Contain("two refreshers could race (D19)");
    }

    [Fact]
    public void AHoldingNobodyNamed_IsRefusedToo_NeverTreatedAsHeld()
    {
        KimiShimHold.Held(null!, @"C:\persons\credentials").Should().BeOfType<AcpHolding.Unusable>().Which.Sentence.Should().Contain("cannot be taken");
    }
}

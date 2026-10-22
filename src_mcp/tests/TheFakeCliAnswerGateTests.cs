using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>FAKECLI_ANSWER_GATE</c> (E5 S10, C33) holds the fake CLI's ANSWER until the gate file exists — and holds it BEFORE the
/// answer, not after: the vault read in <see cref="TheServerAnswersInitializeAtOnceTests"/> is "still running" only because the
/// fake has said nothing yet. The server test cannot pin that placement alone (it can reply to <c>initialize</c> before the fake
/// has even started), so the placement is pinned here, on the fake, where the gate is the only thing that moves.
/// </summary>
/// <remarks>
/// The markers are the proof, never a delay: <c>&lt;gate&gt;.held</c> is written when the launch reaches the gate and
/// <c>&lt;gate&gt;.answering</c> the instant before it writes anything. A fake that answered first and waited afterwards writes
/// <c>.answering</c> BEFORE <c>.held</c>, so seeing <c>.held</c> with <c>.answering</c> already there is the whole failure.
/// </remarks>
[Collection("fakecli-env")]
public sealed class TheFakeCliAnswerGateTests : IDisposable
{
    private const string Answer = "the vault's answer";

    /// <summary>The suite's guard: it names a hang and measures nothing.</summary>
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(60);

    private readonly TempDir _dir = TempDir.For("coai-answer-gate-");

    public void Dispose() => _dir.Dispose();

    private string Gate => _dir.At("answer.gate");

    [Fact]
    public async Task AClosedGate_HoldsTheAnswerBack_AndTheFakeSaysItIsParked()
    {
        using var fake = Launch(Gate);
        var stdout = fake.StandardOutput.ReadToEndAsync();
        try
        {
            (await Polls.Until(() => File.Exists(Gate + ".held"), Guard)).Should().BeTrue("the fake never reached its answer gate");

            File.Exists(Gate + ".answering").Should().BeFalse("the gate comes BEFORE the answer: a fake that answered first is not holding anything back");
            fake.HasExited.Should().BeFalse("a closed gate parks the launch");
            int.Parse(File.ReadAllText(Gate + ".held").Trim(), System.Globalization.CultureInfo.InvariantCulture)
                .Should().Be(fake.Id, "the marker names the launch that is parked, so a test can ask whether it is still alive");
        }
        finally
        {
            await File.WriteAllTextAsync(Gate, string.Empty, CancellationToken.None);
        }

        await fake.WaitForExitAsync().WaitAsync(Guard);

        (await stdout).Should().Be(Answer, "once the gate opens the held answer is given, whole");
        fake.ExitCode.Should().Be(0);
        File.Exists(Gate + ".answering").Should().BeTrue("the fake says when it starts to answer");
    }

    [Fact]
    public async Task WithNoGateNamed_TheAnswerComesAtOnce_AndNothingIsLeftBesideIt()
    {
        using var fake = Launch(gate: string.Empty);
        var stdout = fake.StandardOutput.ReadToEndAsync();

        await fake.WaitForExitAsync().WaitAsync(Guard);

        (await stdout).Should().Be(Answer);
        Directory.EnumerateFiles(_dir.Path).Should().BeEmpty("a launch without a gate leaves no gate markers");
    }

    /// <summary>The fake as the server's vault read launches it — <c>config -</c>, the key on stdin — steered by exactly this environment.</summary>
    private Process Launch(string gate)
    {
        var info = new ProcessStartInfo(FakeCliInvocations.Exe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add("config");
        info.ArgumentList.Add("-");
        // Nothing another test left in the process environment may steer this launch.
        foreach (var name in info.Environment.Keys.Where(key => key.StartsWith("FAKECLI_", StringComparison.Ordinal)).ToList())
        {
            info.Environment.Remove(name);
        }

        info.Environment["FAKECLI_MODE"] = "vendor";
        info.Environment["FAKECLI_STDOUT"] = Answer;
        if (gate.Length > 0)
        {
            info.Environment["FAKECLI_ANSWER_GATE"] = gate;
        }

        var process = Process.Start(info)!;
        process.StandardInput.WriteLine("a key");
        process.StandardInput.Close();

        return process;
    }
}

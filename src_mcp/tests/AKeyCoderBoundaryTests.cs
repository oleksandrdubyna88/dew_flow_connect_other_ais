using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s7 t5, DoD d3 — the two preflight rows a launch with a pool key adds, proved at the boundary alone: the person's sign-in file is not in the unit, and the key folder cannot be read from it. Each row is proved able to say no: its property is put
/// back (the bind) or taken away (<c>InaccessiblePaths</c>) by the test-only <c>Degrade</c> door, and that row refuses the launch. The key folder is inside the main checkout, which every unit sees (a folder under the temp folders is out of sight anyway).
/// </summary>
public sealed class AKeyCoderBoundaryTests
{
    private static ProcessRequest Keyed(CoderBed bed) =>
        bed.Fake("write-files", Path.Combine(bed.Worktree, "the-coder-ran.txt")) with { Environment = new Dictionary<string, string?> { [ClaudeConsultant.KeyVariable] = KeyWorld.Token("a") } };

    /// <summary>A key folder the unit WOULD see: inside the main checkout, which is bound into every unit read-only. A folder under the temp folders is out of sight anyway, so it would prove nothing.</summary>
    private static string KeysAt(CoderBed bed)
    {
        Assert.SkipUnless(SystemdUser.Works(out var why), why);
        var folder = Path.Combine(bed.Main, "keys-under-test");
        Directory.CreateDirectory(Path.Combine(folder, "claude"));
        File.WriteAllText(Path.Combine(folder, "claude", "k-000000000000.key"), "made-up\n");

        return folder;
    }

    [Fact]
    public async Task AKeyedLaunch_IsAdmitted_WithNoSignInBind_TheKeyFolderHidden_AndBothRowsHolding()
    {
        await using var bed = await CoderBed.CreateAsync();
        var keys = KeysAt(bed);
        var job = bed.Job with { KeysFolder = keys };

        var outcome = await bed.Turns(bed.Boundary()).RunAsync(job, Keyed(bed), "c1");

        var finished = outcome.Should().BeAssignableTo<CoderTurnOutcome>().Subject;
        finished.Should().NotBeOfType<CoderTurnOutcome.Refused>(finished.ToString());
        var record = bed.Launcher.Requests.SelectMany(request => request.Arguments).ToList();
        record.Where(argument => argument.StartsWith("Bind", StringComparison.Ordinal)).Should().NotContain(argument => argument.Contains(".credentials.json", StringComparison.Ordinal));
        record.Should().Contain($"InaccessiblePaths=-{keys}");
    }

    [Fact]
    public async Task TheKeyFolderRow_GoesRedWhenTheInaccessiblePathsPropertyIsDropped()
    {
        await using var bed = await CoderBed.CreateAsync();
        var keys = KeysAt(bed);
        var job = bed.Job with { KeysFolder = keys };
        var hide = $"InaccessiblePaths=-{keys}";

        var outcome = await bed.Turns(bed.Boundary(arguments => arguments.Contains(hide) ? [.. arguments.Take(arguments.ToList().IndexOf(hide) - 1), .. arguments.Skip(arguments.ToList().IndexOf(hide) + 1)] : arguments)).RunAsync(job, Keyed(bed), "c1");

        var refused = outcome.Should().BeOfType<CoderTurnOutcome.Refused>(outcome.ToString()).Subject;
        refused.Sentence.Should().Contain("keys-folder-hidden");
        refused.Record.Preflight.Single(row => row.Item == "keys-folder-hidden").Held.Should().BeFalse();
    }

    [Fact]
    public async Task TheSignInRow_GoesRedWhenTheBindIsPutBack()
    {
        await using var bed = await CoderBed.CreateAsync();
        var keys = KeysAt(bed);
        var job = bed.Job with { KeysFolder = keys };
        var bind = $"BindReadOnlyPaths={bed.SignIn}:{job.CoderSignInFile}";

        var outcome = await bed.Turns(bed.Boundary(arguments => arguments.Contains("NoNewPrivileges=yes") ? [.. arguments.Take(arguments.ToList().IndexOf("NoNewPrivileges=yes") - 1), "-p", bind, .. arguments.Skip(arguments.ToList().IndexOf("NoNewPrivileges=yes") - 1)] : arguments)).RunAsync(job, Keyed(bed), "c1");

        var refused = outcome.Should().BeOfType<CoderTurnOutcome.Refused>(outcome.ToString()).Subject;
        refused.Sentence.Should().Contain("sign-in-not-in-unit");
    }

    [Fact]
    public async Task ALaunchWithNoKey_BindsTheSignInFileAsItAlwaysDid_AndHasNoKeyFolderToHide()
    {
        Assert.SkipUnless(SystemdUser.Works(out var why), why);
        await using var bed = await CoderBed.CreateAsync();
        var request = bed.Fake("write-files", Path.Combine(bed.Worktree, "the-coder-ran.txt"));

        var outcome = await bed.Turns(bed.Boundary()).RunAsync(bed.Job, request, "c1");

        outcome.Should().NotBeOfType<CoderTurnOutcome.Refused>(outcome.ToString());
        bed.Launcher.Requests.SelectMany(rq => rq.Arguments).Should().Contain($"BindReadOnlyPaths={bed.SignIn}:{bed.Job.CoderSignInFile}");
        bed.Launcher.Requests.SelectMany(rq => rq.Arguments).Should().NotContain(argument => argument.StartsWith("InaccessiblePaths=-/", StringComparison.Ordinal) && argument.Contains("keys", StringComparison.Ordinal));
    }
}

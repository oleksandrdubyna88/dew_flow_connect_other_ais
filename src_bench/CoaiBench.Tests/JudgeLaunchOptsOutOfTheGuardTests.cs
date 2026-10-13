using System.Runtime.InteropServices;
using Xunit;
using FluentAssertions;
using CoaiBench.Judging;
using CoaiBench.Model;

namespace CoaiBench.Tests;

/// <summary>
/// The REAL launch path of the judge hands its child <c>SUBAGENT_MODEL_GUARD=off</c>: a fake
/// <c>claude</c> reports the value it finds in its own environment, and the judge returns it as the verdict.
/// </summary>
public sealed class JudgeLaunchOptsOutOfTheGuardTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-judge-fake-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string FakeClaude()
    {
        const string answer = """{"result":"{\"useful\":\"yes\",\"verdict\":\"guard=VALUE\"}"}""";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var cmd = Path.Combine(_dir, "fake-claude.cmd");
            File.WriteAllText(cmd, "@echo off\r\nmore >nul\r\n@echo " + answer.Replace("VALUE", "%SUBAGENT_MODEL_GUARD%") + "\r\n");
            return cmd;
        }

        var sh = Path.Combine(_dir, "fake-claude");
        File.WriteAllText(sh, "#!/bin/sh\ncat >/dev/null\nprintf '%s\\n' '" + answer.Replace("VALUE", "'\"$SUBAGENT_MODEL_GUARD\"'") + "'\n");
        File.SetUnixFileMode(sh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return sh;
    }

    [Fact]
    public async Task TheJudgeChildSeesTheGuardSwitchedOff_ThroughTheRealLaunch()
    {
        var work = new Case("split-once", "artifacts/bench/plan-B.md", "7133c2f", "4a27a17");
        var run = new RunRecord(work, "codex", 1, 1)
        {
            Stages = [new StageResult("CodeReview", 1.0) { Findings = [new Finding(Title: "something")] }],
        };

        var judged = await new Judge(FakeClaude(), "claude-fable-5-1", _dir).JudgeAsync(run, TestContext.Current.CancellationToken);

        judged.Stages[0].Findings[0].Verdict.Should().Be("guard=off");
        judged.Stages[0].Findings[0].Useful.Should().Be("yes");
    }
}

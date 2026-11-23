using System.Diagnostics;
using CoaiMcp.Core.Work;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// B12 — <c>coai-mcp --validate-work</c> run as a PROCESS answers the same bytes, and the same exit code, as the validator run in this test's own process, on every vector of
/// <c>shared/work-plan-vectors.json</c> and on the repository's <c>v2/</c> folder.
/// </summary>
/// <remarks>
/// The binary is the one <see cref="ServerBinary"/> names: the built (JIT) one, or — with <c>COAI_CONTRACT_EXE</c> set to a PUBLISHED binary, the release smoke — the Native AOT one. So the same
/// test is "JIT against in-process" in an ordinary run and "native against JIT" in the smoke, and a trim or AOT difference of the YAML reader or the JSON writer shows up as a byte that differs.
/// </remarks>
[Collection("console-out")]
public sealed class TheBinaryAnswersLikeTheInProcessValidatorTests : IDisposable
{
    private readonly TempDir _root = TempDir.For("coai-validate-binary-");

    private string DataDir => _root.At("data");

    private (int Code, string Out) InProcess(string cwd, WorkLimits limits)
    {
        var output = new StringWriter();
        var code = ValidateWorkMode.Run(["--validate-work", "plan"], Env(limits), cwd, output, _ => { });

        return (code, output.ToString());
    }

    private Func<string, string?> Env(WorkLimits limits) => name => name switch
    {
        "COAI_DATA_DIR" => DataDir,
        "COAI_ROLES_WORK_METHOD" => LimitsJson(limits),
        _ => null,
    };

    private static string LimitsJson(WorkLimits limits) =>
        $$"""{"maxEpicsPerEpoch":{{limits.MaxEpicsPerEpoch}},"mvpEpics":{"min":{{limits.MvpMin}},"max":{{limits.MvpMax}}},"maxStoriesPerEpic":{{limits.MaxStoriesPerEpic}},"maxTasksPerStory":{{limits.MaxTasksPerStory}}}""";

    private (int Code, string Out) AsProcess(string cwd, WorkLimits limits)
    {
        ProcessStartInfo info = OneShotProcess.StartInfo(DataDir, "--validate-work", "plan");
        info.WorkingDirectory = cwd;
        info.Environment["COAI_ROLES_WORK_METHOD"] = LimitsJson(limits);
        var (code, said, _) = OneShotProcess.Run(info, OneShotProcess.DefaultDeadline);

        return (code, said);
    }

    [Fact]
    public void EveryVector_GetsTheSameBytes_FromTheBinaryAndFromThisProcess()
    {
        var vectors = WorkPlan.Vectors();
        var different = new List<string>();
        for (var i = 0; i < vectors.Count; i++)
        {
            var vector = vectors[i];
            var cwd = _root.At($"vector-{i}");
            foreach (var (relative, text) in vector.Files)
            {
                var path = Path.Combine(cwd, "plan", relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text);
            }

            if (InProcess(cwd, vector.Limits) != AsProcess(cwd, vector.Limits))
            {
                different.Add(vector.Name);
            }
        }

        vectors.Count.Should().BeGreaterThan(80, "every vector ran");
        different.Should().BeEmpty("the binary and this process must answer the same bytes and the same exit code");
    }

    [Fact]
    public void TheV2Folder_GetsTheSameBytes_FromTheBinaryAndFromThisProcess()
    {
        var limits = TheV2FolderIsAValidPlanTests.DefaultLimits();
        var cwd = _root.At("v2-cwd");
        Directory.CreateDirectory(cwd);

        // The folder is named by its absolute path in both, so the answer's featureFolder is the same string.
        var inProcess = new StringWriter();
        var code = ValidateWorkMode.Run(["--validate-work", Path.Combine(RepoPaths.Root(), "v2")], Env(limits), cwd, inProcess, _ => { });
        ProcessStartInfo info = OneShotProcess.StartInfo(DataDir, "--validate-work", Path.Combine(RepoPaths.Root(), "v2"));
        info.WorkingDirectory = cwd;
        info.Environment["COAI_ROLES_WORK_METHOD"] = LimitsJson(limits);
        var (processCode, processOut, _) = OneShotProcess.Run(info, OneShotProcess.DefaultDeadline);

        code.Should().Be(0);
        processCode.Should().Be(0);
        processOut.Should().Be(inProcess.ToString());
    }

    public void Dispose() => _root.Dispose();
}

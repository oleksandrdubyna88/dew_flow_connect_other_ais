using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>The <c>validate_work</c> tool over a REAL stdio server (epoch 3 E1 s2 t4): the one-shot's answer for a good call, <c>{"error": ...}</c> for each refused argument.</summary>
public sealed class TheValidateWorkToolTests : IDisposable
{
    private readonly TempDir _root = TempDir.For("coai-validate-work-tool-");

    private string Repo => _root.At("repo");

    private void Write(string relative, string text)
    {
        var path = Path.Combine(Repo, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private async Task<JsonElement> Call(string repoPath, string featureFolder, params (string, string)[] env)
    {
        using var server = StdioServer.Start(_root.At("data"), alsoInTheEnvironment: env);
        try
        {
            await StdioServer.InitializeAsync(server);

            return await StdioServer.Call(server, 2, "validate_work", JsonSerializer.Serialize(new { repoPath, featureFolder }));
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }
    }

    [Fact]
    public async Task AGoodCall_AnswersTheValidation_ForThePlanInTheWorkingTree()
    {
        foreach (var (path, text) in WorkPlan.Base())
        {
            Write("plan/" + path, text);
        }

        var answer = await Call(Repo, "plan");

        answer.GetProperty("mode").GetString().Should().Be("validate-work");
        answer.GetProperty("valid").GetBoolean().Should().BeTrue();
        answer.GetProperty("featureFolder").GetString().Should().Be("plan");
        answer.GetProperty("limits").GetProperty("source").GetString().Should().Be("default");
    }

    [Fact]
    public async Task TheLimits_AreThoseOfTheServersDevice_AndAnInvalidPlanAnswersItsErrors()
    {
        var files = WorkPlan.Base();
        files[WorkPlan.TaskPath(WorkPlan.Story, "t2-two")] = WorkPlan.Task("t2-two");
        foreach (var (path, text) in files)
        {
            Write("plan/" + path, text);
        }

        var answer = await Call(Repo, "plan", ("COAI_ROLES_WORK_METHOD", """{"maxTasksPerStory":1}"""));

        answer.GetProperty("valid").GetBoolean().Should().BeFalse();
        answer.GetProperty("limits").GetProperty("source").GetString().Should().Be("device");
        answer.GetProperty("errors")[0].GetProperty("code").GetString().Should().Be("too-many-tasks");
    }

    [Theory]
    [InlineData("/etc", "is not a relative path")]
    [InlineData("../outside", "`..` segment")]
    [InlineData("plan/../..", "`..` segment")]
    [InlineData("C:\\plan", "is not a relative path")]
    [InlineData("", "featureFolder is empty")]
    [InlineData("nothing-here", "is not a folder")]
    [InlineData("empty", "no feature.yaml")]
    public async Task ARefusedArgument_AnswersAnError_NamingTheArgument(string featureFolder, string wording)
    {
        Directory.CreateDirectory(Path.Combine(Repo, "empty"));

        var answer = await Call(Repo, featureFolder);

        answer.EnumerateObject().Select(property => property.Name).Should().Equal("error");
        answer.GetProperty("error").GetString().Should().Contain(wording);
        answer.GetProperty("error").GetString().Should().NotContain("workItem", "the argument is called featureFolder here");
    }

    [Fact]
    public async Task ARepoThatIsNotAFolder_IsRefused()
    {
        var answer = await Call(_root.At("missing"), "plan");

        answer.GetProperty("error").GetString().Should().Contain("repoPath");
    }

    [Fact]
    public async Task AFeatureFolderThatRunsThroughALink_IsRefused()
    {
        foreach (var (path, text) in WorkPlan.Base())
        {
            Write("real/" + path, text);
        }

        Directory.CreateSymbolicLink(Path.Combine(Repo, "plan"), Path.Combine(Repo, "real"));

        var answer = await Call(Repo, "plan");

        answer.GetProperty("error").GetString().Should().Contain("link");
    }

    public void Dispose() => _root.Dispose();
}

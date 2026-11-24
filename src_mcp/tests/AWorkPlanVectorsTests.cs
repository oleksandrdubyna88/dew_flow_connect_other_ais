using System.Text.RegularExpressions;
using CoaiMcp.Core.Work;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E1 s2 t3 — <c>shared/work-plan-vectors.json</c> (written before the validator) is the contract: every vector gets exactly the codes it expects, every error code has one failing and
/// one passing vector, and the table of <c>shared/work-method.md</c> names every code.
/// </summary>
public sealed partial class AWorkPlanVectorsTests
{
    public static TheoryData<string> VectorNames => [.. WorkPlan.Vectors().Select(vector => vector.Name)];

    [Theory]
    [MemberData(nameof(VectorNames))]
    public void AVector_GetsExactlyTheCodesItExpects(string name)
    {
        var vector = WorkPlan.Vectors().Single(one => one.Name == name);

        var report = WorkPlan.Validate(vector.Files, vector.Limits);

        WorkPlan.Codes(report).Should().Equal(vector.Expect, string.Join("\n", report.Errors.Select(error => $"{error.Code} {error.Path}: {error.Message}")));
    }

    [Fact]
    public void VectorNames_AreUnique()
    {
        var names = WorkPlan.Vectors().Select(vector => vector.Name).ToList();

        names.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void EveryErrorCode_HasOneFailingAndOnePassingVector()
    {
        var vectors = WorkPlan.Vectors();

        foreach (var code in WorkErrorCodes.All)
        {
            vectors.Should().Contain(vector => vector.Covers == code && vector.Expect.Contains(code), $"'{code}' needs a failing vector");
            vectors.Should().Contain(vector => vector.Covers == code && vector.Expect.Count == 0, $"'{code}' needs a passing vector");
        }
    }

    [Fact]
    public void EveryVector_CoversAKnownCode_AndAPassingOneExpectsNothing()
    {
        foreach (var vector in WorkPlan.Vectors())
        {
            WorkErrorCodes.All.Should().Contain(vector.Covers, $"the vector '{vector.Name}' covers a code the validator does not have");
            vector.Expect.Where(code => !WorkErrorCodes.All.Contains(code)).Should().BeEmpty();
        }
    }

    [Fact]
    public void TheMethodFilesTable_NamesEveryCode_InTheValidatorsOrder()
    {
        var text = File.ReadAllText(Path.Combine(RepoPaths.Root(), "shared", "work-method.md"));
        var table = text[text.IndexOf("## Rules and error codes", StringComparison.Ordinal)..];
        table = table[..table.IndexOf("## Dependencies", StringComparison.Ordinal)];

        var codes = TableCode().Matches(table).Select(match => match.Groups[1].Value).ToList();

        codes.Should().Equal(WorkErrorCodes.All);
    }

    [GeneratedRegex(@"^\| `([a-z-]+)` \|", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex TableCode();
}

using CoaiMcp.Core.Work;
using CoaiMcp.Roles;
using CoaiMcp.Work;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// B1, the acceptance fixture of epoch 3 E1 s2: this repository's own <c>v2/</c> folder is a valid plan under the DEFAULT limits (an empty environment, so no person's setting is read).
/// If a file of the plan breaks a rule the rule stays and the file is reported; nothing here is loosened to make it pass.
/// </summary>
public sealed class TheV2FolderIsAValidPlanTests
{
    internal static WorkLimits DefaultLimits() => RolesAnswer.Compute(_ => null, string.Empty, string.Empty, claudeHook: false).WorkMethod.ToLimits();

    [Fact]
    public void B1_TheV2Folder_IsValid_WithTheDefaultLimits()
    {
        var folder = new DiskWorkFolder(Path.Combine(RepoPaths.Root(), "v2"));

        var report = new WorkPlanValidator(new StrictYamlReader()).Validate(folder, DefaultLimits());

        report.Errors.Should().BeEmpty(string.Join("\n", report.Errors.Select(error => $"{error.Code} {error.Path} [{error.Node}]: {error.Message}")));
        report.FeatureFound.Should().BeTrue();
        report.Valid.Should().BeTrue();
        report.Counts.Epochs.Should().Be(5);
        report.Counts.Tasks.Should().BeGreaterThan(50, "the walk found the plan");
    }

    [Fact]
    public void TheDefaultLimits_AreTheShippedDefaults()
    {
        DefaultLimits().Should().Be(new WorkLimits(MaxEpicsPerEpoch: 5, MvpMin: 1, MvpMax: 2, MaxStoriesPerEpic: 8, MaxTasksPerStory: 6));
    }

    [Fact]
    public void ThePlansNotes_AreOnlyNotes()
    {
        var folder = new DiskWorkFolder(Path.Combine(RepoPaths.Root(), "v2"));

        var report = new WorkPlanValidator(new StrictYamlReader()).Validate(folder, DefaultLimits());

        report.Notes.Count.Should().BeLessThan(WorkReport.MaxListed, "notes are few; if this grows, the credential-word note needs a look");
    }
}

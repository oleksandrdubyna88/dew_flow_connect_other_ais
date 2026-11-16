using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The agy hook allows a file reader only on a file inside a folder the launch was given — and never one inside the coai
/// folder it runs from (research/PLAN_the_agy_hook_checks_the_path.md, story S1).
/// </summary>
/// <remarks>
/// <para>The fixture stands the coai folder in as <c>W/home</c> (the payload's <c>cwd</c>); the roots are
/// <c>W/job-1</c> and <c>W/second</c>. Every deny test first proves, with a control payload, that the fixture is one
/// the rule accepts — so a deny is the rule speaking, not a rejected fixture. Links are real links
/// (<see cref="File.CreateSymbolicLink"/>) and skip where the machine cannot make one.</para>
/// </remarks>
public sealed class TheAgyHookReadsOnlyInsideItsRootsTests : IDisposable
{
    private readonly TempDir _temp = TempDir.For("coai-agy-hook-paths-");
    private readonly string _w;

    public TheAgyHookReadsOnlyInsideItsRootsTests()
    {
        // Resolved once: on a machine whose temp folder is itself behind a link, the fixture is spelled the way the hook sees it.
        _w = RealPath.Resolve(_temp.Path) is { Ok: true } real ? real.Path : _temp.Path;
        Put("home/.agents/hooks.json");
        Put("job-1/inside.txt");
        Put("job-1/secret.txt");
        Put("job-1/sub/deep.txt");
        Put("job-10/secret.txt");
        Put("second/b.txt");
        Put("outside/secret.txt");
        Put("outside/deeper/x.txt");
    }

    public void Dispose() => _temp.Dispose();

    // ---------- the fixture ----------

    private string At(string relative) => Path.Combine(_w, relative.Replace('/', Path.DirectorySeparatorChar));

    private string Home => At("home");

    private string Job1 => At("job-1");

    private string Second => At("second");

    private string Inside => At("job-1/inside.txt");

    private string Outside => At("outside/secret.txt");

    private void Put(string relative)
    {
        var full = At(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "text of " + relative);
    }

    private void Link(string relative, string text, bool folder = false)
    {
        try
        {
            if (folder)
            {
                Directory.CreateSymbolicLink(At(relative), text);
            }
            else
            {
                File.CreateSymbolicLink(At(relative), text);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"this machine cannot create a symbolic link: {e.Message}");
        }
    }

    // ---------- payloads and answers ----------

    /// <summary>The payload as agy 1.5.9 sends it: cwd, hookEventName, toolCall { args { Path }, id, name }, workspacePaths = [cwd, ..roots].</summary>
    private JsonObject Doc(string? path, string tool = "read_file", string? cwd = null, string[]? roots = null) => new()
    {
        ["cwd"] = cwd ?? Home,
        ["hookEventName"] = "PreToolUse",
        ["toolCall"] = new JsonObject
        {
            ["args"] = new JsonObject { ["Path"] = path },
            ["id"] = "call-1",
            ["name"] = tool,
        },
        ["workspacePaths"] = new JsonArray([.. Entries(cwd ?? Home, roots ?? [Job1, Second]).Select(entry => (JsonNode?)JsonValue.Create(entry))]),
    };

    private static IEnumerable<string> Entries(string cwd, string[] roots) => [cwd, .. roots];

    private string Call(string? path, string tool = "read_file", string? cwd = null, string[]? roots = null) =>
        Doc(path, tool, cwd, roots).ToJsonString();

    private static JsonObject Args(JsonObject doc) => (JsonObject)doc["toolCall"]!["args"]!;

    private static (string Decision, string Reason) Answer(string payload, EntryReader? read = null)
    {
        var root = JsonDocument.Parse(AntigravityReadOnly.Decide(payload, read)).RootElement;

        return (root.GetProperty("decision").GetString()!, root.TryGetProperty("reason", out var reason) ? reason.GetString()! : string.Empty);
    }

    private static void Allowed(string payload, string? why = null, EntryReader? read = null) =>
        Answer(payload, read).Decision.Should().Be("allow", why ?? payload);

    /// <summary>The control is allowed FIRST, then the payload is denied with the path reason.</summary>
    private void Denied(string payload, string? control = null, string? why = null, EntryReader? read = null)
    {
        Answer(control ?? Call(Inside)).Decision.Should().Be("allow", "the control is the same call with the one thing under test repaired");

        var (decision, reason) = Answer(payload, read);
        decision.Should().Be("deny", why ?? payload);
        reason.Should().Be(AntigravityReadOnly.DenyOutsideReason, why ?? payload);
    }

    private static string Reasonless(string payload)
    {
        var decision = Answer(payload).Decision;
        decision.Should().Be("deny", payload);

        return decision;
    }

    /// <summary>A reader that looks at the real disk and answers differently for ONE entry, matched by its last name.</summary>
    private static EntryReader Standing(string lastName, Func<string, Entry> answer) =>
        path => Path.GetFileName(path) == lastName ? answer(path) : RealPath.Lstat(path);

    // ---------- A: allowed ----------

    [Fact]
    public void A1_EveryFileReader_ReadsAFileInsideTheFirstRoot()
    {
        foreach (var tool in AntigravityReadOnly.HookAllowedTools)
        {
            Allowed(Call(Inside, tool), tool);
        }
    }

    [Fact]
    public void A2_EveryFileReader_ReadsAFileInsideTheSecondRoot()
    {
        foreach (var tool in AntigravityReadOnly.HookAllowedTools)
        {
            Allowed(Call(At("second/b.txt"), tool), tool);
        }
    }

    [Fact]
    public void A3_ADeeperFile_AndADotDotThatStaysInside_AreAllowed()
    {
        Allowed(Call(At("job-1/sub/deep.txt")));
        Allowed(Call(Path.Combine(Job1, "sub", "..", "inside.txt")));
    }

    [Fact]
    public void A4_ALinkInsideTheRoot_ToAFileInsideTheRoot_IsAllowed()
    {
        Link("job-1/in-link", "inside.txt");
        Link("job-1/abs-link", At("job-1/sub/deep.txt"));

        Allowed(Call(At("job-1/in-link")));
        Allowed(Call(At("job-1/abs-link")));
    }

    [Fact]
    public void A5_ARootSpelledThroughALink_ContainsItsOwnFiles()
    {
        Link("via", _w, folder: true);
        var roots = new[] { At("via/job-1") };

        Allowed(Call(At("via/job-1/inside.txt"), roots: roots));
        Allowed(Call(At("job-1/inside.txt"), roots: roots));
    }

    [Fact]
    public void A6_AMissingLastName_InsideARoot_IsDecidedNotRefused()
    {
        Allowed(Call(At("job-1/not-there.txt")));
        Allowed(Call(At("job-1/no-dir/x.txt")));
    }

    [Fact]
    public void A7_TheRootItself_IsDecided_WithOrWithoutATrailingSeparator()
    {
        Allowed(Call(Job1));
        Allowed(Call(Job1 + Path.DirectorySeparatorChar));
    }

    // ---------- B: denied ----------

    [Fact]
    public void B1_AFileOutsideEveryRoot_IsDenied() => Denied(Call(Outside));

    [Fact]
    public void B2_ASiblingWhoseNameStartsWithTheRootsName_IsDenied() => Denied(Call(At("job-10/secret.txt")));

    [Fact]
    public void B3_ADotDotThatLeavesTheRoot_IsDenied()
    {
        Denied(Call(Path.Combine(Job1, "..", "outside", "secret.txt")));
        Denied(Call(Path.Combine(Job1, "sub", "..", "..", "job-10", "secret.txt")));
    }

    [Fact]
    public void B4_ALeafLink_ToAFileOutside_IsDenied()
    {
        Link("job-1/leak", Outside);

        Denied(Call(At("job-1/leak")));
    }

    [Fact]
    public void B5_AFolderLink_ToAFolderOutside_IsDenied()
    {
        Link("job-1/dir", At("outside"), folder: true);

        Denied(Call(At("job-1/dir/secret.txt")));
    }

    [Fact]
    public void B6_ALinkWhoseTargetPathCrossesAnotherLink_IsDenied()
    {
        Link("job-1/l2", At("outside"), folder: true);
        Link("job-1/alias", At("job-1/l2/secret.txt"));

        Denied(Call(At("job-1/alias")));
    }

    [Fact]
    public void B7_ADotDotInsideALinkText_IsFoldedByTheDisk_NotByTheString()
    {
        Link("job-1/l3", At("outside/deeper"), folder: true);

        Denied(Call(Path.Combine(Job1, "l3", "..", "secret.txt")));
    }

    [Fact]
    public void B8_ADanglingLink_ToAPlaceOutside_IsDenied()
    {
        Link("job-1/gone", At("outside/nothing.txt"));

        Denied(Call(At("job-1/gone")));
    }

    [Fact]
    public void B9_ANameBelowAFile_OrADotDotAfterAMissingName_IsDenied()
    {
        Denied(Call(Path.Combine(Inside, "x")));
        Denied(Call(Path.Combine(Inside, "..", "secret.txt")));
        Denied(Call(Path.Combine(Job1, "nope", "..", "inside.txt")));
    }

    [Fact]
    public void B10_TheCoaiFolderItself_IsDenied() => Denied(Call(At("home/.agents/hooks.json")));

    [Fact]
    public void B11_WhenTheOnlyEntryIsTheCwd_NothingIsReadable()
    {
        var alone = Array.Empty<string>();

        Denied(Call(At("home/.agents/hooks.json"), roots: alone));
        Denied(Call(Inside, roots: alone));
    }

    [Fact]
    public void B12_ARootThatContainsTheCoaiFolder_StillDoesNotOpenIt()
    {
        var wide = new[] { _w };

        Denied(Call(At("home/.agents/hooks.json"), roots: wide), control: Call(Inside, roots: wide));
        Allowed(Call(At("job-10/secret.txt"), roots: wide));
    }

    [Fact]
    public void B13_TheCwdAndAnEntryNamingOneFolderThroughALink_AreTheSameFolder()
    {
        Link("via", _w, folder: true);
        var viaHome = At("via/home");

        var spelledAsLink = new[] { Home, Job1 };
        Denied(Call(At("home/.agents/hooks.json"), cwd: viaHome, roots: spelledAsLink), control: Call(Inside, cwd: viaHome, roots: spelledAsLink));

        var reverse = new[] { viaHome, Job1 };
        Denied(Call(At("home/.agents/hooks.json"), cwd: Home, roots: reverse), control: Call(Inside, cwd: Home, roots: reverse));
    }

    [Fact]
    public void B14_APathThatIsNotFullyQualified_IsDenied_WhateverTheProcessDirectoryIs()
    {
        var relative = new List<string> { "inside.txt", "job-1/inside.txt", "./inside.txt", "../job-1/inside.txt" };
        if (OperatingSystem.IsWindows())
        {
            relative.AddRange([@"\x", "C:x"]);
        }

        foreach (var path in relative)
        {
            Denied(Call(path), why: path);
        }
    }

    [Fact]
    public void B15_ACallWithoutAStringPath_IsDenied()
    {
        var args = new List<Action<JsonObject>>
        {
            doc => ((JsonObject)doc["toolCall"]!).Remove("args"),
            doc => doc["toolCall"]!["args"] = null,
            doc => doc["toolCall"]!["args"] = "a string",
            doc => Args(doc).Remove("Path"),
            doc => Args(doc)["Path"] = null,
            doc => Args(doc)["Path"] = 7,
            doc => Args(doc)["Path"] = new JsonArray(Inside),
            doc => Args(doc)["Path"] = new JsonObject { ["Path"] = Inside },
            doc => Args(doc)["Path"] = string.Empty,
        };

        for (var i = 0; i < args.Count; i++)
        {
            var doc = Doc(Inside);
            args[i](doc);

            Denied(doc.ToJsonString(), why: $"variant {i}: {doc.ToJsonString()}");
        }
    }

    [Fact]
    public void B16_ANulOrAnOverlongPath_IsDenied_NotThrown()
    {
        Denied(Call(Inside + "\0"));
        Denied(Call(Job1 + Path.DirectorySeparatorChar + new string('a', RealPath.MaxPathChars + 1)));
    }

    [Fact]
    public void B17_APayloadWithTheArgumentSpelledTwoWays_OrNotAsPath_IsDenied()
    {
        var two = Doc(Inside);
        Args(two)["path"] = Outside;
        Denied(two.ToJsonString(), why: "Path and path");

        var lower = Doc(null);
        Args(lower).Remove("Path");
        Args(lower)["path"] = Inside;
        Denied(lower.ToJsonString(), why: "path only");

        var upper = Doc(null);
        Args(upper).Remove("Path");
        Args(upper)["PATH"] = Inside;
        Denied(upper.ToJsonString(), why: "PATH only");
    }

    [Fact]
    public void B18_ADuplicatePropertyName_IsDenied_NotThrown()
    {
        static string Q(string text) => JsonSerializer.Serialize(text);

        var twoPaths = $$"""
            {"cwd":{{Q(Home)}},"toolCall":{"args":{"Path":{{Q(Inside)}},"Path":{{Q(Outside)}}},"name":"read_file"},"workspacePaths":[{{Q(Home)}},{{Q(Job1)}}]}
            """;
        var twoRootLists = $$"""
            {"cwd":{{Q(Home)}},"toolCall":{"args":{"Path":{{Q(Outside)}}},"name":"read_file"},"workspacePaths":[{{Q(Home)}},{{Q(Job1)}}],"workspacePaths":[{{Q(Home)}},{{Q(At("outside"))}}]}
            """;

        Reasonless(twoPaths);
        Reasonless(twoRootLists);
    }

    [Fact]
    public void B19_ACwdThatIsNotAResolvableFullyQualifiedPath_DeniesEveryRead()
    {
        var variants = new List<Action<JsonObject>>
        {
            doc => doc.Remove("cwd"),
            doc => doc["cwd"] = null,
            doc => doc["cwd"] = 12,
            doc => doc["cwd"] = string.Empty,
            doc => doc["cwd"] = "home",
            doc => doc["cwd"] = Home + "\0",
        };

        for (var i = 0; i < variants.Count; i++)
        {
            var doc = Doc(Inside);
            variants[i](doc);

            Denied(doc.ToJsonString(), why: $"variant {i}: {doc.ToJsonString()}");
        }
    }

    [Fact]
    public void B20_AWorkspacePathsThatIsNotAnArrayOfFullyQualifiedStrings_DeniesEveryRead()
    {
        var variants = new List<Action<JsonObject>>
        {
            doc => doc.Remove("workspacePaths"),
            doc => doc["workspacePaths"] = null,
            doc => doc["workspacePaths"] = Job1,
            doc => doc["workspacePaths"] = new JsonObject { ["0"] = Job1 },
        };
        var elements = new List<JsonNode?> { 7, null, string.Empty, "job-1", Job1 + "\0" };
        variants.AddRange(elements.Select(bad => (Action<JsonObject>)(doc => ((JsonArray)doc["workspacePaths"]!).Add(bad?.DeepClone()))));

        for (var i = 0; i < variants.Count; i++)
        {
            var doc = Doc(Inside);
            variants[i](doc);

            Denied(doc.ToJsonString(), why: $"variant {i}: {doc.ToJsonString()}");
        }
    }

    [Fact]
    public void B21_ALinkLoopAmongTheEntries_DeniesEveryRead()
    {
        Link("loop-a", At("loop-b"));
        Link("loop-b", At("loop-a"));

        Denied(Call(Inside, roots: [Job1, At("loop-a")]));
    }

    [Fact]
    public void B22_ARootThatCannotBeLookedAt_DeniesTheCall_EvenBesideAGoodRoot()
    {
        var reader = Standing("second", _ => throw new IOException("the disk said no"));

        Denied(Call(Inside), read: reader);
    }

    [Fact]
    public void B23_ALinkChainOverTheLimit_AndALinkLoop_AreDeniedAtOnce()
    {
        Link("job-1/loop-a", "loop-b");
        Link("job-1/loop-b", "loop-a");
        for (var i = 0; i < 44; i++)
        {
            Link($"job-1/chain-{i}", $"chain-{i + 1}");
        }

        Link("job-1/chain-44", "inside.txt");

        var clock = Stopwatch.StartNew();
        Denied(Call(At("job-1/loop-a")));
        Denied(Call(At("job-1/chain-0")));

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void B24_ALinkAnyReaderReports_IsFollowed_OnEveryMachine()
    {
        var reader = Standing("stood-in", _ => Entry.Link(Outside));

        Denied(Call(At("job-1/stood-in")), read: reader);
    }

    [Fact]
    public void B25_AReaderThatThrowsSomethingUnforeseen_IsADeny_NotAnException()
    {
        EntryReader reader = _ => throw new InvalidOperationException("nobody planned for this");

        Denied(Call(Inside), read: reader);
    }

    [Fact]
    public void B26_ACaseOnlyDifference_FollowsTheFileSystemsComparison()
    {
        var shouted = At("JOB-1/inside.txt");

        if (DocumentId.Comparison == StringComparison.OrdinalIgnoreCase)
        {
            Allowed(Call(shouted));
        }
        else
        {
            Denied(Call(shouted));
        }
    }

    [Fact]
    public void B27_TodaysOldPayload_AReaderWithNoPathAndNoFolders_IsDenied() =>
        Denied("""{"toolCall":{"name":"read_file","args":{}}}""");

    // ---------- C: the reasons ----------

    [Fact]
    public void C1_AnyOtherTool_IsDeniedWithTheReadOnlyReason_EvenOnAFileInsideTheRoot()
    {
        foreach (var tool in (string[])["write_file", "write_to_file", "run_command", "Read_File", string.Empty])
        {
            var (decision, reason) = Answer(Call(Inside, tool));

            decision.Should().Be("deny", tool);
            reason.Should().Be(AntigravityReadOnly.DenyReason, tool);
        }
    }

    [Fact]
    public void C2_ThePathReason_IsTheConstantSentence_AndNamesNoPath()
    {
        var (_, reason) = Answer(Call(Outside));

        reason.Should().Be("coai: this consultation reads only inside the folders it was given");
        reason.Should().NotContain(_w).And.NotContain("secret");
    }

    [Fact]
    public void C3_TheHookMode_AnswersAllowThenDeny_AndExitsZeroBothTimes()
    {
        foreach (var (payload, expected) in new[] { (Call(Inside), "allow"), (Call(Outside), "deny") })
        {
            using var output = new StringWriter();

            AntigravityReadOnly.RunHook(new StringReader(payload), output).Should().Be(0);

            JsonDocument.Parse(output.ToString()).RootElement.GetProperty("decision").GetString().Should().Be(expected);
        }
    }
}

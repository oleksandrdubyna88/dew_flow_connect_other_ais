using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The fixture and the helpers the agy hook's test classes share: the hook's place check
/// (research/PLAN_the_agy_hook_checks_the_path.md) and the stories of research/PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text.md.
/// </summary>
/// <remarks>
/// <para>The fixture stands the coai folder in as <c>W/home</c> (the hook's own folder — <see cref="HookSite.CoaiFolder"/>); the roots are
/// <c>W/job-1</c> and <c>W/second</c>, and <c>W/aaa</c> is a root whose name sorts before <c>home</c>. Every deny test first proves, with a
/// control payload, that the fixture is one the rule accepts — so a deny is the rule speaking, not a rejected fixture. Links are real links
/// (<see cref="File.CreateSymbolicLink"/>) and skip where the machine cannot make one.</para>
/// </remarks>
public abstract class AgyHookFixture : IDisposable
{
    private readonly TempDir _temp = TempDir.For("coai-agy-hook-paths-");

    protected AgyHookFixture()
    {
        // Resolved once: on a machine whose temp folder is itself behind a link, the fixture is spelled the way the hook sees it.
        W = RealPath.Resolve(_temp.Path) is { Ok: true } real ? real.Path : _temp.Path;
        Put("home/.agents/hooks.json");
        Put("aaa/inside.txt");
        Put("job-1/inside.txt");
        Put("job-1/secret.txt");
        Put("job-1/sub/deep.txt");
        Put("job-1/sub/a/b/x.txt");
        Put("job-1/.hidden");
        Put("job-1/..x/f.txt");
        Put("job-1/x..y.txt");
        Put("job-10/secret.txt");
        Put("second/b.txt");
        Put("outside/secret.txt");
        Put("outside/deeper/x.txt");
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _temp.Dispose();
        }
    }

    // ---------- the fixture ----------

    /// <summary>The fixture's folder, resolved.</summary>
    protected string W { get; }

    protected string At(string relative) => Path.Combine(W, relative.Replace('/', Path.DirectorySeparatorChar));

    protected string Home => At("home");

    protected string Job1 => At("job-1");

    protected string Second => At("second");

    protected string Inside => At("job-1/inside.txt");

    protected string Outside => At("outside/secret.txt");

    protected string TempPath => _temp.Path;

    protected void Put(string relative)
    {
        var full = At(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "text of " + relative);
    }

    protected void Link(string relative, string text, bool folder = false)
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

    /// <summary>A folder link inside the fixture to the fixture itself, its target spelled as <see cref="TempDir"/> spells it.</summary>
    /// <remarks>Not <c>W</c>: where the temp folder is behind a link (macOS: <c>/var</c> → <c>/private/var</c>), the teardown
    /// walks the unresolved spelling and must recognise the link's target as its own parent, or it stops the deletion
    /// (<c>LinkTargetLostException</c>, the macOS job of the 0.65.4 release PR). The hook resolves either spelling alike.</remarks>
    protected void LinkToTheFixture(string relative) => Link(relative, _temp.Path, folder: true);

    // ---------- the site ----------

    /// <summary>The site the product's hook has when it runs from <c>W/home/.agents</c>: the coai folder is <c>W/home</c>; every drive is a fixed one; <paramref name="folds"/> is this OS unless given.</summary>
    protected HookSite Site(EntryReader? read = null, Func<string, bool>? isLocal = null, bool? folds = null) => SiteAt(Home, read, isLocal, folds);

    /// <summary>A site with the coai folder given (null: the hook cannot tell where it runs).</summary>
    protected HookSite SiteAt(string? coaiFolder, EntryReader? read = null, Func<string, bool>? isLocal = null, bool? folds = null) =>
        new(coaiFolder, read ?? RealPath.Lstat, isLocal ?? Local, folds ?? OperatingSystem.IsWindows());

    /// <summary>The product's locality rule for THIS OS, with every drive a fixed one.</summary>
    protected static bool Local(string text) => Junctions.IsLocal(text, _ => DriveType.Fixed, OperatingSystem.IsWindows());

    /// <summary>A place that is not local on THIS OS: a share that does not exist on Windows, an autofs host on Unix.</summary>
    protected static string Remote => OperatingSystem.IsWindows() ? @"\\coai-no-such-host\share" : "/net/coai-no-such-host/share";

    // ---------- payloads and answers ----------

    /// <summary>The payload with <c>workspacePaths</c> exactly as listed, in that order, and <c>cwd</c> as given.</summary>
    protected JsonObject Sent(string? path, string cwd, string[] entries, string tool = "read_file") => new()
    {
        ["cwd"] = cwd,
        ["hookEventName"] = "PreToolUse",
        ["toolCall"] = new JsonObject
        {
            ["args"] = new JsonObject { ["Path"] = path },
            ["id"] = "call-1",
            ["name"] = tool,
        },
        ["workspacePaths"] = new JsonArray([.. entries.Select(entry => (JsonNode?)JsonValue.Create(entry))]),
    };

    /// <summary>The payload as agy 1.5.9 sends it: cwd, hookEventName, toolCall { args { Path }, id, name }, workspacePaths = [cwd, ..roots].</summary>
    protected JsonObject Doc(string? path, string tool = "read_file", string? cwd = null, string[]? roots = null) =>
        Sent(path, cwd ?? Home, [cwd ?? Home, .. roots ?? [Job1, Second]], tool);

    protected string Call(string? path, string tool = "read_file", string? cwd = null, string[]? roots = null) =>
        Doc(path, tool, cwd, roots).ToJsonString();

    protected static JsonObject Args(JsonObject doc) => (JsonObject)doc["toolCall"]!["args"]!;

    protected (string Decision, string Reason) Answer(string payload, EntryReader? read = null, HookSite? site = null) =>
        Parse(AntigravityReadOnly.Decide(payload, site ?? Site(read)));

    protected static (string Decision, string Reason) Parse(string answer)
    {
        var root = JsonDocument.Parse(answer).RootElement;

        return (root.GetProperty("decision").GetString()!, root.TryGetProperty("reason", out var reason) ? reason.GetString()! : string.Empty);
    }

    protected void Allowed(string payload, string? why = null, EntryReader? read = null, HookSite? site = null) =>
        Answer(payload, read, site).Decision.Should().Be("allow", why ?? payload);

    /// <summary>The control is allowed FIRST (by the default site unless <paramref name="controlSite"/> says otherwise), then the payload is denied with the path reason.</summary>
    protected void Denied(string payload, string? control = null, string? why = null, EntryReader? read = null, HookSite? site = null, HookSite? controlSite = null)
    {
        Answer(control ?? Call(Inside), site: controlSite ?? Site()).Decision.Should().Be("allow", "the control is the same call with the one thing under test repaired");

        var (decision, reason) = Answer(payload, read, site);
        decision.Should().Be("deny", why ?? payload);
        reason.Should().Be(AntigravityReadOnly.DenyOutsideReason, why ?? payload);
    }

    /// <summary>A reader that looks at the real disk and answers differently for ONE entry, matched by its last name.</summary>
    protected static EntryReader Standing(string lastName, Func<string, Entry> answer) =>
        path => Path.GetFileName(path) == lastName ? answer(path) : RealPath.Lstat(path);
}

using CoaiMcp.Core.Feature;
using CoaiMcp.Core.Outlining;
using CoaiMcp.Normalizer;
using CoaiMcp.Runners.Feature;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The feature pack goes to third-party models, so what the source resolver refuses (D15) is refused
/// here too: a credential-shaped FILE is named and never read, and the CONTENT of every other file —
/// its outlined signatures and its changed hunks — passes the same redaction a served file does.
/// </summary>
/// <remarks>
/// <para>S2.2a outlined a credential-shaped file like any other the moment its extension was a supported
/// language (<c>.env.production.ts</c> is TypeScript), and neither the outline nor the hunks ran the
/// redaction <c>SourceResolver</c> applies — so a secret in a default argument or a changed line went to
/// the reviewer verbatim. Built on a real repository with the real outliner, because both halves of the
/// defect are about what git hands over and what tree-sitter keeps.</para>
/// </remarks>
public sealed class ThePackWithholdsCredentialsTests : IAsyncLifetime
{
    private const string EnvSecret = "hunter2-the-staging-password";
    private const string VendorKey = "ghp_abcdefghijklmnop1234567890";
    private const string BearerToken = "abcdefghijklmnopqrstuvwxyz0123456789";

    private readonly ProcessLauncher _launcher = new();
    private TempGitRepo _repo = null!;
    private string _base = string.Empty;
    private string _head = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _repo = await TempGitRepo.InitAsync(_launcher, "coai-feature-credentials-");
        await Write("src/Client.cs", Client("return 1;"));
        await Write("src/Auth.cs", "public sealed class Auth\n{\n    public int Check(int n)\n    {\n        return n;\n    }\n}\n");
        await Write("config/.env.production.ts", Env("return 1;"));
        await _repo.CommitAsync("base");
        _base = await _repo.HeadAsync();

        await Write("src/Client.cs", Client($"var header = \"Bearer {BearerToken}\";\n        return header.Length;"));
        await Write("src/Auth.cs", "public sealed class Auth\n{\n    public int Check(int n)\n    {\n        return n + 1;\n    }\n}\n");
        await Write("config/.env.production.ts", Env("return 2;"));
        await _repo.CommitAsync("the feature");
        _head = await _repo.HeadAsync();
    }

    public async ValueTask DisposeAsync() => await _repo.DisposeAsync();

    private static string Client(string body) =>
        "public sealed class Client\n{\n"
        + $"    public int Connect(string host, string token = \"{VendorKey}\")\n"
        + "    {\n"
        + $"        {body}\n"
        + "    }\n}\n";

    private static string Env(string body) =>
        $"export function connect(password: string = \"{EnvSecret}\"): number {{\n  {body}\n}}\n";

    private async Task Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_repo.Path, path))!);
        await _repo.WriteAsync(path, text);
    }

    private Task<FeatureOutline> BuildAsync() =>
        new FeatureOutlineBuilder(_launcher, new TreeSitterOutliner()).BuildAsync(_repo.Path, _base, _head);

    /// <summary>Everything of the outline that reaches the reviewer: the section and both omission lists.</summary>
    private static string Pack(FeatureOutline outline) =>
        outline.Section + "\n" + OmissionsRenderer.Render(outline.Omissions, [], FeatureBudget.OmissionsReserveBytes);

    [Fact]
    public async Task ACredentialShapedFile_IsNamedAsWithheld_AndNoneOfItsContentReachesThePack()
    {
        var outline = await BuildAsync();
        var pack = Pack(outline);

        pack.Should().NotContain(EnvSecret, "a credential-shaped file is never read, whatever language its extension names");
        pack.Should().NotContain("export function connect", "not its outline either");
        outline.Omissions.NotOutlined.Should().ContainSingle(n => n.Path == "config/.env.production.ts")
            .Which.Reason.Should().Contain("credential").And.Contain(".env*", "the reason names the shape that withheld it");
    }

    [Fact]
    public async Task ASecretInAnOutlinedSignature_IsRedacted()
    {
        var pack = Pack(await BuildAsync());

        pack.Should().Contain("public int Connect(string host, string token", "the signature itself is still outlined");
        pack.Should().NotContain(VendorKey, "a default argument is content, and content passes the redaction");
        pack.Should().Contain("[redacted]");
    }

    [Fact]
    public async Task ASecretInAChangedHunk_IsRedacted()
    {
        var pack = Pack(await BuildAsync());

        pack.Should().Contain("#### src/Client.cs", "the changed member's hunk is shown");
        pack.Should().NotContain(BearerToken, "a changed line is content, and content passes the redaction");
        pack.Should().Contain("return header.Length;", "and the rest of the hunk is kept as it was");
    }

    /// <summary>The positive the refusals above need: an ordinary file — named for a credential WORD — is read and shown.</summary>
    [Fact]
    public async Task AnOrdinaryFileNamedForACredentialWord_IsOutlinedAndItsHunkShown()
    {
        var pack = Pack(await BuildAsync());

        pack.Should().Contain("### src/Auth.cs (M, +1/-1)", "D15 as narrowed: the words are applied to content, never to file names");
        pack.Should().Contain("+        return n + 1;");
    }

    /// <summary>Every shape the resolver refuses, derived from the ONE table, is withheld by the read plan too.</summary>
    [Fact]
    public void EveryCredentialShape_IsWithheldBeforeAnythingIsRead_EvenInASupportedLanguage()
    {
        var names = CredentialFiles.Patterns.Select(ExampleOf).ToList();
        var files = names.Select(n => new ChangedFile(n, string.Empty, FileChange.Modified, 1, 0, IsBinary: false)).ToList();

        var plan = ReadPlan.For(
            files,
            _ => (new GitObject(new string('a', 40), "blob", 10), true),
            _ => OutlineLanguage.TypeScript,
            FeatureOutlineLimits.Shipped);

        plan.Read.Should().BeEmpty("not one credential-shaped file is read");
        plan.NotRead.Keys.Should().BeEquivalentTo(names);
        plan.NotRead.Values.Should().OnlyContain(n => n.Reason.Contains("credential", StringComparison.Ordinal));
    }

    /// <summary>The companion: the same plan reads an ordinary file, so the check above cannot pass by reading nothing at all.</summary>
    [Fact]
    public void AnOrdinaryFile_IsRead_ByTheSamePlan()
    {
        var plan = ReadPlan.For(
            [new ChangedFile("providers/credentials.ts", string.Empty, FileChange.Modified, 1, 0, IsBinary: false)],
            _ => (new GitObject(new string('a', 40), "blob", 10), true),
            _ => OutlineLanguage.TypeScript,
            FeatureOutlineLimits.Shipped);

        plan.Read.Should().ContainSingle().Which.Path.Should().Be("providers/credentials.ts");
    }

    /// <summary>A file name each pattern matches, in a supported language where the pattern allows one.</summary>
    private static string ExampleOf(string pattern) =>
        pattern.EndsWith('*') ? $"deploy/{pattern[..^1]}.backup.ts" : $"deploy/server{pattern[1..]}";
}

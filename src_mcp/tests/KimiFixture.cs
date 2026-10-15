using System.Security.Cryptography;
using System.Text;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;

namespace CoaiMcp.Tests;

/// <summary>
/// A fake person's Kimi home — the shape the owner's real one has, built INSIDE the test's own <c>testsToDelete-kimi-</c>
/// folder and never anywhere else (E4.2 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>): a <c>config.toml</c> with the managed
/// provider and its OAuth slot, a <c>moonshot-ai</c> row with an <c>api_key</c>, a <c>[[hooks]]</c> entry and a model block of
/// the person's own; <c>credentials/</c> with two READ-ONLY files; and <c>bin/</c> with empty <c>kimi.exe</c>, <c>rg.exe</c> and
/// <c>fd.exe</c>. Never the developer's real <c>~/.kimi-code</c>, never a real token — the two canaries are synthetic strings.
/// </summary>
internal sealed class KimiFixture : IDisposable
{
    /// <summary>The refresh-token canary: lives in the fake <c>credentials</c> file and must never appear in anything coai writes (decision 12).</summary>
    public const string RefreshCanary = "CANARY-REFRESH-7f3a0c keep my sign-in private";

    /// <summary>The api-key canary: lives in the fake <c>config.toml</c>'s <c>moonshot-ai</c> row.</summary>
    public const string ApiKeyCanary = "CANARY-APIKEY-2b91e4";

    public const string SlotKey = "oauth/kimi-code-env-x";

    public const string BaseUrl = "https://api.kimi.com/coding/v1";

    public const string OauthHost = "https://auth.kimi.com";

    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-");

    public KimiFixture(string? config = null)
    {
        Directory.CreateDirectory(Credentials);
        Directory.CreateDirectory(Bin);
        File.WriteAllText(PersonsConfig, config ?? OwnerConfig());
        ReadOnly(Path.Combine(Credentials, "kimi-code-env-x.json"), $$"""{"refresh_token": "{{RefreshCanary}}", "access_token": "not-a-real-one"}""");
        ReadOnly(Path.Combine(Credentials, "kimi-code.json"), """{"refresh_token": "not-a-real-one-either"}""");
        foreach (var tool in (string[])["kimi.exe", "rg.exe", "fd.exe"])
        {
            File.WriteAllBytes(Path.Combine(Bin, tool), []);
        }
    }

    /// <summary>The folder every part of the fixture lives in.</summary>
    public string Folder => _dir.Path;

    /// <summary>The fake person's <c>~/.kimi-code</c>.</summary>
    public string PersonsHome => _dir.At("persons", ".kimi-code");

    public string Credentials => Path.Combine(PersonsHome, "credentials");

    public string PersonsConfig => Path.Combine(PersonsHome, "config.toml");

    public string Bin => Path.Combine(PersonsHome, "bin");

    /// <summary>The fake profile folder — what <c>KimiPlaces.Resolve</c> is given as the real profile.</summary>
    public string Profile => _dir.At("persons");

    /// <summary>The owner's shape, with the person's own model block (<c>max_context_size = 1048576</c>, capabilities in THEIR order).</summary>
    public static string OwnerConfig() => $"""
        default_model = "kimi-code/kimi-for-coding"
        theme = "dark"

        [providers."managed:kimi-code"]
        type = "kimi"
        base_url = "{BaseUrl}"

        [providers."managed:kimi-code".oauth]
        storage = "file"
        key = "{SlotKey}"
        oauth_host = "{OauthHost}"

        [providers."moonshot-ai"]
        type = "kimi"
        base_url = "https://api.moonshot.ai/v1"
        api_key = "{ApiKeyCanary}"

        [models."kimi-code/kimi-for-coding"]
        provider = "managed:kimi-code"
        model = "kimi-for-coding"
        max_context_size = 1048576
        capabilities = [ "tool_use", "thinking", "image_in" ]

        [[hooks]]
        event = "PreToolUse"
        command = "echo a-hook-the-person-wrote"
        """;

    /// <summary>The places for an owned root of this fixture, resolved the way the runtime resolves them: from an injected environment and a real file test.</summary>
    public KimiPlaces Places(string rootName = "kh-x") =>
        KimiPlaces.Resolve(Path.Combine(Folder, "owned", rootName), HostKind.Windows, Env, Profile, File.Exists);

    /// <summary>The environment of a person who set nothing but a PATH holding their own bin.</summary>
    public string? Env(string name) => name switch
    {
        "PATH" => Bin,
        "PATHEXT" => ".COM;.EXE;.BAT;.CMD",
        _ => null,
    };

    /// <summary>Every entry of the person's folder: relative path, attributes and the SHA-256 of its bytes — "whole" means this is unchanged.</summary>
    public IReadOnlyList<string> Hash() =>
    [
        .. Directory.EnumerateFileSystemEntries(PersonsHome, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => $"{Path.GetRelativePath(PersonsHome, path)} {File.GetAttributes(path)} {Digest(path)}"),
    ];

    private static string Digest(string path) =>
        File.Exists(path) ? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) : "-";

    private static void ReadOnly(string path, string text)
    {
        File.WriteAllText(path, text, new UTF8Encoding(false));
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
    }

    public void Dispose() => _dir.Dispose();
}

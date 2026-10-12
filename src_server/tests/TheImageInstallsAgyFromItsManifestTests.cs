using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The container image installs agy from Google's release MANIFEST — checked, hashed, and never from the
/// <c>antigravity.google</c> page.
/// </summary>
/// <remarks>
/// <para><b>Why the page is gone.</b> On GitHub's runners <c>https://antigravity.google/cli/install.sh</c> answered an
/// HTML page instead of the script (release run 38075423539), while the same URL serves <c>text/x-sh</c> here — and the
/// image is built only by the release workflow, so it broke a release. That script does not need the page for the real
/// download anyway: it reads <c>…run.app/manifests/&lt;platform&gt;.json</c> (<c>{version, url, sha512}</c>), downloads the
/// tarball, checks its SHA-512, and copies the <c>antigravity</c> binary to <c>~/.local/bin/agy</c>.
/// <c>src_server/install-agy.sh</c> does exactly that from the same endpoints.</para>
/// <para>Every case runs the REAL script with bash against <c>file://</c> fixtures built here: a tarball with a fake
/// <c>antigravity</c>, a manifest naming it and its hash, and stub <c>uname</c>/<c>ldd</c> first on PATH so the machine the
/// script believes it is on is the test's choice, not the runner's. Where no bash can be found the case reports
/// itself skipped rather than passing.</para>
/// </remarks>
public sealed class TheImageInstallsAgyFromItsManifestTests : IDisposable
{
    private const string FakeAgy = "#!/bin/sh\necho 'agy 9.9.9-fixture'\n";

    private const string Refused = "did not answer with a release manifest";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-install-agy-" + Guid.NewGuid().ToString("N"));

    public TheImageInstallsAgyFromItsManifestTests() => Directory.CreateDirectory(_dir);

    private string Home => Path.Combine(_dir, "home");

    private string Installed => Path.Combine(Home, ".local", "bin", "agy");

    private string Manifests => Path.Combine(_dir, "manifests");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temp directory a stray handle still holds is the OS's to clean.
        }
    }

    [Fact]
    public void AManifestWhoseHashMatches_InstallsAgy_AndItRuns()
    {
        var (tarball, sha512) = Tarball();
        Manifest("linux_amd64", FileUrl(tarball), sha512);

        var (code, stderr) = Run();

        code.Should().Be(0, stderr);
        File.Exists(Installed).Should().BeTrue("the binary the manifest named, with the hash it named, is what agy is");
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(Installed).Should().HaveFlag(UnixFileMode.UserExecute, "the Dockerfile runs it next");
        }

        RunInstalled().Should().Contain("agy 9.9.9-fixture", "the installed file is the archive's antigravity, and it starts");
        stderr.Should().Contain("fetching", "each step is announced BEFORE it starts — a stalled fetch is silent for minutes")
            .And.Contain("linux_amd64.json")
            .And.Contain("version 1.3.3-fixture")
            .And.Contain("SHA-512 verified")
            .And.Contain("installed");
    }

    [Fact]
    public void APayloadWhoseHashDiffers_IsRefused_AndNothingIsInstalled()
    {
        var (tarball, _) = Tarball();
        Manifest("linux_amd64", FileUrl(tarball), new string('0', 128));

        var (code, stderr) = Run();

        code.Should().NotBe(0, "a payload the manifest does not vouch for must fail the build");
        stderr.Should().Contain("does not match", "the build log must say why");
        File.Exists(Installed).Should().BeFalse("a payload that failed its checksum is never installed");
    }

    [Fact]
    public void APageInPlaceOfTheManifest_IsRefused_Shown_AndTriedAgain()
    {
        Directory.CreateDirectory(Manifests);
        Write(Path.Combine(Manifests, "linux_amd64.json"), "<!doctype html>\n<html><body>Not here</body></html>\n");

        var (code, stderr) = Run();

        code.Should().NotBe(0, "a persistent bad answer must still fail the build");
        stderr.Should().Contain("<!doctype html>", "the log shows what arrived instead of a manifest");
        RefusalLines(stderr, Refused).Should().Be(3, "a transient bad answer is what the retry is for");
        File.Exists(Installed).Should().BeFalse();
    }

    [Fact]
    public void AManifestWithoutAChecksum_IsRefused()
    {
        var (tarball, _) = Tarball();
        Directory.CreateDirectory(Manifests);
        Write(Path.Combine(Manifests, "linux_amd64.json"), $"{{\n  \"version\": \"1.3.3\",\n  \"url\": \"{FileUrl(tarball)}\"\n}}\n");

        var (code, stderr) = Run();

        code.Should().NotBe(0, "without a sha512 nothing can vouch for the download");
        RefusalLines(stderr, Refused).Should().Be(3);
        File.Exists(Installed).Should().BeFalse("an unchecked download is never installed");
    }

    [Fact]
    public void AMissingManifest_IsTriedAgain_AndThenFails()
    {
        var (code, stderr) = Run();

        code.Should().NotBe(0);
        RefusalLines(stderr, Refused).Should().Be(3);
        File.Exists(Installed).Should().BeFalse();
    }

    [Fact]
    public void AMissingPayload_IsTriedAgain_AndNothingIsInstalled()
    {
        Manifest("linux_amd64", FileUrl(Path.Combine(_dir, "gone.tar.gz")), new string('0', 128));

        var (code, stderr) = Run();

        code.Should().NotBe(0);
        RefusalLines(stderr, "could not be downloaded").Should().Be(3);
        File.Exists(Installed).Should().BeFalse();
    }

    [Fact]
    public void ARawBinaryPayload_IsInstalledAsItIs()
    {
        // Google's own script takes a payload that is not a .tar.gz as the binary itself; so does this one.
        var binary = Path.Combine(_dir, "agy_linux_x64");
        Write(binary, FakeAgy);
        Manifest("linux_amd64", FileUrl(binary), Sha512(binary));

        var (code, stderr) = Run();

        code.Should().Be(0, stderr);
        RunInstalled().Should().Contain("agy 9.9.9-fixture");
    }

    [Theory]
    [InlineData("x86_64", "glibc", "linux_amd64")]
    [InlineData("aarch64", "glibc", "linux_arm64")]
    [InlineData("x86_64", "musl", "linux_amd64_musl")]
    [InlineData("aarch64", "musl", "linux_arm64_musl")]
    public void EachMachineFetchesItsOwnManifest(string machine, string libc, string platform)
    {
        // Only THIS platform's manifest exists, so an install proves the script asked for it.
        var (tarball, sha512) = Tarball();
        Manifest(platform, FileUrl(tarball), sha512);

        var (code, stderr) = Run(machine, libc);

        code.Should().Be(0, stderr);
        stderr.Should().Contain($"{platform}.json");
        File.Exists(Installed).Should().BeTrue();
    }

    [Fact]
    public void AnUnsupportedMachine_IsRefusedByName_BeforeAnyFetch()
    {
        var (code, stderr) = Run("armv7l");

        code.Should().NotBe(0, "Google publishes no build for it");
        stderr.Should().Contain("armv7l", "the log names the machine it cannot serve")
            .And.NotContain("fetching");
    }

    /// <summary>A tarball holding one file, <c>antigravity</c>, mode 0755 — the shape Google's tarballs have.</summary>
    private (string Path, string Sha512) Tarball()
    {
        var path = Path.Combine(_dir, "cli_linux_x64.tar.gz");
        using (var file = File.Create(path))
        using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Ustar, leaveOpen: false))
        {
            tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "antigravity")
            {
                Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute,
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes(FakeAgy)),
            });
        }

        return (path, Sha512(path));
    }

    /// <summary>A manifest in Google's own layout: pretty-printed, one key per line.</summary>
    private void Manifest(string platform, string url, string sha512)
    {
        Directory.CreateDirectory(Manifests);
        Write(Path.Combine(Manifests, platform + ".json"),
            $"{{\n  \"version\": \"1.3.3-fixture\",\n  \"url\": \"{url}\",\n  \"sha512\": \"{sha512}\"\n}}\n");
    }

    private static string Sha512(string path) => Convert.ToHexStringLower(SHA512.HashData(File.ReadAllBytes(path)));

    private static void Write(string path, string text) =>
        File.WriteAllText(path, text.Replace("\r\n", "\n", StringComparison.Ordinal));

    private static void Script(string path, string text)
    {
        Write(path, text);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static int RefusalLines(string stderr, string refusal) =>
        stderr.Split('\n').Count(line => line.Contains(refusal, StringComparison.Ordinal));

    /// <summary>
    /// Runs the real <c>install-agy.sh</c> with bash, no pause between attempts, <c>HOME</c> in the temp directory, the
    /// manifests read from <c>file://</c>, and <c>uname</c>/<c>ldd</c> stubs saying which machine this is.
    /// </summary>
    private (int Code, string StdErr) Run(string machine = "x86_64", string libc = "glibc")
    {
        var bin = Directory.CreateDirectory(Path.Combine(_dir, "bin")).FullName;
        Script(Path.Combine(bin, "uname"),
            $"#!/bin/sh\ncase \"$1\" in -m) echo '{machine}' ;; -o) echo 'GNU/Linux' ;; *) echo 'Linux' ;; esac\n");
        Script(Path.Combine(bin, "ldd"),
            libc == "musl" ? "#!/bin/sh\necho 'musl libc (stub)'\n" : "#!/bin/sh\necho 'libc.so.6 => /lib/libc.so.6 (stub)'\n");
        Directory.CreateDirectory(Home);
        var script = ShellScripts.Posix(Path.Combine(ShellScripts.RepoRoot(), "src_server", "install-agy.sh"));

        return Bash($"export PATH='{ShellScripts.PathEntry(bin)}':\"$PATH\"; exec bash '{script}'");
    }

    /// <summary>Starts the installed <c>agy</c> the way the Dockerfile does next: <c>agy --version</c>.</summary>
    private string RunInstalled()
    {
        var (code, stdout) = Bash($"'{ShellScripts.Posix(Installed)}' --version", readStdout: true);
        code.Should().Be(0, stdout);

        return stdout;
    }

    private (int Code, string Text) Bash(string command, bool readStdout = false)
    {
        var bash = ShellScripts.Bash();
        Assert.SkipWhen(bash.Length == 0, "no bash on this machine — the script's behaviour runs on CI's Linux and macOS legs");
        var start = new ProcessStartInfo(bash)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(command);
        start.Environment["HOME"] = ShellScripts.Posix(Home);
        start.Environment["INSTALL_CLI_PAUSE"] = "0";
        start.Environment["AGY_MANIFEST_BASE"] = FileUrl(Manifests);
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit(TimeSpan.FromSeconds(60)).Should().BeTrue("the script must not hang");

        return (process.ExitCode, readStdout ? stdout + stderr.Result : stderr.Result);
    }

    private static string FileUrl(string path) => "file:///" + ShellScripts.Posix(path).TrimStart('/');
}

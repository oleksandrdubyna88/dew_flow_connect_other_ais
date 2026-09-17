using System.Diagnostics;
using System.Text;
using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// The key the HOST would test with is a key the SERVER accepts.
/// </summary>
/// <remarks>
/// <para><b>Two parsers, one rule.</b> `deploy/bugs/first-key.sh` picks the first usable key out of a
/// delivered list so the deploy can make one authenticated request with it; `AdminKeys.Lines` decides
/// which lines of the same list are keys. They have to agree, and a code round found that they did
/// not: the shell dropped `#` comments BEFORE trimming, so an indented `  # alice` became the "key"
/// — and the character check then refused it, failing a deployment over a list the server was
/// perfectly happy with. `AdminKeys.Lines` trims first, which makes an indented comment a comment.
/// </para>
/// <para><b>And nothing would have noticed.</b> Every other test in this suite hands the server an
/// environment built in C#, so the shell could say anything at all and the suite would stay green.
/// That was the second finding, and this file is its answer: it runs THE REAL SCRIPT, over a table of
/// lists an operator might plausibly write, and asserts that whatever it picks is a credential this
/// server admits — the whole contract, in the only terms that matter.</para>
/// <para>The shell harness is <c>TheArchiveCheckTests</c>'s, for its reason: on CI a missing `sh` is
/// a broken job and says so; a Windows checkout without git's `sh` on PATH skips, because the
/// alternative is a suite nobody can run locally.</para>
/// </remarks>
public sealed class TheDeliveryAgreesWithTheServerTests
{
    private const string Secret = "a-server-secret";

    /// <summary>Lists an operator might plausibly write, and the key each one really offers.</summary>
    /// <remarks>
    /// Every one of these is a shape somebody types: a note above each key, a list indented to line
    /// up under the marker, a value pasted with a trailing space, a file saved with CRLF.
    /// </remarks>
    public static TheoryData<string, string> EveryPlausibleList() => new()
    {
        { "alices-key\nbobs-key\n", "alices-key" },
        { "# alice\nalices-key\n# bob\nbobs-key\n", "alices-key" },
        { "  # alice, indented under the marker\n  alices-key\n", "alices-key" },
        { "\n\n\nalices-key\n", "alices-key" },
        { "alices-key  \n", "alices-key" },
        { "\talices-key\t\n", "alices-key" },
        { "alices-key\r\nbobs-key\r\n", "alices-key" },
        { "# only a note, then the key\n\n  \nalices-key\n", "alices-key" },
    };

    /// <summary>
    /// What the host picks is what the server holds — for every shape of list.
    /// </summary>
    /// <remarks>
    /// The assertion is the MATCH rather than a string comparison, because the contract is not "the
    /// two agree on characters", it is "the deploy tests a credential this server admits". A shell
    /// that trimmed differently but still produced an accepted key would be fine; one that produced
    /// a rejected one is the false failure this exists to prevent.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryPlausibleList))]
    public void TheKeyTheHostPicksIsOneTheServerAccepts(string list, string expected)
    {
        var delivered = Delivered(list);
        var picked = FirstKey(delivered);

        picked.Should().Be(expected, "the host and the server must agree on which line is a key");

        var admins = Administrators(delivered);
        admins.Match(picked, Secret).Should().BeOfType<AdminKeys.Presented.Administrator>(
            "the deploy would have sent this key and been refused by the server it just deployed");
    }

    /// <summary>A list of nothing but notes offers no key, and the host says so rather than sending one.</summary>
    [Fact]
    public void AListOfOnlyCommentsOffersNoKeyAndTheHostSaysSo()
    {
        var delivered = Delivered("# nobody yet\n\n  # not yet\n");

        var (code, error, _) = Run(delivered);

        code.Should().Be(1);
        error.Should().Contain("holds no key");
        Administrators(delivered).None.Should().BeTrue("and the server agrees there is nobody");
    }

    /// <summary>A value nobody encoded is refused as what it is, rather than blamed on the key.</summary>
    [Fact]
    public void SomethingThatIsNotBase64IsRefusedAsThat()
    {
        var (code, error, _) = Run("this is not base64!!");

        code.Should().Be(1);
        error.Should().Contain("not base64");
    }

    /// <summary>
    /// A key holding a character curl's config format gives meaning to never reaches the file.
    /// </summary>
    /// <remarks>
    /// The server would accept it — an admin key is any string it was given — so this is the one
    /// place the host is deliberately STRICTER than the server, and a refusal here is the right
    /// answer: the alternative is writing a quote into a curl config and finding out what it means.
    /// </remarks>
    [Fact]
    public void AKeyWithAQuoteInItIsRefusedBeforeItIsWrittenAnywhere()
    {
        var (code, error, _) = Run(Delivered("bad\"key\n"));

        code.Should().Be(1);
        error.Should().Contain("character a credential should not");
    }

    /// <summary>The list, as the deploy delivers it: marked, encoded, one line.</summary>
    private static string Delivered(string list) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(AdminKeys.Marker + "\n" + list));

    /// <summary>The administrators that same value configures.</summary>
    private static AdminKeys Administrators(string delivered)
    {
        var read = AdminKeys.Read(delivered, Secret);
        read.Should().BeOfType<AdminKeys.Configured.Admins>(
            "the fixtures here are values the server starts with: " + (read as AdminKeys.Configured.Refused)?.Why);

        return ((AdminKeys.Configured.Admins)read).Keys;
    }

    /// <summary>What the host would send, or a failure if it would send nothing.</summary>
    private static string FirstKey(string delivered)
    {
        var (code, error, key) = Run(delivered);

        code.Should().Be(0, "the host refused a list the server accepts: {0}", error);

        return key;
    }

    /// <summary>Runs the real script the real way: the blob on stdin, the key on stdout.</summary>
    private static (int Code, string Error, string Key) Run(string delivered)
    {
        var script = Path.Combine(Repository(), "deploy", "bugs", "first-key.sh");
        File.Exists(script).Should().BeTrue("{0} is what the deploy runs", script);

        var start = new ProcessStartInfo("sh")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Posix(script));

        using var process = StartOrExplain(start);
        process.StandardInput.Write(delivered);
        process.StandardInput.Close();
        var key = process.StandardOutput.ReadToEnd().Trim();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(milliseconds: 30_000).Should().BeTrue("picking a key should not hang");

        return (process.ExitCode, error, key);
    }

    /// <summary>A path a POSIX shell will accept, which a Windows one is not.</summary>
    private static string Posix(string path) => path.Replace('\\', '/');

    /// <summary>A POSIX shell, or a decision about whose machine this is.</summary>
    private static Process StartOrExplain(ProcessStartInfo start)
    {
        try
        {
            return Process.Start(start)!;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            if (Environment.GetEnvironmentVariable("CI") is { Length: > 0 } ci
                && !ci.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Fail("the delivery contract needs a POSIX shell and CI is the authoritative run");
            }

            Assert.Skip("the delivery contract needs `sh` on PATH; git for Windows provides one");
            throw;
        }
    }

    /// <summary>The checkout this test binary was built inside.</summary>
    private static string Repository()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src_bugs")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("no src_bugs above the test binary");
    }
}

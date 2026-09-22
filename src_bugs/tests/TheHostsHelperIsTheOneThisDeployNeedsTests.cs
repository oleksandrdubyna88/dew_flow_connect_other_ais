using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// The root helper on the host implements the protocol this checkout is about to speak.
/// </summary>
/// <remarks>
/// <para><b>The failure this exists to end, measured on the real host 2026-09-22.</b>
/// <c>install-env.sh</c> is copied to <c>/usr/local/sbin/coai-bugs-install-env</c> when the host is
/// provisioned and is the ONE file a deploy never updates — a root script living in a directory the
/// deploy account owns would be a way to become root, so it is deliberately not refreshed from the
/// checkout. It gained the administrator record in `c38637e3`; the host's copy predated that by ten
/// hours. The old helper's whole reading of stdin is <c>head -1</c>: it took the secret, discarded
/// the key list without a word, wrote an environment file holding no administrators, printed "the
/// environment file is written" and exited 0.</para>
/// <para>So the deploy's own delivery step went GREEN while delivering nobody, the server started
/// perfectly — <c>AdminKeys.Read</c> treats an absent variable as "no administrators", which is a
/// legitimate way to run it — and every admin call answered 401. It was caught a whole deploy later
/// by <c>admin-check</c>, which is the right net but the wrong distance: by then the build is live
/// and a person is reading a 401 with no idea which of six things caused it.</para>
/// <para><b>Why the check is a file of its own</b>, as <c>first-key.sh</c> is: it has to run on the
/// host from inside <c>deploy-cmd.sh</c>, and it has to be exercised here against a helper that is
/// deliberately wrong. A check spelled inline in the wrapper could only be tested by running the
/// wrapper, which wants root, a sudoers line and an ssh forced command. This file runs THE REAL
/// SCRIPT over the real helper and over helpers that are wrong in each way they are wrong.</para>
/// <para>Every refusal the script makes goes to STDERR, which is why <see cref="ReleaseScript"/>'s
/// harness fits unchanged: it answers with the exit code and what was said there.</para>
/// </remarks>
public sealed class TheHostsHelperIsTheOneThisDeployNeedsTests : IDisposable
{
    /// <summary>The check, by the path the deploy names it by.</summary>
    private const string Check = "deploy/bugs/helper-protocol.sh";

    private readonly string _dir = Directory.CreateTempSubdirectory("coai-helper-protocol-").FullName;

    public void Dispose() => Scratch.Delete(_dir);

    /// <summary>The helper this checkout ships is, by definition, the one it needs.</summary>
    /// <remarks>
    /// This is the assertion that makes the other three mean something. The marker lives in exactly
    /// one place — <c>install-env.sh</c> declares it, <c>helper-protocol.sh</c> demands it — and a
    /// change to either that does not change the other fails HERE, on a developer's machine, rather
    /// than on a host during a deploy.
    /// </remarks>
    [Fact]
    public void TheHelperThisCheckoutShipsIsAccepted()
    {
        var (code, error) = ReleaseScript.FromCheckout(Check, "deploy/bugs/install-env.sh");

        code.Should().Be(0, "the checkout's own helper must satisfy the checkout's own demand: {0}", error);
    }

    /// <summary>
    /// The helper that was actually on the host — the one that silently drops the administrators.
    /// </summary>
    /// <remarks>
    /// Not a stand-in: this is the pre-<c>c38637e3</c> body, reduced to the part that matters. It
    /// reads ONE line, writes two variables, and reports success. Every rule that came after it is
    /// absent, and the only thing that can tell it apart from the current helper before it runs is
    /// that it declares no protocol.
    /// </remarks>
    [Fact]
    public void TheHelperThatWasOnTheHostIsRefusedAndTheRepairIsNamed()
    {
        var stale = Helper("""
            #!/bin/sh
            set -eu
            ENV_FILE=/etc/coai-bugs/env
            SECRET=$(head -c 4096 | head -1 | tr -d '\r\n')
            [ -n "$SECRET" ] || { printf 'no secret arrived on stdin\n' >&2; exit 1; }
            printf 'the environment file is written\n'
            """);

        var (code, error) = ReleaseScript.FromCheckout(Check, stale);

        code.Should().Be(1, "a helper that cannot carry administrators must not be handed any");
        error.Should().Contain("install -m 0755",
            "a refusal that does not carry the repair sends somebody to the README to find it");
        error.Should().Contain("coai-bugs-install-env",
            "and it names the file to replace, because there is more than one script here");
    }

    /// <summary>A helper declaring a DIFFERENT protocol is refused as loudly as one declaring none.</summary>
    /// <remarks>
    /// The version that comes after this one is the case nobody is looking at today, and it is the
    /// case a substring match gets wrong: a check asking whether the file merely MENTIONS the helper
    /// would accept every future protocol, including the one that renames the records again.
    /// </remarks>
    [Fact]
    public void AHelperSpeakingAnotherProtocolIsRefused()
    {
        var other = Helper("""
            #!/bin/sh
            # coai-bugs-install-env protocol 1
            printf 'the environment file is written\n'
            """);

        ReleaseScript.FromCheckout(Check, other).Code.Should()
            .Be(1, "protocol 1 is not the protocol this checkout speaks");
    }

    /// <summary>A host that was never provisioned says that, rather than failing inside sudo.</summary>
    [Fact]
    public void AHelperThatIsNotThereIsSaidToBeMissing()
    {
        var absent = ReleaseScript.Posix(Path.Combine(_dir, "not-installed-at-all"));

        var (code, error) = ReleaseScript.FromCheckout(Check, absent);

        code.Should().Be(1);
        error.Should().Contain("not-installed-at-all", "the path that was looked for is the whole news");
    }

    /// <summary>A file at a scratch path holding the given text, as an installed helper would be.</summary>
    /// <remarks>
    /// <para>Written with LF whatever this checkout's line endings are: the script is read by `grep`
    /// on a host, and a fixture that only matched because the developer's git normalised it would be
    /// testing the checkout rather than the rule.</para>
    /// <para>Answered as a POSIX path, because it becomes an ARGUMENT to `sh` — and git for
    /// Windows' `sh` reads a backslash as its escape character, so `C:\x\y` arrives as `C:xy` and
    /// the script reports the wrong path missing.</para>
    /// </remarks>
    private string Helper(string body)
    {
        var path = Path.Combine(_dir, "coai-bugs-install-env");
        File.WriteAllText(path, body.ReplaceLineEndings("\n"));

        return ReleaseScript.Posix(path);
    }
}

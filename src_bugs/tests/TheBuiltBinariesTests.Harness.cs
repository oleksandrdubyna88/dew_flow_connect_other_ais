using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// HOW the real binaries are run: the launches, the drained streams, the ports and the retry.
/// </summary>
/// <remarks>
/// <para>A partial of <see cref="TheBuiltBinariesTests"/>, split from it when the file passed this
/// repository's 800-line maximum. The line count is the trigger, not the reason: what is here is
/// the machinery for driving two real processes over a real socket, and what is left there is what
/// the scenarios ASSERT. A reader asking what the shipped binary guarantees should not have to
/// scroll past pipe-draining and errno wording to find out.</para>
/// <para>It stays a partial of the test class rather than a helper of its own because every piece
/// is bound to one fixture: the throwaway data directory the class creates and deletes, and the
/// collection attribute that keeps these process-launching tests from running beside each other.</para>
/// </remarks>
public sealed partial class TheBuiltBinariesTests
{
    /// <summary>Asserts the binary is there, rather than skipping when it is not.</summary>
    /// <remarks>
    /// <b>It used to skip, and a skip is indistinguishable from a pass in a summary line.</b> This
    /// project references BOTH binaries, so building the tests always builds them: there is no honest
    /// case where one is missing, and the one case that would produce it — a path computed wrongly for
    /// the platform — is exactly what would make this whole file quietly stop testing anything.
    /// </remarks>
    private static void MustExist(string exe) =>
        File.Exists(exe).Should().BeTrue(
            $"{exe} is referenced by this test project, so a build that produced the tests produced it "
            + "too — if it is not there, the path is wrong and this suite is testing nothing");

    private static string Built(params string[] parts)
    {
        var configuration = AppContext.BaseDirectory.Contains("Release", StringComparison.Ordinal)
            ? "Release"
            : "Debug";
        var name = parts[^1] + (OperatingSystem.IsWindows() ? ".exe" : string.Empty);

        return Path.GetFullPath(Path.Combine(
            [AppContext.BaseDirectory, "..", "..", "..", "..", "..", .. parts[..^1],
             "bin", configuration, "net10.0", name]));
    }

    /// <summary>A client for the real socket, with a bearer credential when there is one.</summary>
    private static HttpClient Talking(int port, string key)
    {
        var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        if (key.Length > 0)
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        return http;
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        return port;
    }

    /// <summary>
    /// An endless supply of ports that were free a moment ago — the strongest claim anything can
    /// make about a port it does not hold.
    /// </summary>
    private static IEnumerable<int> FreePorts()
    {
        while (true)
        {
            yield return FreePort();
        }
    }

    /// <summary>How many candidates may be lost to a race before this is called a failure.</summary>
    private const int Attempts = 10;

    /// <summary>
    /// Whether a server that stopped without listening stopped because its port was taken.
    /// </summary>
    /// <remarks>
    /// <para>Narrow on purpose, and for the reason `LoopbackStub` gives for its own errno list:
    /// retrying on ANY early exit would turn a missing keyword list or an unwritable data directory
    /// into ten attempts and a sentence blaming ports — wrong, and ten times slower to be wrong.
    /// So anything this does not recognise fails immediately, carrying what the server said.</para>
    /// <para>The markers are the two halves of what Kestrel prints: its own sentence, which names
    /// the address, and the platform's, which is where the dialects differ — `address already in
    /// use` on Linux and macOS, and on Windows the `Only one usage of each socket address` wording
    /// of `WSAEADDRINUSE`. <see cref="ARealCollisionIsRecognisedOnThisPlatform"/> provokes a real
    /// one and asserts that whatever this platform prints is in this set, which is how the list is
    /// meant to grow rather than by guessing.</para>
    /// </remarks>
    private static bool LostToARace(string said) =>
        said.Contains("Failed to bind to address", StringComparison.OrdinalIgnoreCase)
        || said.Contains("address already in use", StringComparison.OrdinalIgnoreCase)
        || said.Contains("Only one usage of each socket address", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Waits for the server to actually answer — and stops waiting the moment it has died.
    /// </summary>
    /// <remarks>
    /// It polled for the full fifteen seconds whatever happened, so a server that had already
    /// exited was waited out and then reported as one that "must reach the point of listening".
    /// Watching for the exit is what makes a lost port cheap to retry: Kestrel refuses a taken
    /// address in tens of milliseconds, so the retry costs that rather than fifteen seconds a turn.
    /// </remarks>
    private static async Task<bool> Listening(Running server, int port)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                using var reply = await http.GetAsync(
                    new Uri($"http://127.0.0.1:{port}/health"), TestContext.Current.CancellationToken);
                if (reply.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                // Not up yet, which is the ordinary case for the first few attempts.
            }

            if (server.Process.HasExited)
            {
                return false;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        return false;
    }

    /// <summary>
    /// Starts one, with its streams DRAINED rather than merely redirected.
    /// </summary>
    /// <remarks>
    /// A redirected stream nobody reads is a pipe that fills, and a process whose pipe is full stops
    /// writing and then stops running. The server logs to its console sink on every request, so it
    /// would reach that buffer and hang there — looking exactly like a server that never came up.
    /// </remarks>
    private static Running Start(
        string exe, string args, string data, string key = "", string rate = "",
        string admins = "", string adminRate = "")
    {
        var how = Prepared(exe, args, data, key, rate, admins, adminRate);
        var started = Process.Start(how)!;
        var said = new StringBuilder();

        // Drained on their own threads, because the SERVER is left running: nobody calls
        // `ReadToEnd` on a process that never exits, and an undrained pipe stops it dead.
        //
        // KEPT rather than discarded, which it was. A server that dies during startup says why on
        // stderr — the address already in use, the keyword list missing, the data directory
        // unwritable — and throwing that away left every startup failure looking identical from
        // here: fifteen seconds of polling and then "must reach the point of listening", about a
        // process that had explained itself in the first fifty milliseconds.
        started.OutputDataReceived += (_, line) => Keep(said, line.Data);
        started.ErrorDataReceived += (_, line) => Keep(said, line.Data);
        started.BeginOutputReadLine();
        started.BeginErrorReadLine();

        return new Running(started, said);
    }

    /// <summary>Appends a drained line under the lock the two handler threads share.</summary>
    private static void Keep(StringBuilder said, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (said)
        {
            said.AppendLine(line);
        }
    }

    /// <summary>A started server and everything it has said so far.</summary>
    /// <remarks>
    /// The two travel together because the interesting moment is the one where the process is gone:
    /// a <see cref="Process"/> that has exited still answers <see cref="Process.ExitCode"/> but its
    /// streams are closed, so whatever it said has to have been kept as it was said.
    /// </remarks>
    private sealed record Running(Process Process, StringBuilder Said)
    {
        /// <summary>What it has printed, as one block of text.</summary>
        public string Text
        {
            get
            {
                lock (Said)
                {
                    return Said.ToString();
                }
            }
        }
    }

    /// <summary>The environment both launches share: the secret, the data directory, and what a scenario adds.</summary>
    /// <remarks>
    /// One builder for <see cref="Start"/> and <see cref="Run"/>, which used to carry two copies of
    /// it — a variable added to one and not the other is a scenario testing a differently
    /// configured server than it thinks. <paramref name="rate"/> is the limit setting as the unit
    /// would set it; empty leaves it unset, which is the default the server documents. So do
    /// <paramref name="admins"/> and <paramref name="adminRate"/> — and an empty <c>admins</c> is a
    /// real configuration worth running rather than an omission: it is how a deployment whose secret
    /// was never filled in behaves, which is a scenario below.
    /// </remarks>
    private static ProcessStartInfo Prepared(
        string exe, string args, string data, string key, string rate, string admins = "", string adminRate = "")
    {
        var how = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        foreach (var argument in args.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            how.ArgumentList.Add(argument);
        }

        how.Environment["COAI_BUGS_SECRET"] = Secret;
        how.Environment["COAI_BUGS_DATA"] = data;
        how.Environment["COAI_DATA_DIR"] = data;
        // Never inherited from the machine: a developer's own setting must not decide a scenario.
        how.Environment[RatePerMinute.Variable] = rate.Length > 0 ? rate : null;
        // Base64, exactly as `deploy/bugs/install-env.sh` writes it into /etc/coai-bugs/env: this
        // scenario exists to run the REAL binary with the REAL environment, and an unencoded value
        // would be neither.
        how.Environment[AdminKeys.Variable] = admins.Length > 0 ? Delivery.AdminKeys(admins) : null;
        how.Environment[RatePerMinute.Surface.Administrator.Variable] = adminRate.Length > 0 ? adminRate : null;
        if (key.Length > 0)
        {
            how.Environment["COAI_BUGS_KEY"] = key;
        }

        return how;
    }

    private static void Stop(Running server)
    {
        try
        {
            if (!server.Process.HasExited)
            {
                server.Process.Kill(entireProcessTree: true);
                server.Process.WaitForExit(10_000);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone, which is the outcome this was asking for.
        }

        server.Process.Dispose();
    }
}

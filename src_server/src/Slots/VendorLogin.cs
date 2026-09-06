namespace CoaiServer;

/// <summary>
/// <c>coai-server login &lt;vendor&gt; &lt;slot&gt;</c> — the one interactive path in this product.
/// </summary>
/// <remarks>
/// <para>Run as <c>docker compose exec coai-server login codex a</c>, attached to the operator's
/// terminal, because every one of these CLIs signs in by printing a URL and a code and waiting.
/// Nothing about it is reachable over HTTP: a route that could start an interactive sign-in would be
/// a route that can hang a request thread on human input.</para>
/// <para><b>It takes the same lock a review does, first.</b> A sign-in rewrites the account's
/// credentials; a review running on that account at the same moment is the two-writers race that
/// produces <c>invalid_grant</c> and signs the account out. The lock is what makes those two
/// processes — one inside the server, one from <c>exec</c> — aware of each other at all.</para>
/// </remarks>
public static class VendorLogin
{
    /// <summary>The account is in use, and the wait ran out. Distinct so a script can tell.</summary>
    public const int BusyExitCode = 75;

    /// <summary>The command line was wrong, or the vendor/slot is not configured.</summary>
    public const int UsageExitCode = 64;

    /// <summary>Nobody finished the sign-in before the timeout, and the CLI was stopped.</summary>
    public const int TimedOutExitCode = 73;

    /// <summary>How long a person is given to finish a device sign-in.</summary>
    /// <remarks>
    /// Bounded, because a CLI waiting on a browser nobody opened waits for ever otherwise and the
    /// operator's terminal is held with it. Fifteen minutes is generous for "open a URL and paste a
    /// code"; `Coai:LoginTimeoutSeconds` moves it. (local, code round.)
    /// </remarks>
    public static readonly TimeSpan DefaultSignInTimeout = TimeSpan.FromMinutes(15);

    public static async Task<int> RunAsync(
        string[] args,
        string dataDir,
        TextWriter output,
        TimeSpan? wait = null,
        TimeSpan? signInTimeout = null,
        CancellationToken ct = default)
    {
        if (args is not [_, var vendorId, var slotName, ..])
        {
            await output.WriteLineAsync("usage: coai-server login <vendor> <slot>");

            return UsageExitCode;
        }

        var catalog = new VendorCatalogHost(dataDir).Current;
        if (catalog.Find(vendorId) is not { } vendor)
        {
            await output.WriteLineAsync(
                $"'{vendorId}' is not a configured vendor. "
                + (catalog.Vendors.Count > 0
                    ? $"vendors.json has: {string.Join(", ", catalog.Vendors.Select(v => v.Id))}"
                    // Naming the DIRECTORY, not just the file: the commonest cause of an empty catalog
                    // here is `login` run without Coai__DataDir, so it read a different data directory
                    // from the server's and found nothing. (local, code round.)
                    : $"no vendors are configured — this command read {dataDir}, and the server's "
                        + "Coai:DataDir must be the same directory"));

            return UsageExitCode;
        }

        if (!vendor.Slots.Contains(slotName, StringComparer.OrdinalIgnoreCase))
        {
            await output.WriteLineAsync(
                $"'{vendorId}' has no slot '{slotName}'. It has: {string.Join(", ", vendor.Slots)}");

            return UsageExitCode;
        }

        return await SignInAsync(vendor, slotName, dataDir, output, wait, signInTimeout, ct);
    }

    private static async Task<int> SignInAsync(
        VendorConfig vendor,
        string slotName,
        string dataDir,
        TextWriter output,
        TimeSpan? wait,
        TimeSpan? signInTimeout,
        CancellationToken ct)
    {
        var registry = new SlotRegistry(dataDir, new JsonFileStore());
        var slot = registry.Read(vendor.Id, slotName);

        await output.WriteLineAsync($"taking {vendor.Id}/{slotName}…");
        using var lease = await registry.AcquireAsync(
            slot,
            wait,
            budget => output.WriteLine(
                $"{vendor.Id}/{slotName} is in use by a review — waiting up to {budget.TotalSeconds:0}s…"),
            ct);
        if (lease is null)
        {
            // The bounded wait the plan round asked for, twice. An unbounded one is worse than a
            // refusal: an operator who sees nothing cannot tell a busy account from a hung command,
            // and "it printed nothing for an hour" looks like a broken server.
            await output.WriteLineAsync(
                $"{vendor.Id}/{slotName} is busy — a review is running on it. It was still busy after "
                + $"{(wait ?? SlotRegistry.DefaultWait).TotalSeconds:0}s, so nothing was changed. Try again shortly.");

            return BusyExitCode;
        }

        await output.WriteLineAsync(
            $"running `{SignInCommand(vendor.Runtime)} {string.Join(" ", SignInArguments(vendor.Runtime))}` "
            + "— follow its instructions below.");

        int? exitCode;
        try
        {
            // Attached to this terminal, NOT captured: the CLI prints a URL and a code and waits for
            // the person. Capturing that output is what made the one interactive command in this
            // product the one command that could not be used.
            exitCode = await InteractiveProcess.RunAsync(
                SignInCommand(vendor.Runtime),
                SignInArguments(vendor.Runtime),
                slot.Directory,
                SlotEnvironment.For(vendor.Runtime, slot.Directory, registry.TokenFor(slot, vendor.Runtime)),
                signInTimeout ?? DefaultSignInTimeout,
                ct);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            // The CLI is not installed on this machine. An operator meets this the first time they
            // run login on a fresh box, and a stack trace tells them nothing they can act on.
            await output.WriteLineAsync(
                $"{SignInCommand(vendor.Runtime)} could not be started: {e.Message}. "
                + "Install the vendor CLI on the server first, then run this again.");

            return UsageExitCode;
        }

        return await ReportAsync(exitCode, registry, slot, vendor, slotName, output);
    }

    private static async Task<int> ReportAsync(
        int? exitCode,
        SlotRegistry registry,
        AccountSlot slot,
        VendorConfig vendor,
        string slotName,
        TextWriter output)
    {
        if (exitCode is null)
        {
            await output.WriteLineAsync(
                $"{vendor.Id}/{slotName}: the sign-in did not finish in time and was stopped. "
                + "The account is unchanged and still needs signing in — run this again and complete "
                + "the URL the CLI prints.");

            return TimedOutExitCode;
        }

        if (exitCode == 0)
        {
            registry.MarkSignedIn(slot);
            await output.WriteLineAsync($"{vendor.Id}/{slotName} is signed in.");

            return 0;
        }

        // Left marked needs-sign-in: a sign-in that failed has not fixed anything, and clearing the
        // flag because somebody TRIED would put the account back in rotation to fail every job.
        // The CLI already printed its own reason to this terminal — repeating a captured copy is
        // not possible here and would be noise if it were.
        await output.WriteLineAsync(
            $"{vendor.Id}/{slotName} was NOT signed in (the CLI exited {exitCode}). It is still "
            + "marked as needing sign-in.");

        return exitCode.Value;
    }

    /// <summary>The executable that signs this runtime in.</summary>
    private static string SignInCommand(string runtime) => runtime.ToLowerInvariant() switch
    {
        "antigravity" => "agy",
        var other => other,
    };

    /// <summary>
    /// How each CLI is asked to sign in, on a machine with no browser.
    /// </summary>
    /// <remarks>
    /// codex needs <c>--device-auth</c> explicitly; the others print a URL and a code by themselves
    /// when they cannot open a browser. These were run by hand on the VM before being written here.
    /// </remarks>
    private static IReadOnlyList<string> SignInArguments(string runtime) => runtime.ToLowerInvariant() switch
    {
        "codex" => ["login", "--device-auth"],
        "claude" => ["/login"],
        _ => ["login"],
    };
}

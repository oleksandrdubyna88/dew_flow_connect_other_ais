using System.Text.Json;
using System.Text.RegularExpressions;
using CoaiMcp.Runners.Files;

namespace CoaiMcp.Runners.Kimi;

/// <summary>
/// The sweep that retires the lock files of a sign-in folder that is gone or is another folder now (F15 and DL7 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>, A2 §2.5).
/// </summary>
/// <remarks>
/// <para><b>Why it exists, and why only on Linux.</b> A Linux key is the folder's inode (<see cref="FolderIdentity"/>), and an inode changes whenever the folder is made again, so the
/// <c>&lt;key&gt;.lock</c> and <c>&lt;key&gt;.tree.lock</c> files would accumulate (at most 8 identities are assumed in a lock root's life). A Windows key is the final path and cannot change, and a file held with
/// <see cref="FileShare.None"/> cannot be deleted there — so off Linux the sweep does nothing.</para>
/// <para><b>The rule.</b> A key's two files are removed when the sweep can take BOTH at once without waiting (nobody holds them) AND the folder the gate recorded (<see cref="KimiLockRecord"/>) no longer exists or no longer has
/// that identity. They are unlinked while both are still held; a taker that opened the old file just before is caught by <see cref="HeldFile.TakeVerified(string, TimeSpan, bool)"/>. A key whose survivor marker still stands is kept whole
/// (<see cref="IKimiSurvivors"/>). Anything the sweep cannot read or decide is LEFT and said — never the reason the sweep fails (D20's rule), and the sweep never throws.</para>
/// </remarks>
public static partial class KimiLockRetirement
{
    private const string GateSuffix = ".lock";

    /// <summary>How many sign-in keys were retired from <paramref name="root"/>; each decision, retired or left, is told to <paramref name="note"/>.</summary>
    public static int Sweep(string root, Action<string> note)
    {
        if (!OperatingSystem.IsLinux())
        {
            return 0;
        }

        try
        {
            return Directory.Exists(root) ? Gates(root).Count(gate => Judged(gate, note)) : 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            note($"kimi locks: {root} left — it could not be listed ({e.GetType().Name})");

            return 0;
        }
    }

    /// <summary>The gate files of the lock folder: <c>&lt;16 lower hex&gt;.lock</c> — never a <c>.tree.lock</c>, never anything else. Listed up front, so the folder may change under the sweep.</summary>
    private static List<string> Gates(string root) =>
        [.. Directory.EnumerateFiles(root, "*" + GateSuffix).Where(file => GateName().IsMatch(Path.GetFileName(file)))];

    [GeneratedRegex("^[0-9a-f]{16}\\.lock$", RegexOptions.CultureInvariant)]
    private static partial Regex GateName();

    /// <summary>One key: retired, or left. Whatever goes wrong inside is a note, not a throw.</summary>
    private static bool Judged(string gate, Action<string> note)
    {
        try
        {
            return Held(gate, note);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            note($"kimi locks: {gate} left — {e.GetType().Name}: {e.Message}");

            return false;
        }
    }

    /// <summary>Both files taken without waiting, or the key is left; the verdict is reached under both holds.</summary>
    private static bool Held(string gate, Action<string> note)
    {
        var tree = Sibling(gate, ".tree.lock");
        if (HeldFile.Take(gate) is not Hold.Taken gateHold)
        {
            note($"kimi locks: {gate} left — held or unusable");

            return false;
        }

        using (gateHold.Stream)
        {
            if (HeldFile.Take(tree) is not Hold.Taken treeHold)
            {
                note($"kimi locks: {tree} left — held or unusable");

                return false;
            }

            using (treeHold.Stream)
            {
                return Decided(gate, gateHold.Stream, note);
            }
        }
    }

    /// <summary>The verdict, under both holds: a record that says the folder is gone or another, and no survivor marker standing.</summary>
    private static bool Decided(string gate, FileStream gateStream, Action<string> note)
    {
        if (RecordIn(gateStream) is not [var record])
        {
            note($"kimi locks: {gate} left — no readable record");

            return false;
        }

        var reason = WhyRetired(record);

        return reason.Length > 0 && NoSurvivors(gate, note) && Retired(gate, reason, note);
    }

    /// <summary>The record the gate holds, read from the start of the held stream — none (empty) when there is nothing a record can be read from.</summary>
    private static IReadOnlyList<KimiLockRecord> RecordIn(FileStream stream)
    {
        stream.Position = 0;
        var text = new StreamReader(stream, leaveOpen: true).ReadToEnd();
        try
        {
            return JsonSerializer.Deserialize(text, KimiLockJsonContext.Default.KimiLockRecord) is { Identity.Length: > 0, Folder.Length: > 0 } record ? [record] : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Why the key is no longer wanted — its folder is gone, or has another identity — or empty. A folder that exists and cannot be named now is not evidence of anything.</summary>
    private static string WhyRetired(KimiLockRecord record)
    {
        if (!Directory.Exists(record.Folder))
        {
            return $"its sign-in folder {record.Folder} is gone";
        }

        var now = FolderIdentity.Of(record.Folder);

        return now.Length > 0 && now != record.Identity ? $"its sign-in folder {record.Folder} has another identity now ({record.Identity} → {now})" : string.Empty;
    }

    /// <summary>Whether no survivor marker stands for the key (the A2-A3 seam): a standing one keeps all three files.</summary>
    private static bool NoSurvivors(string gate, Action<string> note)
    {
        var standing = KimiSurvivors.Gate.BlockedAsync(Sibling(gate, ".survivors.json"), CancellationToken.None).GetAwaiter().GetResult();
        if (standing.Length > 0)
        {
            note($"kimi locks: {gate} left — {standing}");
        }

        return standing.Length == 0;
    }

    /// <summary>The three files go while both holds stand — unlinking an open file is ordinary on Linux.</summary>
    private static bool Retired(string gate, string reason, Action<string> note)
    {
        File.Delete(gate);
        File.Delete(Sibling(gate, ".tree.lock"));
        File.Delete(Sibling(gate, ".survivors.json"));
        note($"kimi locks: retired {Path.GetFileNameWithoutExtension(gate)} — {reason}");

        return true;
    }

    /// <summary>The key's other file: the gate's path with its <c>.lock</c> replaced.</summary>
    private static string Sibling(string gate, string suffix) => gate[..^GateSuffix.Length] + suffix;
}

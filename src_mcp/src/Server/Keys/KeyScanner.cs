namespace CoaiMcp.Server.Keys;

/// <summary>
/// <c>--keys scan --path P</c>: which files under <c>P</c> hold the exact text of a pool key (KEY_POOL_SPEC.md section 4). It answers the path and the KEY ID — never the text.
/// </summary>
/// <remarks>
/// <para>The text of every key is read once, under the lock, into <see cref="ClaudeKeySecret"/>s, and the lock is let go before the walk; the walk asks each secret whether it is in a chunk (<see cref="ClaudeKeySecret.IsIn"/>),
/// so this class never holds a string of a key. A file is read in chunks that overlap by the longest key, so a text cut by a chunk edge is still found.</para>
/// <para><b>The key store itself is skipped</b> (<paramref name="excluded"/>): scanning the data folder would otherwise "find" every key in its own file. A link is never followed, and a file that cannot be read is counted,
/// not skipped silently.</para>
/// </remarks>
internal static class KeyScanner
{
    private const int Chunk = 64 * 1024;

    public static KeyResult<KeyScanAnswer> Scan(KeyStoreAccess access, string path)
    {
        if (path.Length == 0 || !(Directory.Exists(path) || File.Exists(path)))
        {
            return KeyResult<KeyScanAnswer>.Refused("--keys scan: the path is not there");
        }

        if (!access.Folder.Exists)
        {
            return KeyResult<KeyScanAnswer>.Of(new KeyScanAnswer(0, 0, []));
        }

        var secrets = access.Run(create: false, session => KeyResult<IReadOnlyList<ClaudeKeySecret>>.Of(
            [.. session.Entries().Select(entry => session.ReadSecret(entry.Record.Id)).OfType<ClaudeKeySecret>()]));
        if (secrets.Value is not { } found)
        {
            return KeyResult<KeyScanAnswer>.Refused(secrets.Refusal, secrets.Code);
        }

        return KeyResult<KeyScanAnswer>.Of(Walk(Path.GetFullPath(path), found, access.Folder.Root));
    }

    /// <summary>Every file under <paramref name="path"/> (or the one file) tested against <paramref name="secrets"/>, except what is under <paramref name="excluded"/>.</summary>
    internal static KeyScanAnswer Walk(string path, IReadOnlyList<ClaudeKeySecret> secrets, string excluded)
    {
        var hits = new List<KeyScanHit>();
        int scanned = 0, unreadable = 0;
        var overlap = secrets.Count == 0 ? 0 : secrets.Max(secret => secret.ByteLength) - 1;
        foreach (var file in Files(path).Where(file => !IsUnder(file, excluded)))
        {
            if (Read(file, secrets, overlap) is { } found)
            {
                scanned++;
                hits.AddRange(found.Select(secret => new KeyScanHit(file, secret.Id)));
            }
            else
            {
                unreadable++;
            }
        }

        return new KeyScanAnswer(scanned, unreadable, hits);
    }

    private static IEnumerable<string> Files(string path) =>
        File.Exists(path)
            ? [path]
            : Directory.EnumerateFiles(path, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint });

    private static bool IsUnder(string file, string folder) =>
        folder.Length > 0 && (file.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal) || file == folder);

    private static void AddFound(List<ClaudeKeySecret> found, IReadOnlyList<ClaudeKeySecret> secrets, ReadOnlySpan<byte> window)
    {
        foreach (var secret in secrets)
        {
            if (!found.Contains(secret) && secret.IsIn(window))
            {
                found.Add(secret);
            }
        }
    }

    /// <summary>The secrets found in the file, or null when it could not be read.</summary>
    private static List<ClaudeKeySecret>? Read(string file, IReadOnlyList<ClaudeKeySecret> secrets, int overlap)
    {
        var found = new List<ClaudeKeySecret>();
        try
        {
            if (new FileInfo(file).Length == 0)
            {
                // Nothing in it to find — and a FIFO or a device reports no length, and a plain open of a FIFO with no writer would wait for ever.
                return found;
            }

            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var window = new byte[Chunk + overlap];
            var kept = 0;
            int read;
            while ((read = stream.Read(window, kept, Chunk)) > 0)
            {
                var length = kept + read;
                AddFound(found, secrets, window.AsSpan(0, length));

                kept = Math.Min(overlap, length);
                window.AsSpan(length - kept, kept).CopyTo(window);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return found;
    }
}

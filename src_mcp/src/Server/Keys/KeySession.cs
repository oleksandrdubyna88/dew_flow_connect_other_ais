using System.Text.Json;

namespace CoaiMcp.Server.Keys;

/// <summary>The exit codes of a key verb: 65 wrong arguments or data (never 64 — this binary KNOWS the mode), 74 the store cannot be used, 75 the pool is busy.</summary>
internal static class KeyExit
{
    public const int Data = 65;
    public const int Io = 74;
    public const int Busy = 75;
}

/// <summary>What a key verb came to: the value, or the sentence and the exit code of the refusal. A refusal is an expected answer, never an exception.</summary>
internal sealed record KeyResult<T>
{
    private KeyResult(T? value, string refusal, int code)
    {
        Value = value;
        Refusal = refusal;
        Code = code;
    }

    public T? Value { get; }

    public string Refusal { get; }

    public int Code { get; }

    public bool Ok => Refusal.Length == 0;

    public static KeyResult<T> Of(T value) => new(value, string.Empty, 0);

    public static KeyResult<T> Refused(string sentence, int code = KeyExit.Data) => new(default, sentence, code);

    /// <summary>Never the value: a <c>KeyResult&lt;string&gt;</c> carries the text of a key from stdin to the pool, and a record prints its members (the judgement of s7 part 1).</summary>
    public override string ToString() => $"KeyResult {{ Ok = {Ok}, Code = {Code} }}";
}

/// <summary>One key as the folder holds it: the metadata and what is true of its files.</summary>
internal sealed record KeyEntry(KeyRecord Record, bool KeyFileThere, string ModeProblem)
{
    /// <summary>What is wrong with its files, or empty.</summary>
    public string Problem => KeyFileThere ? ModeProblem : KeyProblems.Missing;

    public bool Usable(DateTime nowUtc) =>
        Record.State == KeyStates.Enabled && Problem.Length == 0 && !Record.InCooldownAt(nowUtc);
}

/// <summary>
/// The pool of one device, held by ONE call at a time across every process: <c>pool.lock</c> open with <see cref="FileShare.None"/> (KEY_POOL_SPEC.md section 5).
/// </summary>
/// <remarks>
/// <para>The <c>SessionClaim</c> shape: the kernel releases the lock when its holder dies — exited, crashed or killed — so there is no expiry to guess and no stale lock to break. Unlike a claim, it is WAITED for, for
/// <see cref="Wait"/> at most, because a holder keeps it for milliseconds; past that the call is refused as busy and the launch stops as <c>crash</c>.</para>
/// <para>Opening it also finishes what a killed writer left: every <c>*.tmp</c> is deleted (every writer holds this lock, so a temp file here belongs to a dead one), and a <c>.key</c> with no <c>.json</c> — an add that
/// never answered ok — is deleted. A <c>.json</c> with no <c>.key</c> is kept and lists as <c>missing</c>.</para>
/// </remarks>
internal sealed class KeySession : IDisposable
{
    /// <summary>How long a call waits for the lock.</summary>
    internal static readonly TimeSpan Wait = TimeSpan.FromSeconds(2);

    /// <summary>How long a WRITING verb waits (an add, an import, a sync, a change) — it blocks for the lock, a minute at most: a crowd of processes that add at once each get their turn, also on a machine under heavy load (the judgement of s7 part 1, finding 5, a flaky test).</summary>
    internal static readonly TimeSpan AdminWait = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan Retry = TimeSpan.FromMilliseconds(15);

    private readonly FileStream _held;

    private KeySession(KeyFolder folder, FileStream held)
    {
        Folder = folder;
        _held = held;
    }

    public KeyFolder Folder { get; }

    /// <summary>
    /// Takes the pool. <paramref name="create"/>: make the folder (a verb that writes); otherwise the folder must be there. <paramref name="wait"/>: how long to wait for the lock when the caller must not give up at the short window of a pick
    /// (the record that a key was exposed); null is <see cref="AdminWait"/> for a writing verb and <see cref="Wait"/> otherwise.
    /// </summary>
    public static KeyResult<KeySession> Open(KeyFolder folder, bool create, TimeSpan? wait = null)
    {
        if (folder.LinkProblem() is { Length: > 0 } link)
        {
            return KeyResult<KeySession>.Refused(link, KeyExit.Io);
        }

        if (create && folder.Prepare() is { Length: > 0 } unusable)
        {
            return KeyResult<KeySession>.Refused(unusable, KeyExit.Io);
        }

        if (!folder.Exists)
        {
            return KeyResult<KeySession>.Refused("there is no key store on this device yet", KeyExit.Data);
        }

        // A writer (an add, an import, a sync) may wait longer than a launch's pick: a pick is on the way to a launch, a verb is a person or a boot script.
        var (held, refusal) = TakeLock(folder, wait ?? (create ? AdminWait : Wait));
        if (held is null)
        {
            return refusal.Length > 0
                ? KeyResult<KeySession>.Refused(refusal, KeyExit.Io)
                : KeyResult<KeySession>.Refused("the key pool is busy — another call is changing it; try again in a moment", KeyExit.Busy);
        }

        var session = new KeySession(folder, held);
        session.FinishWhatADeadWriterLeft();

        return KeyResult<KeySession>.Of(session);
    }

    private static (FileStream? Held, string Refusal) TakeLock(KeyFolder folder, TimeSpan wait)
    {
        var info = new FileInfo(folder.LockPath);
        if (info.LinkTarget is not null || (info.Exists && (info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0))
        {
            return (null, $"{folder.LockPath} is a link or not a regular file, and the pool is never locked through one — delete it");
        }

        var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try
            {
                var held = new FileStream(folder.LockPath, options);
                if (!held.CanSeek)
                {
                    // A pipe or a device is not a regular file: only a regular file can be the lock.
                    held.Dispose();

                    return (null, $"{folder.LockPath} is not a regular file, and the pool is never locked through one — delete it");
                }

                folder.ShareLock(held);

                return (held, string.Empty);
            }
            catch (IOException) when (clock.Elapsed < wait)
            {
                Thread.Sleep(Retry);
            }
            catch (IOException)
            {
                return (null, string.Empty);
            }
        }
    }

    private void FinishWhatADeadWriterLeft()
    {
        foreach (var temp in Directory.EnumerateFiles(Folder.Root, "*.tmp"))
        {
            KeyFolder.TryDelete(temp);
        }

        foreach (var key in Directory.EnumerateFiles(Folder.Root, "k-*.key"))
        {
            var id = Path.GetFileNameWithoutExtension(key);
            if (KeyIds.IsValid(id) && !File.Exists(Folder.MetaPathOf(id)))
            {
                KeyFolder.TryDelete(key);
            }
        }
    }

    /// <summary>The count of metadata files that could not be read, from the last <see cref="Entries"/>.</summary>
    public int Unreadable { get; private set; }

    /// <summary>Every key, in the ring's order: added time, then id.</summary>
    public IReadOnlyList<KeyEntry> Entries()
    {
        var entries = new List<KeyEntry>();
        var unreadable = 0;
        foreach (var meta in Directory.EnumerateFiles(Folder.Root, "k-*.json"))
        {
            if (ReadRecord(meta) is { } record && record.Id == Path.GetFileNameWithoutExtension(meta))
            {
                var keyFile = Folder.KeyPathOf(record.Id);
                var there = File.Exists(keyFile);
                entries.Add(new KeyEntry(record, there, there ? Folder.ModeProblem(keyFile) : string.Empty));
            }
            else
            {
                unreadable++;
            }
        }

        Unreadable = unreadable;

        return [.. entries.OrderBy(entry => entry.Record.AddedUtc).ThenBy(entry => entry.Record.Id, StringComparer.Ordinal)];
    }

    private static KeyRecord? ReadRecord(string path)
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), KeysJson.Default.KeyRecord)?.Normalised();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public PoolFile Pool()
    {
        try
        {
            return (File.Exists(Folder.PoolPath) ? JsonSerializer.Deserialize(File.ReadAllText(Folder.PoolPath), KeysJson.Default.PoolFile) : null)?.Normalised() ?? new PoolFile();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new PoolFile();
        }
    }

    public string SavePool(PoolFile pool) => Folder.WritePrivate(Folder.PoolPath, JsonSerializer.Serialize(pool, KeysJson.Default.PoolFile));

    /// <summary>Writes the metadata of one key; the mode problem of the written file, or empty.</summary>
    public string SaveRecord(KeyRecord record, bool durable = false) => Folder.WritePrivate(Folder.MetaPathOf(record.Id), JsonSerializer.Serialize(record, KeysJson.Default.KeyRecord), durable);

    /// <summary>Writes the text of one key: only the text, one line.</summary>
    public string SaveToken(string id, string text) => Folder.WritePrivate(Folder.KeyPathOf(id), text + "\n", durable: true);

    /// <summary>
    /// THE member that reads a token out of the store: the secret of the key with <paramref name="id"/>, or null when the id is not an id, the file is not there, has a wider mode than the store makes, or does not hash to
    /// its own id. The only callers are inside this folder; the launch road asks the pool (the pool's resume and pick) and never the session.
    /// </summary>
    internal ClaudeKeySecret? ReadSecret(string id)
    {
        if (!KeyIds.IsValid(id) || !File.Exists(Folder.KeyPathOf(id)) || Folder.ModeProblem(Folder.KeyPathOf(id)).Length > 0)
        {
            return null;
        }

        try
        {
            var text = File.ReadAllText(Folder.KeyPathOf(id)).Trim();

            return KeyIds.Of(text) == id ? new ClaudeKeySecret(id, text) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Deletes the pair, the key file first: a crash between the two leaves a <c>.json</c> with no <c>.key</c>, which lists as missing and is never picked — not a key file with no owner.</summary>
    public void DeletePair(string id)
    {
        KeyFolder.TryDelete(Folder.KeyPathOf(id));
        KeyFolder.TryDelete(Folder.MetaPathOf(id));
    }

    public void Dispose() => _held.Dispose();
}

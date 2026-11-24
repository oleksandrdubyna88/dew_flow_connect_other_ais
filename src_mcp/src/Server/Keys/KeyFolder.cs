using System.Runtime.Versioning;
using System.Text;
using CoaiMcp.Runners.Files;

namespace CoaiMcp.Server.Keys;

/// <summary>What a mode read BACK says — the seam K3 of the spec uses to stand in for a data folder that does not keep modes.</summary>
internal sealed record KeyFolderSeams(Func<string, UnixFileMode> ModeOf)
{
    public static KeyFolderSeams Real { get; } = new(static path => OperatingSystem.IsWindows() ? default : File.GetUnixFileMode(path));
}

/// <summary>
/// The folder of one device's claude keys, <c>&lt;dataDir&gt;/keys/claude</c>, its names and its modes (KEY_POOL_SPEC.md section 3).
/// </summary>
/// <remarks>
/// <para><b>Modes are read back, not assumed.</b> The folders are made 0700 and every file 0600, and each mode is read again after it is set. A data folder on <c>/mnt/c</c> keeps no modes: the
/// read-back differs, and the add is refused with the folder's name and the cure. A key file found with a WIDER mode than the store makes is not used.</para>
/// <para><b>Shared instance.</b> A data folder with the setgid bit (<c>/var/lib/coai</c>, group <c>coai</c>, 2770) is the shared VM instance: the folders are 2770 and the files 0660, because the owner decided that
/// instance shares its CLI logins. Every member of the group can read every key there; the first <c>add</c> on such a folder says so.</para>
/// <para><b>Windows.</b> The owner-only ACL is <see cref="PrivateFolder"/>'s, set on <c>keys</c> and inherited by <c>claude</c> and the files; its own read-back is the check. Not run in the Linux suite.</para>
/// <para><b>A write is a temp file made 0600 and a rename.</b> <c>AtomicJson</c> and <c>AtomicFile</c> create their temp file with the process umask, so a token would sit world-readable for a moment; this writer creates the file
/// private and only then fills it. Every writer holds <c>pool.lock</c>, so the turn <c>AtomicJson</c> takes would only repeat it.</para>
/// </remarks>
internal sealed class KeyFolder(string dataDir, KeyFolderSeams seams)
{
    private const UnixFileMode OwnerFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode OwnerFolder = OwnerFile | UnixFileMode.UserExecute;
    private const UnixFileMode GroupFile = OwnerFile | UnixFileMode.GroupRead | UnixFileMode.GroupWrite;
    private const UnixFileMode GroupFolder = OwnerFolder | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.SetGroup;

    public string DataDir => dataDir;

    /// <summary>The mode of a file as the folder reads it back (the seam a test replaces).</summary>
    public UnixFileMode ModeOfFile(string path) => seams.ModeOf(path);

    public string KeysFolder => Path.Combine(dataDir, "keys");

    public string Root => Path.Combine(KeysFolder, "claude");

    public bool Exists => Directory.Exists(Root);

    public string LockPath => Path.Combine(Root, "pool.lock");

    public string PoolPath => Path.Combine(Root, "pool.json");

    public string KeyPathOf(string id) => Path.Combine(Root, id + ".key");

    public string MetaPathOf(string id) => Path.Combine(Root, id + ".json");

    /// <summary>The shared VM instance: the data folder is group-owned with the setgid bit. Never on Windows.</summary>
    public bool Shared => !OperatingSystem.IsWindows() && Safe(() => (seams.ModeOf(dataDir) & UnixFileMode.SetGroup) != 0);

    private UnixFileMode WantedFile => Shared ? GroupFile : OwnerFile;

    private UnixFileMode WantedFolder => Shared ? GroupFolder : OwnerFolder;

    /// <summary>Makes <c>keys/claude</c> private and reads the modes back. The sentence that says why it cannot be used, or empty.</summary>
    public string Prepare()
    {
        Directory.CreateDirectory(dataDir);

        return OperatingSystem.IsWindows() ? PrepareWindows() : PrepareUnix();
    }

    private string PrepareWindows()
    {
        if (PrivateFolder.Open(dataDir, "keys") is PrivateFolderOutcome.Refused refused)
        {
            return refused.Sentence;
        }

        Directory.CreateDirectory(Root);

        return string.Empty;
    }

    [UnsupportedOSPlatform("windows")]
    private string PrepareUnix()
    {
        foreach (var folder in new[] { KeysFolder, Root })
        {
            var problem = MadePrivateFolder(folder);
            if (problem.Length > 0)
            {
                return problem;
            }
        }

        return string.Empty;
    }

    [UnsupportedOSPlatform("windows")]
    private string MadePrivateFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        if (new DirectoryInfo(folder).LinkTarget is not null)
        {
            return $"{folder} is a link, and the key store is never made through one — delete the link";
        }

        var wanted = WantedFolder;
        if (seams.ModeOf(folder) != wanted)
        {
            TrySetMode(folder, wanted);
        }

        var got = seams.ModeOf(folder);

        return got == wanted ? string.Empty : NotKept(folder, got, wanted);
    }

    /// <summary>A chmod that only the owner can make: another user's refusal is not an error here, the read-back says what the mode is.</summary>
    [UnsupportedOSPlatform("windows")]
    private static void TrySetMode(string path, UnixFileMode mode)
    {
        try
        {
            File.SetUnixFileMode(path, mode);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not the owner: the read-back that follows is the verdict.
        }
    }

    private static string NotKept(string path, UnixFileMode got, UnixFileMode wanted) =>
        $"{path} does not keep file modes (it reads back {Octal(got)}, not {Octal(wanted)}) — keys would be readable by other users; "
        + "put the data folder on the Linux file system (COAI_DATA_DIR), not on a Windows drive under /mnt";

    private static string Octal(UnixFileMode mode) => Convert.ToString((int)mode, 8);

    /// <summary>The refusal an add gives when a file it wrote did not keep its private mode: the folder's name and the cure.</summary>
    public static string NotKeptSentence(string folder) =>
        $"{folder} does not keep file modes — a key file did not keep its private mode, so keys there would be readable by other users; "
        + "put the data folder on the Linux file system (COAI_DATA_DIR), not on a Windows drive under /mnt";

    /// <summary>On a shared instance the lock file must be writable by the group too, or the second user's open is refused. A chmod only the owner can make; best effort.</summary>
    public void ShareLock()
    {
        if (!OperatingSystem.IsWindows() && Shared)
        {
            TrySetMode(LockPath, GroupFile);
        }
    }

    /// <summary>Whether the file has a wider mode than the store makes — empty when it does not (or on Windows, where the ACL is the folder's).</summary>
    public string ModeProblem(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return string.Empty;
        }

        var mode = seams.ModeOf(path);
        var allowed = WantedFile;

        return (mode & ~allowed) == 0 ? string.Empty : KeyProblems.WideMode;
    }

    /// <summary>Writes <paramref name="text"/> whole or not at all, the file private from its first byte; the read-back mode problem, or empty.</summary>
    public string WritePrivate(string path, string text)
    {
        var temp = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            WriteNew(temp, text);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        return ModeProblem(path);
    }

    private void WriteNew(string temp, string text)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerFile;
        }

        using (var stream = new FileStream(temp, options))
        {
            stream.Write(Encoding.UTF8.GetBytes(text));
            stream.Flush(true);
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temp, WantedFile);
        }
    }

    /// <summary>Deletes one file; false when it was not there.</summary>
    public static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool Safe(Func<bool> read)
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}

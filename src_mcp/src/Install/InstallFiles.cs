using System.Text;

namespace CoaiMcp.Install;

/// <summary>
/// The few file operations the installer does itself — its OWN files (the manifest, the lock, the backups) in its own state folder.
/// The clients' files are changed by the clients' own commands, never written here.
/// </summary>
internal static class InstallFiles
{
    private static readonly UTF8Encoding NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>A folder only its owner can enter (0700 off Windows; the Windows state folder is already per user).</summary>
    internal static void PrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>Writes <paramref name="text"/> to <paramref name="path"/> whole or not at all: a temp file beside it, mode 0600, then a rename over it.</summary>
    internal static void WritePrivateAtomically(string path, string text)
    {
        var temp = $"{path}.{Environment.ProcessId}.tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var stream = new FileStream(temp, options))
            {
                stream.Write(NoBom.GetBytes(text));
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>A copy of <paramref name="source"/> as <paramref name="destination"/> with mode 0600, byte for byte.</summary>
    internal static void CopyPrivate(string source, string destination)
    {
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var output = new FileStream(destination, options);
        input.CopyTo(output);
    }

    internal static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temp file that cannot be deleted is left; nothing waits on it.
        }
    }

    /// <summary>The lowercase hex SHA-256 of a file's bytes, or empty for a file that is not there.</summary>
    internal static string Sha256Of(string path)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream));
    }

    /// <summary>
    /// Why a client's file could not be changed — the OS's own words — or empty when it can. A file is tried by opening it for writing (never
    /// truncating); a folder by making and removing one probe file, in the nearest folder that exists (the client makes the rest).
    /// </summary>
    internal static string WriteRefusal(string file)
    {
        try
        {
            if (File.Exists(file))
            {
                using var _ = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            }

            var folder = Path.GetDirectoryName(file) ?? string.Empty;
            while (folder.Length > 0 && !Directory.Exists(folder))
            {
                folder = Path.GetDirectoryName(folder) ?? string.Empty;
            }

            if (folder.Length > 0)
            {
                var probe = Path.Combine(folder, $".coai-probe-{Guid.NewGuid():N}");
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
                {
                }
            }

            return string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"{file} cannot be written: {e.Message}";
        }
    }
}

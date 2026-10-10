using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace CoaiServer;

/// <summary>
/// Reading and writing one small JSON file per record, so that a killed process leaves a whole file
/// or no file — never half of one.
/// </summary>
/// <remarks>
/// <para>This is <see cref="SessionStore"/>'s write path, extracted rather than copied. Story 2.2's
/// plan round raised the same requirement for the slot state (<c>cooldown.json</c>,
/// <c>needs-signin.json</c>): a kill during a write leaves truncated JSON, and on restart the
/// registry either loses a cooldown and immediately reuses an exhausted account, or fails to build
/// the catalog at all. That is the same defect the session store already had solved, so the answer is
/// one implementation with two callers — a second copy would drift, and only one of them would get
/// the next fix.</para>
/// <para><b>Every write is atomic</b> — a UNIQUE temporary name in the same directory, then a rename.
/// Unique rather than <c>path + ".tmp"</c> because two writers for one path are the daily case here
/// (two of a person's windows; a review and a <c>login</c>), and a shared temporary name is two
/// writers on one file — the very race the rename was chosen to avoid.</para>
/// <para><b>A read that fails returns null and REPORTS.</b> An unreadable file and an absent one are
/// the same "nothing here" to a caller and completely different things to an operator, and only the
/// log can tell them apart.</para>
/// </remarks>
public sealed class JsonFileStore(Action<string, Exception>? onFailure = null)
{
    /// <summary>What a read lets OTHERS do to the file while it holds it.</summary>
    /// <remarks>
    /// <para>The default a <c>File.ReadAllText</c> has, named so the fact below has somewhere to live.
    /// <see cref="Write"/> REPLACES by rename, and on Windows a rename over a file somebody holds open
    /// is refused — <c>UnauthorizedAccessException: Access to the path is denied</c> — so a roster
    /// listing in progress (<see cref="SessionStore.Active"/>, once a minute per admin page) can land
    /// under a session's hourly last-used stamp.</para>
    /// <para><b>Widening this to <c>ReadWrite | Delete</c> was measured on 2026-10-09 and does not help</b>:
    /// <c>File.Move(…, overwrite: true)</c> goes through <c>MoveFileEx</c>, which has no POSIX rename
    /// semantics, and the replace is refused whatever share the reader granted. The test that pins
    /// the measurement is <c>JsonFileStoreTests</c>; the fix is on the WRITING side — the one write
    /// that may collide with a reader, the stamp, is non-fatal (<see cref="SessionStore.Validate"/>).</para>
    /// </remarks>
    internal const FileShare ReadShare = FileShare.Read;

    /// <summary>The record in <paramref name="path"/>, or null — absent, unreadable, or not JSON.</summary>
    public T? Read<T>(string path, JsonTypeInfo<T> shape)
        where T : class
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, ReadShare);

            return JsonSerializer.Deserialize(file, shape);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            // Gone between the existence check and the open: revoked or swept by somebody else a
            // moment ago. Absent is absent, and absent was never reported.
            return null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            onFailure?.Invoke($"'{path}' could not be read", e);

            return null;
        }
    }

    /// <summary>Replace <paramref name="path"/> with <paramref name="value"/>, all or nothing.</summary>
    public void Write<T>(string path, T value, JsonTypeInfo<T> shape)
    {
        var directory = Path.GetDirectoryName(path) ?? ".";
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $"{Path.GetFileName(path)}.{Path.GetRandomFileName()}.tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, shape));
        // Move, not copy-then-delete: a reader sees the old file or the new one, never half of one.
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>True when the file is gone — including when it was never there.</summary>
    /// <remarks>
    /// Absent is success: deleting twice is not an error, and a file that does not exist is exactly
    /// as gone as one just removed. Only a filesystem that REFUSED is false.
    /// </remarks>
    public bool Delete(string path)
    {
        try
        {
            File.Delete(path);

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            onFailure?.Invoke($"'{path}' could not be deleted", e);

            return false;
        }
    }
}

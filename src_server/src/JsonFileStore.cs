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
/// <summary>
/// What reading one file produced — four facts that used to be two values, a record or null.
/// </summary>
/// <remarks>
/// <para>The null stood for "absent", "could not be opened" and "not JSON" alike, and
/// <see cref="SessionStore.Sweep"/> read every null as litter and deleted the file. On Windows a file
/// is unreadable for exactly as long as another process holds it — a backup, an editor, this server's
/// own rename of it under a last-used stamp — so a sweep that met one at that moment deleted a LIVE
/// session, and an hourly sweep made that an hourly exposure. Found by the risk consultation on story
/// 2.1 of <c>PLAN_team_usage_by_person.md</c>, 2026-10-10.</para>
/// <para>A closed hierarchy, so a caller that decides anything on the outcome has to say what it does
/// with each case; the one that must never be confused with the others is <see cref="Unreadable"/>,
/// which is a statement about THIS attempt and nothing about the file.</para>
/// </remarks>
public abstract record FileRead<T>
    where T : class
{
    private FileRead()
    {
    }

    /// <summary>No such file — never was, or gone before the open. Not a finding.</summary>
    public sealed record Absent : FileRead<T>;

    /// <summary>
    /// The file is there and could not be read THIS time: held by another process, a permission, a
    /// disk. Not litter — the next attempt may read it whole. Never deleted on this answer.
    /// </summary>
    public sealed record Unreadable(Exception Reason) : FileRead<T>;

    /// <summary>Read whole, and not JSON of this shape — a torn write a killed process left, or not ours. Litter.</summary>
    public sealed record Corrupt(Exception Reason) : FileRead<T>;

    public sealed record Found(T Value) : FileRead<T>;
}

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

    /// <summary>The record in <paramref name="path"/>, or null — absent, unreadable, or not JSON. Reports the last two.</summary>
    /// <remarks>
    /// The convenience over <see cref="TryRead"/> for a caller that only wants a record and does not
    /// DECIDE anything on the difference between the other three. A caller that deletes, or keeps, on
    /// the outcome reads through <see cref="TryRead"/> — see <see cref="FileRead{T}"/> for why.
    /// </remarks>
    public T? Read<T>(string path, JsonTypeInfo<T> shape)
        where T : class =>
        TryRead(path, shape) switch
        {
            FileRead<T>.Found found => found.Value,
            FileRead<T>.Unreadable { Reason: var why } => Reported<T>(path, why),
            FileRead<T>.Corrupt { Reason: var why } => Reported<T>(path, why),
            _ => null,
        };

    /// <summary>What reading <paramref name="path"/> produced — one of four facts, never a null that means three.</summary>
    public FileRead<T> TryRead<T>(string path, JsonTypeInfo<T> shape)
        where T : class
    {
        try
        {
            return Open(path, shape);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            // Gone between the existence check and the open: revoked or swept by somebody else a
            // moment ago. Absent is absent, and absent was never reported.
            return new FileRead<T>.Absent();
        }
        catch (JsonException e)
        {
            return new FileRead<T>.Corrupt(e);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new FileRead<T>.Unreadable(e);
        }
    }

    private static FileRead<T> Open<T>(string path, JsonTypeInfo<T> shape)
        where T : class
    {
        if (!File.Exists(path))
        {
            return new FileRead<T>.Absent();
        }

        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, ReadShare);

        return JsonSerializer.Deserialize(file, shape) is { } value
            ? new FileRead<T>.Found(value)
            : new FileRead<T>.Corrupt(new JsonException("the file holds the JSON literal null, which is not a record"));
    }

    private T? Reported<T>(string path, Exception why)
        where T : class
    {
        onFailure?.Invoke($"'{path}' could not be read", why);

        return null;
    }

    /// <summary>Replace <paramref name="path"/> with <paramref name="value"/>, all or nothing.</summary>
    public void Write<T>(string path, T value, JsonTypeInfo<T> shape)
    {
        var directory = Path.GetDirectoryName(path) ?? ".";
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $"{Path.GetFileName(path)}.{Path.GetRandomFileName()}.tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, shape));
        try
        {
            // Move, not copy-then-delete: a reader sees the old file or the new one, never half of one.
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            // The rename's exception is the one that travels; the temporary does not stay behind for it.
            // A stamp refused every hour (a reader holding the file, on Windows) would otherwise leave one
            // orphan per attempt beside the sessions, for ever. (Risk consultation on story 2.1.)
            Discard(temporary);
            throw;
        }
    }

    private void Discard(string temporary)
    {
        try
        {
            File.Delete(temporary);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            onFailure?.Invoke($"'{temporary}' could not be removed after its rename failed", e);
        }
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

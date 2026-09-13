namespace CoaiMcp.Tests;

/// <summary>
/// A temporary directory that removes itself.
/// </summary>
/// <remarks>
/// <para><c>using var work = TempDir.For("coai-thing-");</c> — and the directory is gone when the
/// scope ends, whether the test passed, failed or threw. Eleven classes in this suite created one
/// and deleted it nowhere at all, which is a leak on every single run; the rest do it by hand in
/// <c>Dispose</c>, which is the same thing written out forty times.</para>
/// <para><b>It never throws on the way out.</b> A delete loses a race whenever something still holds
/// a file open — a spawned CLI, a SQLite handle, a reader mid-poll — and a leftover temp directory
/// is not a failing test. What catches those leftovers is <see cref="TempDirsAreSwept"/>, which runs
/// once per assembly; this only means there are far fewer for it to find.</para>
/// <para><b>Read-only files are cleared first.</b> Git marks every object file read-only and
/// <c>Directory.Delete(recursive: true)</c> refuses one with <c>UnauthorizedAccessException</c> — so
/// a directory that ever held a clone could not be removed at all, which is how 5,476 of them
/// accumulated before anybody looked (see <c>PanelService.DeleteEvenIfReadOnly</c>, which learned
/// this first).</para>
/// </remarks>
public sealed class TempDir : IDisposable
{
    private TempDir(string path) => Path = path;

    /// <summary>Where it is. Also what an implicit conversion to <see cref="string"/> gives you.</summary>
    public string Path { get; }

    /// <summary>A fresh directory under the system temp root, named for the test that wanted it.</summary>
    public static TempDir For(string prefix) =>
        new(Directory.CreateTempSubdirectory(prefix).FullName);

    /// <summary>So it can be passed anywhere a path is taken, without <c>.Path</c> at every call.</summary>
    public static implicit operator string(TempDir directory) => directory.Path;

    /// <summary>A path inside it.</summary>
    public string At(params string[] parts) =>
        System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        try
        {
            Clear(Path);
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // Somebody still holds a file in it, or it is already gone. Neither is a test result.
        }
    }

    private static void Clear(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
        }
    }
}

namespace CoaiMcp.Install;

/// <summary>
/// The one lock of the installer: an exclusive hold on <c>&lt;state&gt;/install.lock</c>, taken without waiting. A second run that finds it held
/// does not queue — it answers 75 (v2 E3.S6, scenario 9) — and a run that dies releases it with its process, so there is no stale lock to clean.
/// </summary>
internal sealed class InstallLock : IDisposable
{
    private readonly FileStream _held;

    private InstallLock(FileStream held) => _held = held;

    /// <summary>The lock, or none when another run holds it.</summary>
    internal static InstallLock? TryTake(InstallHost host)
    {
        InstallFiles.PrivateDirectory(host.StateDir);
        try
        {
            return new InstallLock(new FileStream(host.LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose() => _held.Dispose();
}

namespace CoaiMcp.FakeCli;

/// <summary>
/// Epoch 3 E2 s7: the key pool's lock (<c>pool.lock</c>) held by a CHILD process — a file opened with <see cref="FileShare.None"/>, the shape the product takes it with — so a test can show that a second process
/// waits for it, is refused as busy past two seconds, and gets it again the moment the holder is killed (the kernel frees it). Prints <c>held</c> once it holds it; 75 when it is already held.
/// </summary>
internal static class PoolLockVerb
{
    internal static int Run(string lockFile, int holdMs)
    {
        try
        {
            using var held = new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            Console.Out.WriteLine("held");
            Console.Out.Flush();
            Thread.Sleep(holdMs);
            GC.KeepAlive(held);

            return 0;
        }
        catch (IOException)
        {
            return 75;
        }
    }
}

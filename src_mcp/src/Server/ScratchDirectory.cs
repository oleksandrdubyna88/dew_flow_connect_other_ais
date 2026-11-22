namespace CoaiMcp.Server;

/// <summary>
/// An empty directory for a reviewer that must have nowhere to explore.
/// </summary>
/// <remarks>
/// The plan stage runs here rather than in a checkout. A CLI given a repository reads it; a CLI
/// given an empty folder answers from the message, which is what a plan critique is.
/// </remarks>
public sealed class ScratchDirectory : IDisposable
{
    public ScratchDirectory() => Path = Directory.CreateTempSubdirectory("coai-plan-").FullName;

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException) { /* a straggling handle is not worth failing a completed round over */ }
        catch (UnauthorizedAccessException) { }
    }
}

namespace CoaiMcp.Runners.Files;

/// <summary>
/// A path that names something other than a regular file — a FIFO, a socket, a device, a directory — found when it was opened, so nothing
/// was waited on. The message is a sentence of ours and never names the path.
/// </summary>
public sealed class NotRegularFileException(string reason) : IOException(reason);

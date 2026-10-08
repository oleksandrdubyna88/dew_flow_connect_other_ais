namespace CoaiMcp.Tests;

/// <summary>
/// The one way a test reads a fake CLI launch record (<c>FAKECLI_RECORD_DIR</c>, <c>&lt;pid&gt;-&lt;guid&gt;.argv</c>).
/// </summary>
/// <remarks>
/// <para>Tests list and read these records while launches still run, and on Windows a share mode is enforced:
/// <c>File.ReadAllText</c> lets other READERS in and nothing else, so it is refused by any handle with write or delete
/// access — the child writing the record, the rename that puts it in place, a scanner opening a new file. Tag
/// <c>mcp-v0.44.1</c> lost its <c>coai-mcp (win-x64)</c> job to that twice (2026-10-07). The recorder now makes a record
/// whole or absent; this reader is the other half: it lets every other holder in, so it is never the one refused.</para>
/// <para>Every record read goes through here — see <see cref="ALaunchRecordIsWholeOrAbsentTests"/>, which also scans
/// the suite for a record read that does not.</para>
/// </remarks>
internal static class LaunchRecords
{
    /// <summary>One record's text, decoded as <c>File.ReadAllText</c> would (UTF-8, a byte-order mark honoured).</summary>
    public static string Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}

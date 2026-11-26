namespace CoaiMcp.FakeCli;

/// <summary>
/// The fake CLI as a CODER (epoch 3 E2 s2 t5): the verbs that stand in for a claude that writes. A real claude is never started by a test (it costs money and cannot be made to write a path on demand); what is measured is what the OS layer does to a
/// process that tries.
/// </summary>
internal static class CoderVerbs
{
    /// <summary>
    /// <c>write-files &lt;path&gt;...</c>: tries to write each file, one line on stdout for every one it wrote (<c>wrote &lt;path&gt;</c>) and one on STDERR for every one it could not
    /// (<c>fake-cli: could not write &lt;path&gt;: &lt;the OS's words&gt;</c>), and exits 0 either way — the shape of a coder that goes on after a tool failed and reports its work.
    /// </summary>
    internal static int WriteFiles(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                File.WriteAllText(path, "written by the fake coder\n");
                Console.Out.WriteLine($"wrote {path}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"fake-cli: could not write {path}: {e.Message}");
            }
        }

        return 0;
    }

    /// <summary>
    /// The model map's <c>tryWriteFiles</c>: a JSON array of absolute paths, each tried; ONE line on stderr for every one the OS refused (the same line as <see cref="WriteFiles"/>) and NOTHING on stdout — the vendor answer that follows must stay the only thing there.
    /// </summary>
    internal static void TryWrite(string json)
    {
        foreach (var path in System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? [])
        {
            try
            {
                File.WriteAllText(path, "written by the fake coder\n");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"fake-cli: could not write {path}: {e.Message}");
            }
        }
    }

    /// <summary>
    /// <c>hold-in &lt;folder&gt; &lt;ms&gt;</c>: writes <c>ready</c> into the folder (the EVENT a test waits for, never a sleep), then waits <paramref name="milliseconds"/> and exits 0 — a coder that is working while something else happens to the main checkout.
    /// </summary>
    internal static int HoldIn(string folder, int milliseconds)
    {
        File.WriteAllText(Path.Combine(folder, "ready"), Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Thread.Sleep(milliseconds);

        return 0;
    }
}

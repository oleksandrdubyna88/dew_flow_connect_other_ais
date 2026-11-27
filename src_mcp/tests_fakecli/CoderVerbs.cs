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
    /// <c>set-json-field &lt;file&gt; &lt;field&gt; &lt;value&gt;</c>: what a vendor's own refresh step does to its sign-in file, as a verb — reads the JSON object at <paramref name="file"/>, sets the string at the <c>/</c>-separated path <paramref name="field"/> to
    /// <paramref name="value"/> and writes it back. Exit 0; 1 with a line on stderr that names no value when the file cannot be read as an object.
    /// </summary>
    internal static int SetJsonField(string file, string field, string value)
    {
        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file)) is not System.Text.Json.Nodes.JsonObject root)
            {
                Console.Error.WriteLine("fake-cli: the file is not a JSON object");

                return 1;
            }

            var parts = field.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var at = root;
            foreach (var part in parts[..^1])
            {
                at = at[part] as System.Text.Json.Nodes.JsonObject ?? (System.Text.Json.Nodes.JsonObject)(at[part] = new System.Text.Json.Nodes.JsonObject());
            }

            at[parts[^1]] = value;
            File.WriteAllText(file, root.ToJsonString());

            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"fake-cli: could not set the field ({e.GetType().Name})");

            return 1;
        }
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
    /// The model map's <c>readFiles</c>: a JSON array of absolute paths, each tried for READING from inside the unit the fake runs in. One line for every path in <c>sidecars/&lt;pid&gt;.reads</c> of the record folder — <c>read</c> and the first 80 characters of
    /// the file, <c>absent</c> when it is not there, <c>denied</c> when the OS refused — and NOTHING on stdout or stderr: what the fake sees is a fact a test reads, never a thing the vendor answer must carry.
    /// </summary>
    internal static void TryRead(string json, string recordFolder)
    {
        var lines = new List<string>();
        foreach (var path in System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? [])
        {
            try
            {
                var text = File.ReadAllText(path);
                lines.Add($"{path}\tread\t{text.Replace('\n', ' ')[..Math.Min(text.Length, 80)]}");
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                lines.Add($"{path}\tabsent\t");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                lines.Add($"{path}\tdenied\t");
            }
        }

        var folder = Path.Combine(recordFolder, "sidecars");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".reads"), lines);
    }

    /// <summary>
    /// A steering path that is relative (the model map and the record folder of a fixture repository: <c>.fake/model-map.json</c>) is looked for in the working folder first and then below each folder the launch was given with <c>--add-dir</c>. An agy coder runs from the read-only guard folder and reaches
    /// the worktree through <c>--add-dir</c> only, so without this a path that names the worktree could not be written before the job (whose id names the worktree) exists. A path that is rooted, or that is found nowhere, is answered as it was.
    /// </summary>
    internal static string? ResolveSteeringPath(string[] args, string? value)
    {
        if (string.IsNullOrEmpty(value) || Path.IsPathRooted(value))
        {
            return value;
        }

        var folders = new List<string> { Directory.GetCurrentDirectory() };
        for (var at = 0; at + 1 < args.Length; at++)
        {
            if (args[at] == "--add-dir")
            {
                folders.Add(args[at + 1]);
            }
        }

        foreach (var folder in folders)
        {
            var candidate = Path.Combine(folder, value);
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return value;
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

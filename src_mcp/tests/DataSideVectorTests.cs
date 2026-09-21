using System.Text.Json;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The C# half of the shared data-side vectors — the extension's suite asserts the same file.
/// </summary>
/// <remarks>
/// <para>Two things depend on these two implementations agreeing. The extension WRITES the
/// Team-server token under this directory and this binary's shim READS it, so a divergence is a
/// silent "not signed in". And since the panel began saying which directory it resolved, a
/// divergence is also a panel giving migration advice that this server's own startup log
/// contradicts — the same question answered two ways, in front of the person trying to settle it.
/// </para>
/// <para>Isolated unit tests on each side cannot catch that, because each side is self-consistent.
/// So the vectors live outside both, in <c>shared/data-side-vectors.json</c>, and both suites assert
/// them. Raised by codex and gemini independently on the plan round of the panel half.</para>
/// <para>The NOTES are compared by kind rather than by wording: each surface says it in its own
/// words — a log line and a panel paragraph are not the same sentence — and what must never differ
/// is WHETHER a person is warned at all.</para>
/// </remarks>
public sealed class DataSideVectorTests : IDisposable
{
    private sealed record Vector(
        string Why,
        string DataDir,
        string DataSide,
        string Dir,
        string SettingsPath,
        string LogsPath,
        string ServerNoticesPath,
        bool Refused,
        bool RootHasDatabase,
        bool DirExists,
        string[] Notes);

    private static readonly Vector[] Vectors = Load();

    private static Vector[] Load()
    {
        // tests/bin/<cfg>/net10.0 → the repository root, then the shared folder both sides read.
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "shared", "data-side-vectors.json"));

        using var file = File.OpenRead(path);
        using var parsed = JsonDocument.Parse(file);

        return [.. parsed.RootElement.GetProperty("vectors").EnumerateArray().Select(v => new Vector(
            v.GetProperty("why").GetString() ?? "",
            v.GetProperty("dataDir").GetString() ?? "",
            v.GetProperty("dataSide").GetString() ?? "",
            v.GetProperty("dir").GetString() ?? "",
            v.GetProperty("settingsPath").GetString() ?? "",
            v.GetProperty("logsPath").GetString() ?? "",
            v.GetProperty("serverNoticesPath").GetString() ?? "",
            v.GetProperty("refused").GetBoolean(),
            v.GetProperty("rootHasDatabase").GetBoolean(),
            v.GetProperty("dirExists").GetBoolean(),
            [.. v.GetProperty("notes").EnumerateArray().Select(n => n.GetString() ?? "")]))];
    }

    /// <summary>A real directory, because the notes ask the filesystem and these vectors say what it answers.</summary>
    private readonly string _root = Directory.CreateTempSubdirectory("coai-vectors-").FullName;

    private Func<string, string?> Env(Vector vector) =>
        name => name switch
        {
            "COAI_DATA_DIR" => vector.DataDir.Length == 0 ? null : _root,
            "COAI_DATA_SIDE" => vector.DataSide.Length == 0 ? null : vector.DataSide,
            _ => null,
        };

    /// <summary>The directory a vector means, with its placeholders filled in for this platform.</summary>
    private string Expected(Vector vector) =>
        vector.Dir == "<default>"
            ? PanelSettings.DefaultDataDir
            : Path.Combine(_root, vector.Dir.Replace("<root>", string.Empty).TrimStart('/'));

    public static TheoryData<int> Cases()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Vectors.Length; i++)
        {
            data.Add(i);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryVectorResolvesToTheDirectoryTheExtensionResolves(int index)
    {
        var vector = Vectors[index];
        var resolving = () => PanelSettings.FromEnvironment(Env(vector)).DataDir;

        if (vector.Refused)
        {
            resolving.Should().Throw<InvalidOperationException>(vector.Why).WithMessage("*COAI_DATA_SIDE*");
            return;
        }

        resolving().Should().Be(Expected(vector), vector.Why);
    }

    /// <summary>
    /// The settings file and the log root land where the extension says they do.
    /// </summary>
    /// <remarks>
    /// Added 2026-09-18, when <c>SettingsFile.DataDirFrom</c> stopped being a second resolver. Until
    /// then these two were the only things in the data directory that did NOT move with the side, so
    /// two installations sharing one NAS — the whole reason a side exists — shared one settings file
    /// and overwrote each other in silence. Asserting the DIRECTORY alone could not see it: the
    /// directory was already right.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryVectorPutsTheSettingsFileAndTheLogsWhereTheExtensionPutsThem(int index)
    {
        var vector = Vectors[index];
        if (vector.Refused)
        {
            // Asserted rather than skipped, which the code round was right to name: a fixture that
            // carried a settings path for a REFUSED side would be describing where data goes for a
            // configuration the product will not start on, and the extension suite already asserts
            // this direction. A skip here left the two halves checking different things.
            vector.SettingsPath.Should().BeEmpty(vector.Why);
            vector.LogsPath.Should().BeEmpty(vector.Why);
            return;
        }

        var dir = Expected(vector);

        SettingsFile.PathFor(SettingsFile.DataDirFrom(Env(vector)))
            .Should().Be(Path.Combine(dir, "settings.json"), vector.Why);
        ServiceDefaults.CoaiLogPath.RootFor(SettingsFile.DataDirFrom(Env(vector)))
            .Should().Be(Path.Combine(dir, "logs"), vector.Why);

        // And the vector itself says the same thing, so the fixture cannot drift away from the two
        // implementations it is here to hold together.
        vector.SettingsPath.Should().Be(vector.Dir + "/settings.json");
        vector.LogsPath.Should().Be(vector.Dir + "/logs");
    }

    /// <summary>
    /// The notices file lands where the extension's own resolver says it does.
    /// </summary>
    /// <remarks>
    /// <para>Added with story 1.3 of the server-notices plan. The extension has DERIVED this path
    /// since 2026-09-17 — <c>serverNoticesPath(dataDir)</c> in <c>notificationsFile.ts</c> — and has
    /// had nothing to read, because nothing writes it. The moment the server does, the two halves
    /// have to agree about WHERE, and disagreeing is the silent failure: the server writes, the
    /// extension reads an empty directory, and every surface goes on reporting half the product
    /// exactly as it does today.</para>
    /// <para>One field wider than the pair added on 2026-09-18, and asserted the same way —
    /// refused cases included, where a path would be describing where data goes for a configuration
    /// the product will not start on.</para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryVectorPutsTheNoticesFileWhereTheExtensionPutsIt(int index)
    {
        var vector = Vectors[index];
        if (vector.Refused)
        {
            vector.ServerNoticesPath.Should().BeEmpty(vector.Why);
            return;
        }

        ServerNotices.PathFor(SettingsFile.DataDirFrom(Env(vector)))
            .Should().Be(Path.Combine(Expected(vector), "server-notices.jsonl"), vector.Why);
        vector.ServerNoticesPath.Should().Be(vector.Dir + "/server-notices.jsonl");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryVectorWarnsAboutExactlyWhatTheExtensionWarnsAbout(int index)
    {
        var vector = Vectors[index];
        if (vector.Refused)
        {
            return;
        }

        Arrange(vector);

        var kinds = PanelSettings.StorageNotes(Env(vector))
            .Select(note => note.Contains("coai.db", StringComparison.Ordinal) ? "loose-database" : "new-directory");

        kinds.Should().Equal([.. vector.Notes], vector.Why);
    }

    /// <summary>The filesystem the vector describes, made real — these two notes ask it directly.</summary>
    private void Arrange(Vector vector)
    {
        var database = Path.Combine(_root, "coai.db");
        if (vector.RootHasDatabase)
        {
            File.WriteAllText(database, "not really a database, but it is there");
        }
        else if (File.Exists(database))
        {
            File.Delete(database);
        }

        var dir = Expected(vector);
        if (vector.DirExists)
        {
            Directory.CreateDirectory(dir);
        }
        else if (Directory.Exists(dir) && dir != _root)
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temp directory that outlives one run is litter, not a failed test.
        }
    }
}

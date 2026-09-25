using System.Text.Json;
using CoaiMcp.Core.Outlining;
using CoaiMcp.Normalising;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>coai-mcp --outline &lt;file&gt;</c>: the outliner behind a door a person — and a measurement —
/// can use.
/// </summary>
/// <remarks>
/// <para>A one-shot mode, so the rule in <c>.agents/PROJECT.md</c> binds it: it is selected from
/// <c>args[0]</c> before any transport opens, its stdout is its whole interface, and a binary that
/// KNOWS it never exits 64 — a missing argument is 65. "Not outlined" (a language it does not read, a
/// file too large, a parse failure) is an ANSWER on stdout with exit 0, never an exit code: a person
/// asking about a file deserves the reason, and a script counting files must not lose one.</para>
/// </remarks>
public sealed class TheOutlineModeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-outline-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);

        return path;
    }

    private static (int Code, string Stdout, string Notes) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var notes = new List<string>();
        var code = OutlineMode.Run(args, stdout, notes.Add);

        return (code, stdout.ToString(), string.Join('\n', notes));
    }

    [Fact]
    public void TheModeIsSelectedByItsFlag() =>
        Program.Classify(["--outline", "a.cs"]).Should().Be(Program.Startup.Outline);

    [Fact]
    public void AFileIsOutlinedAsText()
    {
        var file = Write("Cart.cs", "public class Cart\n{\n    public void Add(int x) { secret(x); }\n}\n");

        var (code, stdout, _) = Run("--outline", file);

        code.Should().Be(0);
        stdout.ReplaceLineEndings("\n").Should().Be("public class Cart [1-4]\n  public void Add(int x) [3-3]\n");
    }

    [Fact]
    public void JsonIsOffered_ForAMeasurementToRead()
    {
        var file = Write("lib.rs", "pub fn f(a: i32) -> i32 { a }\n");

        var (code, stdout, _) = Run("--outline", file, "--json");

        code.Should().Be(0);
        using var doc = JsonDocument.Parse(stdout);
        doc.RootElement.GetProperty("language").GetString().Should().Be("Rust");
        doc.RootElement.GetProperty("status").GetString().Should().Be("Outlined");
        var entry = doc.RootElement.GetProperty("entries").EnumerateArray().Single();
        entry.GetProperty("signature").GetString().Should().Be("pub fn f(a: i32) -> i32");
        entry.GetProperty("kind").GetString().Should().Be("fn");
    }

    [Fact]
    public void AnUnsupportedFile_IsAnAnswer_NotAnExitCode()
    {
        var file = Write("notes.md", "# notes\n");

        var (code, stdout, _) = Run("--outline", file);

        code.Should().Be(0);
        stdout.Should().Contain("unsupported (language)");
    }

    /// <summary>The mode checks the SIZE before it reads a byte — a person can point it at a bundle.</summary>
    [Fact]
    public void AFileOverTheCeiling_IsNamedWithItsSize_WithoutBeingRead()
    {
        var file = Path.Combine(_dir, "bundle.js");
        using (var stream = File.Create(file))
        {
            stream.SetLength(OutlineLimits.MaxInputBytes + 10);
        }

        var (code, stdout, _) = Run("--outline", file);

        code.Should().Be(0);
        stdout.Should().Contain("unsupported (too large)").And.Contain((OutlineLimits.MaxInputBytes + 10).ToString());
    }

    [Fact]
    public void NoFile_IsARequestFault_NeverAnOldBinary()
    {
        var (code, _, notes) = Run("--outline");

        code.Should().Be(65, "64 means 'this binary has never heard of --outline'; it has, and the request is wrong");
        notes.Should().Contain("--outline <file>");
    }

    [Fact]
    public void AFileThatIsNotThere_IsNoInput()
    {
        var (code, _, notes) = Run("--outline", Path.Combine(_dir, "missing.cs"));

        code.Should().Be(66);
        notes.Should().Contain("missing.cs");
    }
}

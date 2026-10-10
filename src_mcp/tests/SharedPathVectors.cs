using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>shared/path-family-vectors.json</c>, read CHECKED: every row a JSON object and every field of its declared type,
/// or the test fails naming the section, the row and the field — never an InvalidOperationException from a null row
/// (the third code round). <c>pathFamily.test.ts</c> reads the file the same way. Its own file since the CLI-path vectors
/// (todo/PLAN_paths_per_side.md E1.2) are read by a second class.
/// </summary>
internal static class SharedVectors
{
    public static IReadOnlyList<Row> Rows(string section)
    {
        using var file = File.OpenRead(System.IO.Path.GetFullPath(System.IO.Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "shared", "path-family-vectors.json")));
        using var parsed = System.Text.Json.JsonDocument.Parse(file);
        parsed.RootElement.TryGetProperty(section, out var rows).Should().BeTrue($"the file has a `{section}` section");
        rows.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Array, $"`{section}` is an array");

        return [.. rows.EnumerateArray().Select((row, at) => Row.Of(row.Clone(), $"{section}[{at}]"))];
    }

    public sealed record Row(System.Text.Json.JsonElement Element, string Where)
    {
        public static Row Of(System.Text.Json.JsonElement element, string where)
        {
            element.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Object, $"{where} is a vector row — a JSON object, not {element.ValueKind}");

            return new Row(element, where);
        }

        public string Text(string name)
        {
            var value = Field(name);
            value.ValueKind.Should().Be(System.Text.Json.JsonValueKind.String, $"{Where}.{name} is a string");

            return value.GetString() ?? string.Empty;
        }

        public bool Flag(string name)
        {
            var value = Field(name);
            (value.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
                .Should().BeTrue($"{Where}.{name} is a boolean, not {value.ValueKind}");

            return value.GetBoolean();
        }

        /// <summary>A flag a row may leave out, false when absent — and a boolean when present.</summary>
        public bool OptionalFlag(string name) => Element.TryGetProperty(name, out _) && Flag(name);

        private System.Text.Json.JsonElement Field(string name)
        {
            Element.TryGetProperty(name, out var value).Should().BeTrue($"{Where} has a `{name}`");

            return value;
        }
    }
}

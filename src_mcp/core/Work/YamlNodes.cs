namespace CoaiMcp.Core.Work;

/// <summary>What a plain scalar is. A quoted scalar is always <see cref="String"/>.</summary>
public enum YamlScalarKind
{
    String,
    Integer,
    Boolean,
    Null,
}

/// <summary>A node of a parsed YAML file, with the line it starts on (1-based), so an error can say where.</summary>
public abstract record YamlNode(int Line);

/// <summary>A scalar: its text (unquoted, unescaped) and what it is.</summary>
public sealed record YamlScalar(int Line, string Text, YamlScalarKind Kind) : YamlNode(Line)
{
    /// <summary>The value of a <see cref="YamlScalarKind.Integer"/>; 0 for anything else.</summary>
    public long Integer => Kind == YamlScalarKind.Integer && long.TryParse(Text, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0;

    /// <summary>The value of a <see cref="YamlScalarKind.Boolean"/>; false for anything else.</summary>
    public bool Boolean => Kind == YamlScalarKind.Boolean && Text == "true";
}

/// <summary>One key of a map.</summary>
public sealed record YamlEntry(string Key, int Line, YamlNode Value);

/// <summary>A map in the order the file wrote it. Keys are unique: a second one is a refusal of the reader.</summary>
public sealed record YamlMap(int Line, IReadOnlyList<YamlEntry> Entries) : YamlNode(Line)
{
    /// <summary>The entry of <paramref name="key"/>, or null when the map has none — absent is an answer here, not a fault.</summary>
    public YamlEntry? Find(string key) => Entries.FirstOrDefault(entry => entry.Key == key);
}

/// <summary>A list.</summary>
public sealed record YamlList(int Line, IReadOnlyList<YamlNode> Items) : YamlNode(Line);

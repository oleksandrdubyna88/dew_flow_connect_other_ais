namespace CoaiMcp.Core.Collecting;

/// <summary>The one way a git object id is shortened for a sentence a person reads.</summary>
public static class GitIds
{
    private const int ShortLength = 12;

    /// <summary>The first twelve characters of <paramref name="id"/>, or all of it when it is shorter.</summary>
    public static string Short(string id) => id[..Math.Min(ShortLength, id.Length)];
}

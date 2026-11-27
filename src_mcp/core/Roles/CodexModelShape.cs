namespace CoaiMcp.Core.Roles;

/// <summary>
/// The exact shape of a codex model name, <c>gpt-&lt;version&gt;-&lt;family&gt;</c> (epoch 3 E3 s2 t5; <c>SPEC_CHANGES.md</c> section Q1; ledger entry 2.1): <c>&lt;version&gt;</c> is whole numbers with single dots between them (<c>6</c>, <c>6.1</c>, <c>5.6</c>, <c>6.10</c>) and
/// <c>&lt;family&gt;</c> is one of the families of <c>shared/role-model-names.json</c> (<c>codex.families</c>). Nothing else has the shape: not <c>GPT-6.1-terra</c>, not <c>gpt-x-terra</c>, not <c>gpt-6.1-terra-preview</c>, not a name with a space.
/// </summary>
/// <remarks>
/// <para>It is the one reading of the shape, for the name check before a folder is made (<see cref="RoleModelNames"/>) and for the choice of the newest model of a family at the entry of a job. The shape is right for codex ONLY: the codex CLI refuses a name it does not know before any paid
/// call, and the name must also be in the CLI's own list. The claude CLI bills its default model for a name it does not know, so a claude id is never accepted by its shape.</para>
/// <para>Versions are compared number by number, and a missing number is 0: 7 is newer than 6.2, 6.10 is newer than 6.9, 6.1 is newer than 6.</para>
/// </remarks>
public static class CodexModelShape
{
    /// <summary>The most digits of one number of a version: more is no version of a model, and no number can overflow the comparison.</summary>
    private const int MostDigits = 9;

    /// <summary>The family and the version of <paramref name="name"/> when it has the exact shape with a family of <paramref name="families"/>; false for any other text.</summary>
    public static bool TryParse(string name, IReadOnlyCollection<string> families, out string family, out IReadOnlyList<int> version)
    {
        family = string.Empty;
        version = [];
        var parts = name.Split('-');
        if (parts.Length != 3 || parts[0] != "gpt" || !families.Contains(parts[2], StringComparer.Ordinal) || Numbers(parts[1]) is not { } numbers)
        {
            return false;
        }

        family = parts[2];
        version = numbers;

        return true;
    }

    /// <summary>Whether <paramref name="name"/> has the exact shape with a family of <paramref name="families"/>.</summary>
    public static bool Has(string name, IReadOnlyCollection<string> families) => TryParse(name, families, out _, out _);

    /// <summary>Which of two versions is newer: positive when <paramref name="left"/> is, negative when <paramref name="right"/> is, 0 when they are equal. Number by number; a missing number is 0.</summary>
    public static int Compare(IReadOnlyList<int> left, IReadOnlyList<int> right)
    {
        for (var at = 0; at < Math.Max(left.Count, right.Count); at++)
        {
            var difference = (at < left.Count ? left[at] : 0).CompareTo(at < right.Count ? right[at] : 0);
            if (difference != 0)
            {
                return difference;
            }
        }

        return 0;
    }

    private static List<int>? Numbers(string version)
    {
        var numbers = new List<int>();
        foreach (var piece in version.Split('.'))
        {
            if (piece.Length is 0 or > MostDigits || !piece.All(character => character is >= '0' and <= '9'))
            {
                return null;
            }

            numbers.Add(int.Parse(piece, System.Globalization.CultureInfo.InvariantCulture));
        }

        return numbers;
    }
}

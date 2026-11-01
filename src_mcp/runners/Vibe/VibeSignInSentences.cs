using CoaiMcp.Runners.Files;

namespace CoaiMcp.Runners.Vibe;

/// <summary>
/// The sentences a refused Vibe sign-in file is told in (DL2 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>, A1 spec §1.7, §3.5).
/// </summary>
/// <remarks>
/// <b>Each is a function of the PATH, a line number and a kind — never of a byte of the file</b> (I4). A name a refusal could echo may be
/// the continuation of a quoted VALUE (<c>MISTRAL_API_KEY="dummy</c> + <c>LEAK_CANARY=ending"</c> binds one name), so a sentence names the
/// RULE that was broken and leaves what was found unsaid. The path is the person's own sign-in file, which is a path and not its content.
/// </remarks>
internal static class VibeSignInSentences
{
    private const string Plan = "DL2 of research/PLAN_kimi_and_vibe_on_linux.md";

    /// <summary>The refusal for line <paramref name="line"/> of the sign-in file at <paramref name="path"/> being of <paramref name="kind"/>.</summary>
    public static string Name(string path, int line, DotenvRefusal kind) => kind switch
    {
        DotenvRefusal.NotText =>
            $"the Vibe sign-in file {path} is not text Vibe can use at line {line} (an invalid UTF-8 byte sequence, which python-dotenv cannot decode, or a NUL byte, which python-dotenv reads but Vibe cannot put in its environment); coai does not link it ({Plan})",
        DotenvRefusal.NoBinding =>
            $"line {line} of the Vibe sign-in file {path} does not read as `NAME=value` or a `#` comment — coai links only a file whose every line is a `{DotenvNames.Admitted}=…` binding or a comment ({Plan}); keep only `{DotenvNames.Admitted}=…` in it",
        DotenvRefusal.OtherName =>
            $"line {line} of the Vibe sign-in file {path} binds a variable other than {DotenvNames.Admitted} — a `VIBE_*` or any other variable in that file would configure coai's Vibe launches, so the launch is refused ({Plan}); keep only `{DotenvNames.Admitted}=…` in it",
        _ =>
            $"the Vibe sign-in file {path} starts with a UTF-8 byte-order mark, which python-dotenv reads as part of the first variable's name — save it without one ({Plan})",
    };

    /// <summary>
    /// The refusal for a sign-in file that is not a regular file coai can judge (<paramref name="reason"/> is <see cref="RegularFiles"/>'s
    /// own words), with the cure that reason has. A path and system words only.
    /// </summary>
    public static string Target(string path, string reason) =>
        $"the Vibe sign-in file {path} was not linked ({reason}) — coai links a sign-in only when it is a regular file, owned by the user coai runs as, "
        + $"that only that user can write ({Plan}); {Cure(path, reason)}";

    /// <summary>One cure per kind of reason, asked in order: what the reason says, and the cure it has (as a table, so no method branches per kind).</summary>
    private static readonly (string Says, Func<string, string> Cure)[] Cures =
    [
        ("writable by group or others", path => $"run `chmod 600 {path}`"),
        ("a symbolic link", path => $"make {path} the file itself, not a link — coai never chains links"),
        ("a FIFO", _ => "Vibe itself can read a FIFO sign-in (a 1Password-style environment file), but coai links only a regular file it can judge"),
        ("larger than", _ => $"keep only `{DotenvNames.Admitted}=…` in it"),
    ];

    private static string Cure(string path, string reason) =>
        Cures.FirstOrDefault(known => reason.Contains(known.Says, StringComparison.Ordinal)).Cure?.Invoke(path) ?? "check the file's owner and type";
}

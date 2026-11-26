using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;

namespace CoaiMcp.Runners.Toolchain;

/// <summary>What the real sign-in file was when it was looked at: its sha256 and its modification time. A hash, never text (judgement S3; s7 t6 d4).</summary>
/// <param name="Exists">False when there was no such file.</param>
/// <param name="Sha256">The sha256 of the file's bytes, lower-case hex; empty when there is none.</param>
/// <param name="ModifiedUtc">The modification time; null when there is no file.</param>
internal sealed record RealSignInState(bool Exists, string Sha256, DateTime? ModifiedUtc);

/// <summary>What <see cref="SignInCopy.Make"/> came to: a copy, or the sentence that says why there is none. The sentence never holds a value of the file.</summary>
internal abstract record SignInCopyResult
{
    private SignInCopyResult()
    {
    }

    /// <summary>The copy is in place. <paramref name="Expiry"/> is the access token's expiry, or null when it could not be read.</summary>
    public sealed record Made(DateTime? Expiry) : SignInCopyResult;

    /// <summary>No copy: why.</summary>
    public sealed record Refused(string Sentence) : SignInCopyResult;
}

/// <summary>
/// The sign-in of a vendor that signs in with a file, as a COPY that coai makes for ONE job (epoch 3 E3 s7 t6; judgement S3 and S4, SPEC C2 item 3): never a bind of the real file, because the CLI may rewrite it and a refresh on a read-only bind would rotate the token and lose the new one.
/// The copy holds only what the recipe of the vendor's seam keeps (the access token and its expiry), puts a placeholder where the recipe says (codex: the refresh token, CX-R4) and leaves out everything else (agy: no refresh token at all, AG-R2); it is mode 0600 in a folder of mode 0700 of the job's coder home.
/// </summary>
/// <remarks>
/// <para><b>The real file is opened for reading only</b> (<see cref="Snapshot"/> and <see cref="Make"/> take a shared read handle), and its sha256 and modification time before and after the job go into the envelope.</para>
/// <para><b>No value reaches a sentence, a log or an exception from here</b>: a refusal names a field, a length or a time, never a text. The token texts leave this type only through <see cref="TokensOf"/>, for the one search of the commit (K3), and are held in memory only.</para>
/// </remarks>
internal static class SignInCopy
{
    /// <summary>What stands in the place of a refresh token the coder's unit must not hold (CX-R4): not a token, and no refresh on the copy can rotate anything with it.</summary>
    public const string Placeholder = "coai-placeholder-no-refresh-token";

    /// <summary>The sha256 and the modification time of the real file, read through a shared READ handle; <see cref="RealSignInState"/> with <c>Exists: false</c> when there is none.</summary>
    public static RealSignInState Snapshot(string realFile)
    {
        try
        {
            var info = new FileInfo(realFile);
            if (!info.Exists)
            {
                return new RealSignInState(false, string.Empty, null);
            }

            using var stream = new FileStream(realFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            return new RealSignInState(true, Convert.ToHexStringLower(SHA256.HashData(stream)), info.LastWriteTimeUtc);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new RealSignInState(false, string.Empty, null);
        }
    }

    /// <summary>
    /// Makes the coder's copy at <paramref name="copyFile"/> from <paramref name="sourceFile"/> (the real file, or the file a refresh unit rewrote in its scratch home) by <paramref name="recipe"/>: the kept fields as they are, the placeholders where the recipe says, nothing else.
    /// An existing file at the copy's place is removed first and the copy is made with <c>CreateNew</c>, so nothing is written through a link a coder left there.
    /// </summary>
    public static SignInCopyResult Make(CoderSignInRecipe.CopyForJob recipe, string sourceFile, string copyFile, string home)
    {
        JsonObject? source;
        try
        {
            using var stream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            source = JsonNode.Parse(stream) as JsonObject;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new SignInCopyResult.Refused($"the sign-in file of this vendor could not be read ({e.GetType().Name}), so no copy was made for the job");
        }
        catch (JsonException)
        {
            return new SignInCopyResult.Refused("the sign-in file of this vendor is not JSON that coai can read, so no copy was made for the job");
        }

        if (source is null)
        {
            return new SignInCopyResult.Refused("the sign-in file of this vendor is not a JSON object, so no copy was made for the job");
        }

        var copy = new JsonObject();
        foreach (var field in recipe.KeptFields)
        {
            if (At(source, field) is { } value)
            {
                Put(copy, field, value.DeepClone());
            }
        }

        foreach (var (field, placeholder) in recipe.Placeholders)
        {
            if (At(source, field) is not null)
            {
                Put(copy, field, JsonValue.Create(placeholder));
            }
        }

        var expiry = ExpiryOf(recipe, copy);
        var written = Write(copyFile, copy.ToJsonString(), home);

        return written.Length > 0 ? new SignInCopyResult.Refused(written) : new SignInCopyResult.Made(expiry);
    }

    /// <summary>The expiry of the access token in <paramref name="root"/> by the recipe: the <c>exp</c> claim of a JWT, or an RFC 3339 time; null when the recipe has none or it cannot be read.</summary>
    public static DateTime? ExpiryOf(CoderSignInRecipe.CopyForJob recipe, JsonObject root)
    {
        if (recipe.ExpiryField.Length == 0 || At(root, recipe.ExpiryField) is not JsonValue node || !node.TryGetValue<string>(out var text) || string.IsNullOrEmpty(text))
        {
            return null;
        }

        return recipe.ExpiryIsJwt ? JwtExpiry(text) : DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time) ? time : null;
    }

    /// <summary>The expiry of the copy that lies at <paramref name="copyFile"/>, or null.</summary>
    public static DateTime? ExpiryOfFile(CoderSignInRecipe.CopyForJob recipe, string copyFile)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllBytes(copyFile)) is JsonObject root ? ExpiryOf(recipe, root) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The gate asked before EVERY launch (CX-R1; AG-R2): the access token of the copy must outlive the launch — <c>expiry &gt;= now + runtimeMax + the recipe's margin</c> — or the launch is refused and never started on less. Empty when it does. The sentence names the vendor
    /// and the time, never a token, and tells the person to start the vendor's own CLI once: it refreshes the person's own file.
    /// </summary>
    public static string Gate(CoderSignInRecipe.CopyForJob recipe, DateTime? expiry, DateTime nowUtc, TimeSpan runtimeMax, string vendor)
    {
        if (recipe.ExpiryField.Length == 0)
        {
            return string.Empty;
        }

        var need = runtimeMax + recipe.Margin;
        if (expiry is null)
        {
            return $"the expiry of the {vendor} sign-in could not be read, so no launch is started on a copy that may run out during it; start {vendor} once yourself (its own CLI refreshes your own file), then start the coder again";
        }

        var left = expiry.Value - nowUtc;

        return left >= need
            ? string.Empty
            : $"the {vendor} sign-in has {Math.Max(0, (int)left.TotalMinutes)} minutes left and the launch needs {(int)Math.Ceiling(need.TotalMinutes)} ({(int)Math.Ceiling(runtimeMax.TotalMinutes)} for the unit and {(int)recipe.Margin.TotalMinutes} beyond it), so it was not started on less; start {vendor} once yourself (its own CLI refreshes your own file), then start the coder again";
    }

    /// <summary>The token texts of the copy at <paramref name="copyFile"/> AS THEY ARE NOW (agy rewrites its file during a turn): the recipe's token fields, the placeholder never. Held in memory for the commit search, never written anywhere.</summary>
    public static IReadOnlyList<string> TokensOf(CoderSignInRecipe.CopyForJob recipe, string copyFile)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllBytes(copyFile)) is not JsonObject root)
            {
                return [];
            }

            return [.. recipe.TokenFields.Select(field => At(root, field) is JsonValue node && node.TryGetValue<string>(out var text) ? text : string.Empty).Where(text => text.Length > 0 && text != Placeholder).Distinct(StringComparer.Ordinal)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private static string Write(string copyFile, string text, string home)
    {
        try
        {
            var folder = Path.GetDirectoryName(copyFile)!;

            // The coder writes into its home: a link it made where the folder belongs must not lead the copy out of the home (SPEC E7). The folder is a real directory inside the home, or it is not there yet.
            var kind = RealPath.Lstat(folder).Kind;
            if (kind is not (EntryKind.Missing or EntryKind.Directory))
            {
                return "what stands where the folder of the sign-in copy belongs is not a folder, so the launch was not started";
            }

            if (kind == EntryKind.Directory && !IsInside(home, folder))
            {
                return "the folder of the sign-in copy leads out of the job's home, so the launch was not started";
            }

            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            else
            {
                Directory.CreateDirectory(folder);
            }

            if (RealPath.Lstat(copyFile).Kind is EntryKind.File or EntryKind.Link)
            {
                File.Delete(copyFile);
            }

            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using var stream = new FileStream(copyFile, options);
            stream.Write(Encoding.UTF8.GetBytes(text));

            return string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"the sign-in copy for the job could not be written ({e.GetType().Name}), so the launch was not started";
        }
    }

    private static bool IsInside(string home, string folder)
    {
        var realHome = RealPath.Resolve(home);
        var realFolder = RealPath.Resolve(folder);

        return realHome.Ok && realFolder.Ok && (realFolder.Path == realHome.Path || realFolder.Path.StartsWith(realHome.Path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static JsonNode? At(JsonObject root, string path)
    {
        JsonNode? node = root;
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            node = node is JsonObject map && map.TryGetPropertyValue(part, out var next) ? next : null;
            if (node is null)
            {
                return null;
            }
        }

        return node;
    }

    private static void Put(JsonObject root, string path, JsonNode? value)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var map = root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (map.TryGetPropertyValue(parts[i], out var next) && next is JsonObject nested)
            {
                map = nested;
            }
            else
            {
                var made = new JsonObject();
                map[parts[i]] = made;
                map = made;
            }
        }

        map[parts[^1]] = value;
    }

    private static DateTime? JwtExpiry(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
            var claims = JsonNode.Parse(Convert.FromBase64String(payload)) as JsonObject;

            return claims?["exp"] is JsonValue exp && exp.TryGetValue<long>(out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime : null;
        }
        catch (Exception e) when (e is FormatException or JsonException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}

/// <summary>
/// What the job keeps of ONE vendor's sign-in copy (s7 t6): the state of the real file when the job first looked at it, the token texts of every copy it made (C2 of the pass-3 verify: every vendor binds the same coder home, so a token text that the left vendor's copy held is searched for in every
/// later commit of the job, whether or not its file is still there), and the number of refresh units run. Memory only: nothing here is written anywhere.
/// </summary>
internal sealed class SignInJobState(string vendor, CoderSignInRecipe.CopyForJob recipe, string copyFile, string realFile)
{
    private readonly HashSet<string> _tokens = new(StringComparer.Ordinal);

    public string Vendor => vendor;

    public CoderSignInRecipe.CopyForJob Recipe => recipe;

    /// <summary>Where the coder's copy lies in the job's home.</summary>
    public string CopyFile => copyFile;

    /// <summary>Where the real file is.</summary>
    public string RealFile => realFile;

    /// <summary>The real file as the job first saw it; null until it did.</summary>
    public RealSignInState? Before { get; set; }

    /// <summary>How many refresh units this job ran for the vendor.</summary>
    public int Refreshes { get; set; }

    /// <summary>Remembers the token texts of the copy as it is now (agy rewrites its file during a turn): they stay in the set for the life of the job.</summary>
    public void Remember() => Remember(SignInCopy.TokensOf(recipe, copyFile));

    /// <summary>Remembers token texts that were read elsewhere.</summary>
    public void Remember(IEnumerable<string> tokens)
    {
        foreach (var token in tokens)
        {
            _tokens.Add(token);
        }
    }

    /// <summary>Every token text this job's copy of the vendor ever held, plus what the file holds now.</summary>
    public IReadOnlyList<string> Tokens()
    {
        Remember();

        return [.. _tokens];
    }

    public override string ToString() => $"sign-in of {vendor}";
}

/// <summary>The sign-in states of one job, by vendor.</summary>
internal sealed class SignInJobs
{
    private readonly Dictionary<string, SignInJobState> _states = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <summary>The state of <paramref name="vendor"/>, made by <paramref name="make"/> the first time.</summary>
    public SignInJobState Of(string vendor, Func<SignInJobState> make)
    {
        lock (_gate)
        {
            if (!_states.TryGetValue(vendor, out var state))
            {
                state = make();
                _states[vendor] = state;
            }

            return state;
        }
    }

    /// <summary>Every state, in the order the vendors were first prepared.</summary>
    public IReadOnlyList<SignInJobState> All()
    {
        lock (_gate)
        {
            return [.. _states.Values];
        }
    }
}

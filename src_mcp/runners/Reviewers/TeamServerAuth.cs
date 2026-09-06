using System.Security.Cryptography;
using System.Text;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// Where a Team server's token lives, and the one spelling of a server URL that decides it.
/// </summary>
/// <remarks>
/// <para><b>The URL is normalised before anything is done with it.</b> The panel saves
/// <c>https://coai.example.com/</c>; a vendor row's base URL may be written without the slash. Two
/// hashes of one server read as "not signed in" one second after a successful sign-in, and the person
/// signs in again, and it happens again. Raised on the plan round of the Team-server plan, before any
/// of this existed.</para>
/// <para><b>The same normalised value builds the REQUEST URLs too</b>, not just the hash. A trailing
/// slash left in place turns <c>{url}/api/catalog</c> into <c>…com//api/catalog</c>, which the token
/// still matches — so the failure is a 404 from a server the panel then calls unhealthy. One value,
/// both jobs. (codex, plan round.)</para>
/// <para><b>The token is a FILE, and the file is owner-only.</b> It is never in settings.json, never
/// in argv, never logged — but none of that protects it from another user on the same machine, which
/// is what the mode is for.</para>
/// </remarks>
public static class TeamServerAuth
{
    /// <summary>How much of the hash names the file. 16 hex characters is 64 bits.</summary>
    /// <remarks>
    /// Long enough that two servers cannot collide in any real deployment, short enough that the
    /// directory is readable by a person looking for the one they just signed into.
    /// </remarks>
    public const int HashLength = 16;

    /// <summary>
    /// One canonical spelling of a server URL.
    /// </summary>
    /// <remarks>
    /// Scheme and host lower-cased, a DEFAULT port dropped (443 for https, 80 for http) and a
    /// non-default one kept, every trailing slash removed, the path preserved because a server may
    /// legitimately live under one. An input that is not a URL comes back trimmed and lower-cased
    /// rather than throwing: this runs while building a command line, and a bad URL should fail at
    /// the request with a sentence, not here with a stack trace.
    /// </remarks>
    public static string Normalise(string? url)
    {
        var text = (url ?? string.Empty).Trim();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed))
        {
            return text.TrimEnd('/').ToLowerInvariant();
        }

        var port = parsed.IsDefaultPort ? string.Empty : $":{parsed.Port}";
        var path = parsed.AbsolutePath.TrimEnd('/');

        return $"{parsed.Scheme.ToLowerInvariant()}://{parsed.Host.ToLowerInvariant()}{port}{path}";
    }

    /// <summary>The file this server's token is kept in.</summary>
    public static string TokenPath(string dataDir, string serverUrl) =>
        Path.Combine(dataDir, "servers", Fingerprint(serverUrl) + ".token");

    /// <summary>The short hash that names the file. Shared with the extension, vector for vector.</summary>
    public static string Fingerprint(string serverUrl) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalise(serverUrl))))
            .ToLowerInvariant()[..HashLength];

    /// <summary>An absolute URL under this server, whatever slashes the caller wrote.</summary>
    public static string Endpoint(string serverUrl, string route) =>
        Normalise(serverUrl) + "/" + route.TrimStart('/');

    /// <summary>The token, or empty when this machine has not signed into that server.</summary>
    /// <remarks>
    /// Empty rather than an exception: "not signed in" is an ordinary state with its own sentence and
    /// its own exit code, not a fault.
    /// </remarks>
    public static string ReadToken(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Write the token, readable by its owner and nobody else.
    /// </summary>
    /// <returns>
    /// Empty when the file is owner-only. Otherwise a sentence saying it is NOT — the caller must show
    /// it, because a bearer token another local user can read is a login they can take.
    /// </returns>
    /// <remarks>
    /// <para>A bearer token on a shared machine is worth exactly as much to the next user as to this
    /// one, and keeping it out of settings, argv and logs does nothing about the file itself.</para>
    /// <para><b>The mode is VERIFIED, not merely requested.</b> It used to be set inside a swallowed
    /// try, so on a filesystem that ignores modes — a CIFS mount, a container volume owned by another
    /// uid — sign-in reported success over a world-readable token. Three reviewers raised it, two as
    /// blocking. Signing in still SUCCEEDS there, because refusing would strand anybody whose home is
    /// on such a mount; what changed is that it can no longer do so silently.</para>
    /// </remarks>
    public static string WriteToken(string path, string token)
    {
        var directory = Path.GetDirectoryName(path) ?? ".";
        Directory.CreateDirectory(directory);
        Restrict(directory);
        File.WriteAllText(path, token.Trim());
        Restrict(path);

        return IsOwnerOnly(path)
            ? string.Empty
            : $"the token file {path} could not be made readable only by you — anybody with an "
                + "account on this machine can read it and sign in to the Team server as you";
    }

    /// <summary>Is the file actually owner-only, whatever the write attempted?</summary>
    /// <remarks>
    /// Windows is reported as owner-only: the Unix mode API does not apply there, a user profile
    /// directory is already ACL-protected, and answering "no" would print a warning on every Windows
    /// machine that nobody could act on.
    /// </remarks>
    public static bool IsOwnerOnly(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            var group = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute;
            var other = UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

            return (File.GetUnixFileMode(path) & (group | other)) == UnixFileMode.None;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Cannot be read, so cannot be claimed to be safe.
            return false;
        }
    }

    private static void Restrict(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var mode = Directory.Exists(path)
                ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                : UnixFileMode.UserRead | UnixFileMode.UserWrite;
            File.SetUnixFileMode(path, mode);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Not swallowed any more — WriteToken verifies the result and tells the caller. This
            // catch only keeps a filesystem that refuses modes from throwing out of a sign-in.
        }
    }
}

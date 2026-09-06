using System.Security.Cryptography;
using System.Text.Json;

namespace CoaiServer;

/// <summary>What the server currently believes its vendors are.</summary>
/// <param name="Vendors">The entries that loaded and validated. Never null; empty is a real state.</param>
/// <param name="Error">
/// Empty when the file on disk is what is being served. Otherwise the reason the file was refused —
/// and the vendors above are the LAST GOOD ones, not what the file says.
/// </param>
/// <remarks>
/// The error travels with the catalog instead of only into the log, because keeping the previous
/// configuration when an edit is bad means the file and the behaviour disagree. That is the right
/// trade — a typo at 2 a.m. must not empty the allowlist and take every review down — but it is only
/// safe if the disagreement is VISIBLE. The operator sees this sentence on the catalog they are
/// already looking at; a log line on a box they would have to SSH into is not the same thing.
/// (Plan round, two reviewers.)
/// </remarks>
public sealed record VendorCatalog(IReadOnlyList<VendorConfig> Vendors, string Error)
{
    public static VendorCatalog Empty { get; } = new([], string.Empty);

    public VendorConfig? Find(string id) =>
        Vendors.FirstOrDefault(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when this vendor exists AND allows this model.</summary>
    public bool Allows(string vendorId, string model) =>
        Find(vendorId) is { } vendor
        && vendor.Models.Any(m => string.Equals(m, model, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// <c>vendors.json</c> as the operator edits it, re-read when it actually changes.
/// </summary>
/// <remarks>
/// <para>The same shape as the panel's settings host (<c>src_mcp/src/Server/PanelServiceHost.cs</c>):
/// a token computed cheaply on each access, a rebuild only when it moved, all under one lock. Not
/// a <see cref="FileSystemWatcher"/> — a watcher is a background thread, a queue and a set of
/// platform quirks to own, for a file that is read a few times a minute.</para>
/// <para><b>The token is a content hash, not the timestamp.</b> The plan round pointed out that a
/// replacement of the same size within the filesystem's timestamp resolution leaves length and write
/// time unchanged, so a metadata stamp would keep serving the old allowlist while promising hot
/// reload — and an operator watching nothing happen would conclude the reload is broken rather than
/// that they hit a resolution edge. The file is a few hundred bytes; hashing it costs less than the
/// two stat calls a metadata stamp would need.</para>
/// </remarks>
public sealed class VendorCatalogHost
{
    private readonly string _path;
    private readonly Action<string> _log;
    private readonly Lock _gate = new();
    private string _token = string.Empty;
    private VendorCatalog _current = VendorCatalog.Empty;

    public VendorCatalogHost(string dataDir, Action<string>? log = null)
    {
        _path = Path.Combine(dataDir, "vendors.json");
        _log = log ?? (_ => { });
        var (token, catalog) = Load(VendorCatalog.Empty);
        _token = token;
        _current = catalog;
    }

    /// <summary>The file this host reads, so a message can name the thing to create.</summary>
    public string FilePath => _path;

    /// <summary>The catalog to answer this call with — re-read only when the file actually moved.</summary>
    public VendorCatalog Current
    {
        get
        {
            lock (_gate)
            {
                var token = TokenOf(_path);
                if (token == _token)
                {
                    return _current;
                }

                var (newToken, catalog) = Load(_current);
                _token = newToken;
                _current = catalog;

                return _current;
            }
        }
    }

    /// <summary>
    /// The identity of the file's CONTENT, or empty when there is no file.
    /// </summary>
    /// <remarks>
    /// A read failure returns a token that cannot equal the previous one, so the next access tries
    /// again rather than caching a transient error as the steady state — a file being written by the
    /// operator's editor is exactly when this races.
    /// </remarks>
    private static string TokenOf(string path)
    {
        try
        {
            return File.Exists(path)
                ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
                : string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Unrepeatable();
        }
    }

    /// <summary>Read the file ONCE, and report the identity of exactly the bytes that were parsed.</summary>
    /// <remarks>
    /// Hashing the file in one call and parsing it in another would let the operator's save land
    /// between them: the host would then remember a token for content it never loaded, and the edit
    /// would be invisible until the NEXT one. One read, one hash of those bytes.
    /// </remarks>
    /// <summary>The bytes without a UTF-8 byte-order mark, which the JSON reader refuses.</summary>
    /// <remarks>
    /// This file is edited by hand on a server, and several editors write a BOM by default — Windows
    /// Notepad among them. <c>JsonSerializer</c> does not skip one, so the operator's reward for using
    /// the wrong editor was <c>'0xEF' is an invalid start of a value</c>, a message that says nothing
    /// about what to do. Found by this story's own catalog tests, which wrote the file the way a person
    /// would.
    /// </remarks>
    private static ReadOnlySpan<byte> WithoutBom(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith(Bom) ? bytes[Bom.Length..] : bytes;

    private static ReadOnlySpan<byte> Bom => [0xEF, 0xBB, 0xBF];

    /// <summary>A token that cannot equal any other, so the next access retries instead of caching a failure.</summary>
    private static string Unrepeatable() => "unreadable:" + Guid.NewGuid().ToString("N");

    private (string Token, VendorCatalog Catalog) Load(VendorCatalog previous)
    {
        byte[] bytes;
        try
        {
            if (!File.Exists(_path))
            {
                // Absent is not an error. A fresh install has no vendors until somebody writes the
                // file, and the catalog names the file to write rather than reporting a fault.
                return (string.Empty, VendorCatalog.Empty);
            }

            bytes = File.ReadAllBytes(_path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (Unrepeatable(), Refuse(previous, $"{Path.GetFileName(_path)} could not be read: {e.Message}"));
        }

        var token = Convert.ToHexString(SHA256.HashData(bytes));
        try
        {
            var entries = JsonSerializer.Deserialize(WithoutBom(bytes), ServerJsonContext.Default.VendorConfigArray) ?? [];

            return (token, Validate(entries, previous));
        }
        catch (JsonException e)
        {
            return (token, Refuse(previous, $"{Path.GetFileName(_path)} is not valid JSON: {e.Message}"));
        }
    }
    private VendorCatalog Validate(VendorConfig[] entries, VendorCatalog previous)
    {
        var refusal = FirstRefusal(entries);

        return refusal.Length > 0 ? Refuse(previous, refusal) : Accept(entries);
    }

    private static string FirstRefusal(VendorConfig[] entries)
    {
        var duplicate = entries
            .GroupBy(v => v.Id ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        return duplicate is not null
            ? $"vendor id '{duplicate.Key}' appears {duplicate.Count()} times — ids are how a client "
                + "names a vendor, so they must be unique"
            : entries.Select(e => e.Refusal()).FirstOrDefault(r => r.Length > 0) ?? string.Empty;
    }

    private VendorCatalog Accept(VendorConfig[] entries)
    {
        _log($"vendors.json loaded — {entries.Length} vendor(s): {string.Join(", ", entries.Select(v => v.Id))}");

        return new VendorCatalog(entries, string.Empty);
    }

    private VendorCatalog Refuse(VendorCatalog previous, string reason)
    {
        var kept = previous.Vendors.Count > 0
            ? $" — keeping the {previous.Vendors.Count} vendor(s) already loaded"
            : " — and nothing was loaded before it, so no vendor is available";
        _log($"vendors.json REFUSED: {reason}{kept}");

        return previous with { Error = reason };
    }
}

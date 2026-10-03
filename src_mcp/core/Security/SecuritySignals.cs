using System.Text.RegularExpressions;
using CoaiMcp.Core.Context;
using CoaiMcp.Core.Feature;
using CoaiMcp.Core.Notices;

namespace CoaiMcp.Core.Security;

public sealed record SecurityFile(FileDiff Diff, IReadOnlyList<string> Signals)
{
    public bool DetectionIncomplete { get; init; }
    public bool SupportingMaterial => SecuritySignals.IsSupportingMaterial(Diff.Path);
}

/// <summary>Bounded lexical routing, including removed checks. These matches are not vulnerabilities.</summary>
public static class SecuritySignals
{
    public const int MaxFiles = 512;
    public const int MaxFileCharacters = 262144;
    private static readonly string[] SupportingFolders = ["docs", "research", "todo", "test", "tests", "__tests__", "fixtures"];

    // A ranking hint, never a reason to withhold code or declare a path safe.
    public static bool IsSupportingMaterial(string path) =>
        IsProse(path)
        || path.Replace('\\', '/').Split('/').Any(part => SupportingFolders.Contains(part, StringComparer.OrdinalIgnoreCase));

    // Prose remains available as supporting context, but cannot alone assert an application surface.
    private static bool IsProse(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".md" or ".markdown" or ".rst";

    // Ordinary words such as "where" and "update", and querySelector/executeCommand, are not SQL.
    // Keep common query calls and statement shapes, including removed code and configuration strings.
    private static readonly Regex SqlCode = new(
        @"\b(?:(?:query(?:first|single|multiple)?(?:ordefault)?|execute(?:reader|nonquery|scalar)?)(?:async)?\s*(?:<[^>\r\n]{1,160}>)?\s*\(|select\s+[\s\S]{1,256}\s+from\b|insert\s+into\b|update\s+\S+\s+set\b|delete\s+from\b|(?:create|alter|drop)\s+table\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromSeconds(1));

    private static readonly IReadOnlyDictionary<string, string[]> Terms = new Dictionary<string, string[]>
    {
        ["sql"] = ["sql", "dbcontext", "dbconnection", "dbcommand", "migrationbuilder", "dapper"],
        ["auth-token"] = ["jwt", "bearer", "cookie", "session", "authenticate", "oauth", "openid", "oidc", "pkce", "tokenvalidationparameters", "validateissuersigningkey", "redirect_uri", "client_secret"],
        ["oauth"] = ["oauth", "openid", "oidc", "pkce", "redirect_uri", "redirecturi", "code_verifier", "code_challenge", "authorization_code", "acquiretoken", "msal"],
        ["authz"] = ["authorize", "authorization", "permission", "tenant", "role", "allowanonymous", "mapget", "mappost", "mapput", "mapdelete", "httppost", "httpget", "controller", "route(", "user.claims", "companyid", "frombody", "dbcontext.update", ".updateasync", "patch", "endpoint"],
        ["xss"] = ["innerhtml", "outerhtml", "dangerouslysetinnerhtml", "document.write", "webview", "<script", "markupstring", "htmlstring", "response.writeasync", "v-html"],
        ["ssrf"] = ["httpclient", "httprequestmessage", "fetch(", "axios", "webrequest", "restsharp", "requests.get", "urllib", "redirect", "url"],
        ["path"] = ["path.", "file.", "directory.", "readfile", "writefile", "extract", "archive"],
        ["upload"] = ["iformfile", "multipart", "uploadedfile", "uploadfile", "fileupload", "multer", "formdata", "request.files", "request.form.files"],
        ["command"] = ["process", "exec(", "execfile", "spawn(", "shell", "subprocess", "cmd.exe", "/bin/sh"],
        ["deserialize"] = ["deserialize", "pickle", "yaml.load", "binaryformatter", "json.parse", "typenamehandling", "dtdprocessing", "type.gettype"],
        ["secrets"] = ["secret", "password", "credential", "apikey", "api_key", "connectionstring", "ilogger", "loginformation", "logerror", "bearer"],
        ["crypto"] = ["encrypt", "decrypt", "sha1", "md5", "random", "cryptograph", "cipher"],
        ["concurrency"] = ["stripe", "paymentintent", "rowversion", "dbupdateconcurrencyexception", "balance", "credit", "transactionscope", "semaphoreslim", "lock (", "lock("],
        ["webhooks"] = ["webhook", "stripe-signature", "x-hub-signature", "hmacsha256", "fixedtimeequals", "crypto.createhmac", "timestamp"],
        ["prompt-injection"] = ["ichatclient", "kernel", "openaiclient", "anthropic", "tooldefinition", "system_prompt", "user_input"],
        ["entry-point"] = ["mapget", "mappost", "controller", "endpoint", "handler", "route", "main("],
    };

    public static IReadOnlyList<SecurityFile> Classify(IReadOnlyList<FileDiff> files) =>
        [.. files.Take(MaxFiles).OrderBy(f => f.Path, StringComparer.Ordinal).Select(Classify)];

    private static SecurityFile Classify(FileDiff file)
    {
        var withheld = file.IsBinary || CredentialFiles.LooksLikeOne(file.Path);
        var raw = file.Text.Length <= MaxFileCharacters ? file.Text : string.Empty;
        var safe = withheld ? string.Empty : Redaction.SafeSource(raw);
        var text = IsProse(file.Path) ? string.Empty : file.Path + "\n" + safe;
        return new(file with { Text = safe }, [.. Terms.Where(pair => Matches(pair, text)).Select(pair => pair.Key)])
        { DetectionIncomplete = !withheld && file.Text.Length > MaxFileCharacters };
    }

    private static bool Matches(KeyValuePair<string, string[]> group, string text) =>
        group.Value.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase))
        || (group.Key == "sql" && SqlCode.IsMatch(text));

    /// <summary>
    /// The one order a slice reads files in: production and configuration ahead of documentation and
    /// tests, then by how many of the focus signals a file carries. Shared by the sixteen-file source
    /// reader and the context composer, so the files given source are the ones the pack puts first.
    /// </summary>
    public static IEnumerable<SecurityFile> Rank(IEnumerable<SecurityFile> files, IEnumerable<string> focus)
    {
        var wanted = focus.ToHashSet(StringComparer.Ordinal);
        return files.OrderBy(f => f.SupportingMaterial).ThenByDescending(f => f.Signals.Count(wanted.Contains));
    }

    public static bool Triggered(SecurityPrompt prompt, IReadOnlyList<SecurityFile> files) =>
        prompt.Triggers.Count == 0 ? !SecurityCatalog.IsPreset(prompt.Id)
            : files.Any(f => f.Signals.Intersect(prompt.Triggers).Any());
}

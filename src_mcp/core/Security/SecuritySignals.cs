using CoaiMcp.Core.Context;
using CoaiMcp.Core.Feature;
using CoaiMcp.Core.Notices;

namespace CoaiMcp.Core.Security;

public sealed record SecurityFile(FileDiff Diff, IReadOnlyList<string> Signals)
{
    public bool DetectionIncomplete { get; init; }
}

/// <summary>Bounded lexical routing, including removed checks. These matches are not vulnerabilities.</summary>
public static class SecuritySignals
{
    public const int MaxFiles = 512;
    public const int MaxFileCharacters = 262144;
    private static readonly IReadOnlyDictionary<string, string[]> Terms = new Dictionary<string, string[]>
    {
        ["sql"] = ["sql", "query", "execute", "database", "dbcontext", "migration", "dapper", "select ", "insert ", "update ", "delete ", "where "],
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
        var text = file.Path + "\n" + safe;
        return new(file with { Text = safe }, [.. Terms.Where(pair => pair.Value.Any(term =>
            text.Contains(term, StringComparison.OrdinalIgnoreCase))).Select(pair => pair.Key)])
        { DetectionIncomplete = !withheld && file.Text.Length > MaxFileCharacters };
    }

    public static bool Triggered(SecurityPrompt prompt, IReadOnlyList<SecurityFile> files) =>
        prompt.Triggers.Count == 0 ? !SecurityCatalog.IsPreset(prompt.Id)
            : files.Any(f => f.Signals.Intersect(prompt.Triggers).Any());
}

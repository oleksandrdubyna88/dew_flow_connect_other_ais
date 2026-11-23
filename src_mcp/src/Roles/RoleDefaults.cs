using System.Text.Json;

namespace CoaiMcp.Roles;

/// <summary>One main-AI vendor's shipped table.</summary>
internal sealed record VendorDefaults(
    string Cli,
    IReadOnlyList<(string Role, IReadOnlyList<DefaultModel> Models)> Roles,
    IReadOnlyList<Band> Bands,
    string Note);

/// <summary>A shipped model; <see cref="WhenVendorRow"/> is the runtime a configured reviewer row must have for it to apply, or empty.</summary>
internal sealed record DefaultModel(string Model, string Vendor, string WhenVendorRow);

/// <summary>
/// <c>shared/role-defaults.json</c>, embedded: the roles' defaults per main-AI vendor (server.md §2, §20). Read once; an embedded file that does not parse is a build fault, so it throws.
/// </summary>
internal sealed class RoleDefaults
{
    internal const string SeedResource = "CoaiMcp.role-defaults.json";

    /// <summary>The roles, in the order the answer lists them.</summary>
    internal static readonly IReadOnlyList<string> RoleNames = ["orchestrator", "coder", "scout", "important", "ba"];

    private static readonly Lazy<RoleDefaults> Shipped = new(() => Parse(ReadSeed()));

    internal static RoleDefaults Current => Shipped.Value;

    internal IReadOnlyDictionary<string, VendorDefaults> MainAi { get; }

    internal IReadOnlyList<string> HaikuKinds { get; }

    internal WorkMethod WorkMethod { get; }

    private RoleDefaults(IReadOnlyDictionary<string, VendorDefaults> mainAi, IReadOnlyList<string> haikuKinds, WorkMethod workMethod)
    {
        MainAi = mainAi;
        HaikuKinds = haikuKinds;
        WorkMethod = workMethod;
    }

    private static string ReadSeed()
    {
        using var stream = typeof(RoleDefaults).Assembly.GetManifestResourceStream(SeedResource)
            ?? throw new InvalidOperationException($"the embedded {SeedResource} is missing — the build did not embed shared/role-defaults.json");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    /// <summary>The defaults of <paramref name="json"/> — public to the tests, which read the file from the repository.</summary>
    internal static RoleDefaults Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var vendors = new Dictionary<string, VendorDefaults>(StringComparer.Ordinal);
        foreach (var vendor in root.GetProperty("mainAi").EnumerateObject())
        {
            vendors[vendor.Name] = VendorOf(vendor.Value);
        }

        var method = root.GetProperty("workMethod");
        var mvp = method.GetProperty("mvpEpics");

        return new RoleDefaults(
            vendors,
            [.. root.GetProperty("haikuKinds").EnumerateArray().Select(kind => kind.GetString() ?? string.Empty)],
            new WorkMethod(
                mvp.GetProperty("min").GetInt32(),
                mvp.GetProperty("max").GetInt32(),
                method.GetProperty("maxEpicsPerEpoch").GetInt32(),
                method.GetProperty("minConsultsPerEpoch").GetInt32(),
                method.GetProperty("minConsultsPerEpic").GetInt32(),
                method.GetProperty("maxStoriesPerEpic").GetInt32(),
                method.GetProperty("maxTasksPerStory").GetInt32()));
    }

    private static VendorDefaults VendorOf(JsonElement vendor)
    {
        var roles = RoleNames.Select(name => (name, (IReadOnlyList<DefaultModel>)[.. vendor.GetProperty("roles").GetProperty(name).EnumerateArray().Select(ModelOf)])).ToList();
        var bands = vendor.GetProperty("complexityBands").EnumerateArray().Select(band => new Band(
            band.GetProperty("min").GetInt32(),
            band.GetProperty("max").GetInt32(),
            band.GetProperty("model").GetString() ?? string.Empty,
            band.GetProperty("vendor").GetString() ?? string.Empty)).ToList();

        // A shipped default has no default of its own to fall back on, so one that breaks the vendor-routing rule is a build fault, like a file that does not parse.
        var wrong = roles.SelectMany(role => role.Item2.Select(model => (Where: $"role '{role.name}'", model.Model, model.Vendor)))
            .Concat(bands.Select(band => (Where: "a band", band.Model, band.Vendor)))
            .Select(entry => (entry.Where, Refusal: VendorRouting.Refusal(entry.Model, entry.Vendor)))
            .FirstOrDefault(entry => entry.Refusal.Length > 0);
        if (wrong.Refusal is { Length: > 0 })
        {
            throw new InvalidOperationException($"shared/role-defaults.json: {wrong.Where} breaks the vendor-routing rule — {wrong.Refusal}");
        }

        return new VendorDefaults(
            vendor.GetProperty("cli").GetString() ?? string.Empty,
            roles,
            bands,
            vendor.TryGetProperty("note", out var note) ? note.GetString() ?? string.Empty : string.Empty);
    }

    private static DefaultModel ModelOf(JsonElement model) => new(
        model.GetProperty("model").GetString() ?? string.Empty,
        model.GetProperty("vendor").GetString() ?? string.Empty,
        model.TryGetProperty("whenVendorRow", out var row) ? row.GetString() ?? string.Empty : string.Empty);
}

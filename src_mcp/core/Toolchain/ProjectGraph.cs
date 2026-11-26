using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace CoaiMcp.Core.Toolchain;

/// <summary>What one project file says, as data: its references (resolved to repository paths), the packages it names, and the facts that make it a test project. Nothing here is an argv item.</summary>
/// <param name="Path">The project file's path from the repository root, with <c>/</c> separators.</param>
/// <param name="References">Each <c>ProjectReference</c> that resolves to a path INSIDE the repository, normalised; sorted.</param>
/// <param name="Outside">The <c>Include</c> texts of references that leave the repository: noted and never read.</param>
/// <param name="Unresolved">The <c>Include</c> texts that hold an MSBuild property or wildcard: they cannot be resolved without evaluating MSBuild.</param>
/// <param name="Packages">The names of every <c>PackageReference</c> (an <c>Include</c> or an <c>Update</c>), sorted.</param>
/// <param name="Properties">The three properties that decide the test facts, when the project (or its <c>Directory.Build.props</c>) sets one without a condition: <c>true</c> or <c>false</c>.</param>
/// <param name="Conditioned">How many elements that matter had a condition and were not evaluated.</param>
public sealed record ProjectFacts(
    string Path,
    IReadOnlyList<string> References,
    IReadOnlyList<string> Outside,
    IReadOnlyList<string> Unresolved,
    IReadOnlyList<string> Packages,
    IReadOnlyDictionary<string, bool> Properties,
    int Conditioned);

/// <summary>A project file that could not be read as a project: its path and why.</summary>
public sealed record ProjectFault(string Path, string Why);

/// <summary>
/// Reads MSBuild project files (<c>.csproj</c>, <c>.fsproj</c>, <c>.vbproj</c>) and the <c>Directory.Build.props</c> beside them as plain XML (epoch 3 E4 s2 t3), and answers the reference graph. <b>The XML is untrusted</b>: DTD processing is prohibited and there is no resolver, so an external entity
/// reads no file and asks for no address; the document size is bounded; nothing is evaluated — no MSBuild, no property expansion, no condition.
/// </summary>
public static class ProjectGraph
{
    /// <summary>The properties whose value decides the test facts (measured by story s1, <c>RESULTS_s1_dotnet.md</c>): an MTP project sets one of the first two; a project can say it is or is not a test project.</summary>
    public static readonly IReadOnlyList<string> TestProperties = ["UseMicrosoftTestingPlatformRunner", "TestingPlatformDotnetTestSupport", "IsTestProject"];

    /// <summary>The package a VSTest project references (measured by story s1).</summary>
    public const string VsTestPackage = "Microsoft.NET.Test.Sdk";

    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersFromEntities = 0,
        MaxCharactersInDocument = ManifestLimits.MaxManifestBytes,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
    };

    /// <summary>Reads one project file. A text that is not XML, holds a DTD or is too large is a fault that names the path; it is never an empty project.</summary>
    public static (ProjectFacts? Facts, ProjectFault? Fault) Read(string path, string text, string? propsText = null)
    {
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new StringReader(text), Settings);
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException e)
        {
            return (null, new ProjectFault(path, $"is not read as a project: {Short(e.Message)} (DTDs and external entities are never processed)"));
        }

        var root = document.Root;
        if (root is null || root.Name.LocalName != "Project")
        {
            return (null, new ProjectFault(path, "is not an MSBuild project (its root element is not <Project>)"));
        }

        var folder = Folder(path);
        var conditioned = 0;
        var references = new List<string>();
        var outside = new List<string>();
        var unresolved = new List<string>();
        var packages = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var element in root.Descendants())
        {
            switch (element.Name.LocalName)
            {
                case "ProjectReference":
                    if (Conditioned(element))
                    {
                        conditioned++;

                        break;
                    }

                    var include = (string?)element.Attribute("Include") ?? string.Empty;
                    switch (Resolve(folder, include))
                    {
                        case (Resolution.Inside, var resolved): references.Add(resolved); break;
                        case (Resolution.Outside, _): outside.Add(include); break;
                        default: unresolved.Add(include); break;
                    }

                    break;
                case "PackageReference":
                    if (Conditioned(element))
                    {
                        conditioned++;

                        break;
                    }

                    var name = ((string?)element.Attribute("Include") ?? (string?)element.Attribute("Update") ?? string.Empty).Trim();
                    if (name.Length > 0)
                    {
                        packages.Add(name);
                    }

                    break;
            }
        }

        var properties = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var source in new[] { propsText, text })
        {
            if (source is null)
            {
                continue;
            }

            var (found, skipped) = PropertiesOf(source == text ? document : Parse(source));
            conditioned += skipped;
            foreach (var (key, value) in found)
            {
                properties[key] = value;
            }
        }

        return (new ProjectFacts(path, [.. references.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)], [.. outside.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)], [.. unresolved.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)], [.. packages], properties, conditioned), null);
    }

    private static XDocument? Parse(string text)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(text), Settings);

            return XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static (Dictionary<string, bool> Found, int Skipped) PropertiesOf(XDocument? document)
    {
        var found = new Dictionary<string, bool>(StringComparer.Ordinal);
        var skipped = 0;
        foreach (var group in document?.Root?.Elements().Where(element => element.Name.LocalName == "PropertyGroup") ?? [])
        {
            foreach (var element in group.Elements().Where(element => TestProperties.Contains(element.Name.LocalName)))
            {
                if (Conditioned(group) || Conditioned(element))
                {
                    skipped++;

                    continue;
                }

                found[element.Name.LocalName] = string.Equals(element.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase);
            }
        }

        return (found, skipped);
    }

    private static bool Conditioned(XElement element) => element.Attribute("Condition") is not null || (element.Parent is { } parent && parent.Name.LocalName is "ItemGroup" or "PropertyGroup" && parent.Attribute("Condition") is not null);

    private enum Resolution
    {
        Inside,
        Outside,
        Unresolved,
    }

    /// <summary>A <c>ProjectReference</c> include against the project's folder: backslashes read as separators, <c>.</c> and <c>..</c> folded; a result above the repository root is outside, a property or wildcard is unresolved.</summary>
    private static (Resolution, string) Resolve(string folder, string include)
    {
        var text = include.Trim().Replace('\\', '/');
        if (text.Length == 0 || text.Contains("$(", StringComparison.Ordinal) || text.Contains('*', StringComparison.Ordinal) || text.Contains('@', StringComparison.Ordinal) || text.Contains('%', StringComparison.Ordinal))
        {
            return (Resolution.Unresolved, string.Empty);
        }

        if (text.StartsWith('/') || (text.Length > 1 && text[1] == ':'))
        {
            return (Resolution.Outside, string.Empty);
        }

        var parts = new List<string>(folder.Length == 0 ? [] : folder.Split('/'));
        foreach (var segment in text.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (parts.Count == 0)
                {
                    return (Resolution.Outside, string.Empty);
                }

                parts.RemoveAt(parts.Count - 1);

                continue;
            }

            parts.Add(segment);
        }

        return (Resolution.Inside, string.Join('/', parts));
    }

    /// <summary>The folder of a repository path: everything before the last <c>/</c>; empty for the root.</summary>
    public static string Folder(string path)
    {
        var slash = path.LastIndexOf('/');

        return slash < 0 ? string.Empty : path[..slash];
    }

    private static string Short(string message) => message.Length > 160 ? message[..160] : message.Replace('\n', ' ');

    /// <summary>The count of references that reach <paramref name="path"/> from other projects of <paramref name="all"/>.</summary>
    public static int ReferencedBy(string path, IEnumerable<ProjectFacts> all) =>
        all.Count(other => other.Path != path && other.References.Contains(path, StringComparer.Ordinal));

    /// <summary>Whether <paramref name="child"/> lies in <paramref name="folder"/> (a folder of <c>""</c> holds everything).</summary>
    public static bool Under(string folder, string child) => folder.Length == 0 || child.StartsWith(folder + "/", StringComparison.Ordinal);

    /// <summary>A whole number as invariant text.</summary>
    internal static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}

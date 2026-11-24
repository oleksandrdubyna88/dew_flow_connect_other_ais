namespace CoaiMcp.Core.Work;

/// <summary>
/// What reading a YAML file gave: the root, or the one refusal that stopped the read. A refusal carries a code of the work-plan error table
/// (<c>yaml-too-large</c>, <c>yaml-syntax</c>, <c>yaml-forbidden</c>, <c>yaml-duplicate-key</c>), a sentence and the line.
/// </summary>
public abstract record YamlRead
{
    private YamlRead()
    {
    }

    /// <summary>The file read; <see cref="Root"/> is a map, a list or a scalar.</summary>
    public sealed record Parsed(YamlNode Root) : YamlRead;

    /// <summary>The file was refused.</summary>
    public sealed record Refused(string Code, string Message, int Line) : YamlRead;
}

/// <summary>
/// The seam between the work-plan validator and a YAML parser. The shipped reader is <see cref="StrictYamlReader"/>, a strict subset of YAML
/// (see <c>v2/spikes/RESULTS_yaml_aot.md</c> for why it is not a package): a second reader can sit behind this without a change to the validator.
/// </summary>
public interface IYamlReader
{
    /// <summary>Reads one document. Never throws for a file that is wrong; that is a <see cref="YamlRead.Refused"/>.</summary>
    YamlRead Read(string text);
}

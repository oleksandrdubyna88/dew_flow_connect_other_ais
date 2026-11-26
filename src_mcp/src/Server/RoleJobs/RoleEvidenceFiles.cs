using System.Text;
using CoaiMcp.Core.Roles;
using CoaiMcp.Runners.Files;
using Files = CoaiMcp.Runners.Files;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The files of the tree a scout or judge job read, as the evidence check sees them (epoch 3 E3 s1 t1): <see cref="IEvidenceSource"/> over the working tree of the repository. It is the ONE way a path of a model's answer reaches a file
/// (spec E.5), and it reaches it through <see cref="RealPath"/>, the one road that resolves links — never by a walk of its own.
/// </summary>
/// <remarks>
/// <para><b>A path that runs through a link is not opened.</b> The path is resolved root-down; when the real path is not the path as spelled below the real root, a link was on the way — a link to a place inside the repository as well as one
/// that leaves it — and the answer is <see cref="EvidenceFileState.ThroughLink"/>, an error of the answer. A path that cannot be resolved (a loop, an IO error) is the same.</para>
/// <para>The text is read whole, at most <see cref="RoleEvidence.MaxFileBytes"/>, and decoded as strict UTF-8: a file over the cap and a file that is not UTF-8 each give their own state, never a guess. A line ends at <c>\n</c>
/// (a <c>\r</c> before it is not part of the line); the empty piece after the last newline is not a line. Each file is read once per job.</para>
/// <para>The paths it is asked about passed <see cref="RoleEvidence.PathProblem"/> first: no <c>..</c>, no drive, no credential-shaped name.</para>
/// </remarks>
internal sealed class RoleEvidenceFiles(string repoPath) : IEvidenceSource
{
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static StringComparison PathComparison => OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private readonly Dictionary<string, EvidenceFileRead> _read = new(StringComparer.Ordinal);

    public EvidenceFileState Probe(string path) => Located(path).State;

    public EvidenceFileRead Read(string path)
    {
        if (_read.TryGetValue(path, out var known))
        {
            return known;
        }

        var found = Located(path);

        return _read[path] = found.State == EvidenceFileState.Ok ? Lines(found.Path) : EvidenceFileRead.Without(found.State);
    }

    /// <summary>Where the path is and what it is, without reading it: the real file when it is a regular file inside the tree with no link on the way.</summary>
    private (EvidenceFileState State, string Path) Located(string path)
    {
        var root = RealPath.Resolve(repoPath);
        if (!root.Ok)
        {
            return (EvidenceFileState.Missing, string.Empty);
        }

        var spelled = Path.Combine(root.Path, path.Replace('/', Path.DirectorySeparatorChar));
        var real = RealPath.Resolve(spelled);
        if (!real.Ok || !string.Equals(real.Path, spelled, PathComparison))
        {
            return (EvidenceFileState.ThroughLink, string.Empty);
        }

        return (Kind(RealPath.Lstat(real.Path)), real.Path);
    }

    private static EvidenceFileState Kind(Entry entry) => entry.Kind switch
    {
        Files.EntryKind.Missing => EvidenceFileState.Missing,
        Files.EntryKind.Directory => EvidenceFileState.NotAFile,
        Files.EntryKind.Link => EvidenceFileState.ThroughLink,
        _ => EvidenceFileState.Ok,
    };

    private static EvidenceFileRead Lines(string file)
    {
        try
        {
            var length = new FileInfo(file).Length;
            if (length > RoleEvidence.MaxFileBytes)
            {
                return EvidenceFileRead.Without(EvidenceFileState.TooLarge);
            }

            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var bytes = new byte[length + 1];
            var total = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);

            return total > length ? EvidenceFileRead.Without(EvidenceFileState.TooLarge) : Decoded(bytes.AsSpan(0, total));
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return EvidenceFileRead.Without(EvidenceFileState.Missing);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return EvidenceFileRead.Without(EvidenceFileState.Unreadable);
        }
    }

    private static EvidenceFileRead Decoded(ReadOnlySpan<byte> bytes)
    {
        try
        {
            var text = Strict.GetString(bytes).TrimStart('﻿');
            var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
            if (lines.Count > 0 && lines[^1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            return new EvidenceFileRead(EvidenceFileState.Ok, lines);
        }
        catch (DecoderFallbackException)
        {
            return EvidenceFileRead.Without(EvidenceFileState.NotUtf8);
        }
    }
}

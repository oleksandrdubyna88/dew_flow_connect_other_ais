using CoaiMcp.Core.Work;
using CoaiMcp.Work;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary><c>DiskWorkFolder</c> (epoch 3 E1 s2 t3): the working tree as an <c>IWorkFolder</c> — names of folders, a file as text or as one of its three other answers, and a link never followed.</summary>
public sealed class ADiskWorkFolderTests : IDisposable
{
    private readonly TempDir _root = TempDir.For("coai-work-folder-");

    private DiskWorkFolder Folder => new(_root.At("feature"));

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root.At("feature"), relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void Folders_AreTheDirectChildren_AndAMissingFolderHasNone()
    {
        Write("epoch-1/epoch.yaml", "x");
        Write("epoch-2/epoch.yaml", "x");
        Write("loose.yaml", "x");

        Folder.Folders(string.Empty).Should().BeEquivalentTo("epoch-1", "epoch-2");
        Folder.Folders("epoch-1").Should().BeEmpty();
        Folder.Folders("nowhere/at/all").Should().BeEmpty();
    }

    [Fact]
    public void AFile_IsItsText_OrMissing()
    {
        Write("a/b.yaml", "id: x\n");

        Folder.Read("a/b.yaml", 100).Should().BeEquivalentTo(new WorkFile.Text("id: x\n"));
        Folder.Read("a/c.yaml", 100).Should().BeOfType<WorkFile.Missing>();
        Folder.Read("a", 100).Should().BeOfType<WorkFile.Missing>("a folder is not a file");
    }

    [Fact]
    public void AFileOverTheLimit_IsTooLarge_AndNotReadWhole()
    {
        Write("big.yaml", new string('x', 2000));

        Folder.Read("big.yaml", 499).Should().BeEquivalentTo(new WorkFile.TooLarge(2000), "the answer names the real size, which the length says without a read");
        Folder.Read("big.yaml", 2000).Should().BeOfType<WorkFile.Text>();
        Folder.Read("big.yaml", 1999).Should().BeOfType<WorkFile.TooLarge>();
    }

    [Fact]
    public void TextThatIsNotUtf8_IsAnAnswer_NotAThrow()
    {
        var path = Path.Combine(_root.At("feature"), "bad.yaml");
        Directory.CreateDirectory(_root.At("feature"));
        File.WriteAllBytes(path, [0x61, 0xFF, 0xFE, 0x62]);

        Folder.Read("bad.yaml", 100).Should().BeOfType<WorkFile.NotUtf8>();
    }

    [Fact]
    public void ALinkToAFolderOrAFile_IsNotFollowed()
    {
        var outside = _root.At("outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.yaml"), "id: leaked\n");
        Directory.CreateDirectory(_root.At("feature"));
        Directory.CreateSymbolicLink(Path.Combine(_root.At("feature"), "epoch-9"), outside);
        File.CreateSymbolicLink(Path.Combine(_root.At("feature"), "linked.yaml"), Path.Combine(outside, "secret.yaml"));

        Folder.Folders(string.Empty).Should().BeEmpty("a link that is a folder is not a folder here");
        Folder.Read("linked.yaml", 100).Should().BeOfType<WorkFile.Missing>("a link that is a file is not a file here");
    }

    public void Dispose() => _root.Dispose();
}

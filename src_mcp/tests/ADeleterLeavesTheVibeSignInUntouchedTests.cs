using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Vibe;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Every deleter of a coai-owned Vibe home leaves the person's <c>~/.vibe/.env</c> as it found it (DL2 of
/// <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>; A1 spec §5; S-V10): the link inside the home is gone, and the file it pointed at keeps its
/// bytes, its mode and its mtime — on a <c>0600</c> target and on a <c>0400</c> one.
/// </summary>
/// <remarks>
/// <para><b>This test is GREEN first, not red</b> (A1 spec §9 item 4): today's deleters already unlink a file link by its own name
/// (<c>GitScratch.Unlink</c>) and never ask a link's attributes, so there is no product defect to observe. Its teeth are proven the testing
/// rule's way for a fix that already landed — <see cref="Mutant"/>, a deleter with no link check that clears the read-only attribute and
/// deletes every entry: on Unix <c>File.SetAttributes</c> is a <c>chmod</c> that FOLLOWS a symbolic link, so it changes the TARGET's mode, and
/// <see cref="TheMutantDeleter_ChangesTheTargetsMode_AndTheCheckSeesIt"/> goes red-in-miniature with the real symptom
/// (<c>mode 400 → 600</c>).</para>
/// <para>Linux only: a file symlink needs no privilege there and the mode is a Unix mode. Skipped elsewhere with that reason.</para>
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class ADeleterLeavesTheVibeSignInUntouchedTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("testsToDelete-vsd-");

    public void Dispose()
    {
        if (OperatingSystem.IsLinux())
        {
            foreach (var file in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories).Where(file => !OwnedPaths.IsLinkIfThere(file)))
            {
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        _dir.Dispose();
    }

    private static void OnLinux() => Assert.SkipUnless(OperatingSystem.IsLinux(), "a file symlink to a Unix-moded file: runs on the Linux legs");

    private static string Mode(UnixFileMode mode) => Convert.ToString((int)mode, 8);

    /// <summary>What the person's file was: its bytes, its mode and its mtime.</summary>
    private sealed record Seen(byte[] Bytes, UnixFileMode Mode, DateTime Written);

    private static Seen Look(string path) => new(File.ReadAllBytes(path), File.GetUnixFileMode(path), File.GetLastWriteTimeUtc(path));

    /// <summary>Empty when the file is as it was; else what changed, in the words of the red message.</summary>
    private static string Difference(Seen before, Seen after) =>
        string.Join("; ", new[]
        {
            before.Bytes.SequenceEqual(after.Bytes) ? string.Empty : "its bytes",
            before.Mode == after.Mode ? string.Empty : $"mode {Mode(before.Mode)} → {Mode(after.Mode)}",
            before.Written == after.Written ? string.Empty : "its mtime",
        }.Where(said => said.Length > 0));

    private string PersonsFile(UnixFileMode mode)
    {
        var path = Path.Combine(Directory.CreateDirectory(_dir.At("person")).FullName, ".env");
        File.WriteAllText(path, "MISTRAL_API_KEY=sk-test-deleter\n");
        File.SetUnixFileMode(path, mode);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-3));

        return path;
    }

    private static void LinkInto(string home, string persons)
    {
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "vibehistory"), "Hello Vibe!\n");
        File.CreateSymbolicLink(Path.Combine(home, ".env"), persons);
    }

    /// <summary>The deleter that is the defect: no link check — every entry has its read-only attribute cleared, then is deleted.</summary>
    private static void Mutant(string folder)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(entry, File.GetAttributes(entry) & ~FileAttributes.ReadOnly);
            File.Delete(entry);
        }
    }

    private const UnixFileMode OwnerReadWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public static TheoryData<string, UnixFileMode> Routes()
    {
        TheoryData<string, UnixFileMode> data = [];
        foreach (var route in new[] { "scratch", "oneshot-sweep", "oneshot-dispose", "consult-sweep" })
        {
            data.Add(route, OwnerReadWrite);
            data.Add(route, UnixFileMode.UserRead);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void EveryDeleter_RemovesTheLink_AndLeavesThePersonsFileAsItWas(string route, UnixFileMode mode)
    {
        OnLinux();
        var persons = PersonsFile(mode);
        var before = Look(persons);

        var home = Run(route, persons);

        OwnedPaths.IsLinkIfThere(home).Should().BeFalse("the link is gone with the home — removed by its own name");
        Directory.Exists(Path.GetDirectoryName(home)!).Should().BeFalse($"route {route}: the home's folder is gone");
        Difference(before, Look(persons)).Should().BeEmpty($"the deleter changed the person's ~/.vibe/.env (route {route}, target mode {Mode(mode)})");
    }

    /// <summary>Makes a home holding the link and removes it by <paramref name="route"/>; answers where the home's link was.</summary>
    private string Run(string route, string persons)
    {
        switch (route)
        {
            case "scratch":
                return Removed(_dir.At("scratch", "home"), persons, home => GitScratch.DeleteEvenIfReadOnly(Path.GetDirectoryName(home)!));
            case "oneshot-sweep":
                var folder = VibeHomes.OneShotIn(_dir.At("sweep-root"), "0ddba11c");
                return Removed(Path.Combine(folder, "a1b2c3"), persons,
                    _ => VibeHomes.SweepOneShot(_dir.At("sweep-root"), DateTime.UtcNow.AddHours(1), _ => { }).Should().Be(1, "the old one-shot home is swept"));
            case "oneshot-dispose":
                var reserved = VibeHomes.ReserveOneShot(Directory.CreateDirectory(_dir.At("dispose-root")).FullName).Should().BeOfType<OneShotReservation.Reserved>().Subject;
                return Removed(Path.Combine(reserved.Home.Folder, "x"), persons, _ => reserved.Home.Dispose());
            default:
                return ConsultSweep(persons, GitScratch.DeleteEvenIfReadOnly);
        }
    }

    private static string Removed(string home, string persons, Action<string> remove)
    {
        LinkInto(home, persons);
        var link = Path.Combine(home, ".env");
        new FileInfo(link).LinkTarget.Should().Be(persons, "the fixture's link must be the real thing, or this proves nothing");
        remove(link);

        return link;
    }

    /// <summary>D20's sweep, through its delete seam: a conversation folder holding a home with the link, idle past the window.</summary>
    private string ConsultSweep(string persons, Action<string> delete)
    {
        var root = Directory.CreateDirectory(_dir.At("local")).FullName;
        var data = _dir.At("data");
        var store = ConversationFolder.StoreOf(data);
        var id = ConsultationStore.NewId();
        var folder = ConsultantHomes.ConversationFolder(root, ConsultantHomes.Vibe, id);
        var home = Path.Combine(folder, "h");
        LinkInto(home, persons);
        Directory.CreateDirectory(Path.Combine(folder, "tree"));
        var window = TimeSpan.FromMinutes(15);
        ConversationFolder.Begin(folder, store, DateTime.UtcNow - window * 2, ConsultationDeadline.For(TimeSpan.FromMinutes(10)));
        ConversationFolder.End(folder);
        var then = DateTime.UtcNow - window * 2;
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder).Where(entry => !OwnedPaths.IsLinkIfThere(entry)))
        {
            Directory.SetLastWriteTimeUtc(entry, then);
        }

        Directory.SetLastWriteTimeUtc(folder, then);
        var link = Path.Combine(home, ".env");

        ConsultantHomeSweep.Sweep(new HomeSweepInputs(root, store, new ConsultationStore(data).Read, DateTime.UtcNow, window), _ => { }, delete).Should().Be(1);

        return link;
    }

    /// <summary>
    /// The teeth (testing rule: a fix that already landed): the mutant deleter, through the same sweep, changes the person's 0400 file to 0600 —
    /// and the difference check says so. Were the check blind to a mode, the green rows above would mean nothing.
    /// </summary>
    [Fact]
    public void TheMutantDeleter_ChangesTheTargetsMode_AndTheCheckSeesIt()
    {
        OnLinux();
        var persons = PersonsFile(UnixFileMode.UserRead);
        var before = Look(persons);

        ConsultSweep(persons, Mutant);

        Difference(before, Look(persons)).Should().Be("mode 400 → 600", "the deleter changed the person's ~/.vibe/.env (mode 400 → 600): a Unix SetAttributes through the link chmods the TARGET");
    }
}

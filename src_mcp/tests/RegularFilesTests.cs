using System.Diagnostics;
using System.Net.Sockets;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Vibe;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <see cref="RegularFiles"/>, the Linux leg ported from wsl_care (DL2 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>; S-V14): a file a
/// person NAMED is read only when it is a regular file their own account alone may write, only up to the cap of bytes actually read, and
/// never through a link — all judged from one open descriptor. A FIFO, a socket or a device is refused at once, never waited on.
/// </summary>
/// <remarks>
/// The pure rows (the cap, the ownership Theory, the <c>statx</c> offsets) run everywhere. The rows that need a Linux file — a FIFO,
/// <c>/dev/zero</c>, a unix socket, a mode, a symbolic link — skip on Windows and run on the ubuntu CI job; no host here can make
/// another uid's file without root, so that arm is the pure ownership row.
/// </remarks>
public sealed class RegularFilesTests : IDisposable
{
    private readonly TempDir _root = TempDir.For("testsToDelete-regular-files-");

    public void Dispose() => _root.Dispose();

    private static uint Me => RegularFiles.EffectiveUid();

    private string Own(string name, byte[] bytes, UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite)
    {
        var path = _root.At(name);
        File.WriteAllBytes(path, bytes);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, mode);
        }

        return path;
    }

    private static void OnLinux() => Assert.SkipUnless(OperatingSystem.IsLinux(), "a Linux file (a FIFO, a socket, a mode, a symlink): runs on the Linux legs");

    [Fact]
    public void The_cap_counts_bytes_read_never_the_length_a_stream_claims()
    {
        using var lying = new LyingStream(claimedLength: 10, actualBytes: 2 * 1024 * 1024);

        RegularFiles.Capped(lying, 1024).Should().BeOfType<FileReadResult.Unreadable>().Which.Reason.Should().Contain("larger than 1024 bytes");
        lying.Served.Should().BeLessThanOrEqualTo(1025, "nothing past the cap is read");
    }

    [Fact]
    public void The_cap_admits_exactly_the_limit_and_refuses_one_byte_more()
    {
        using var atCap = new MemoryStream(new byte[DotenvNames.MaxBytes]);
        using var over = new MemoryStream(new byte[DotenvNames.MaxBytes + 1]);

        RegularFiles.Capped(atCap, DotenvNames.MaxBytes).Should().BeOfType<FileReadResult.Content>().Which.Bytes.Should().HaveCount(65_536);
        RegularFiles.Capped(over, DotenvNames.MaxBytes).Should().BeOfType<FileReadResult.Unreadable>().Which.Reason.Should().Be("larger than 65536 bytes");
    }

    /// <summary>
    /// The credential-bearing array the cap reads INTO is erased, not just the copy handed out: after a success, after an oversized file and
    /// after a read that throws, the array the stream was given holds no byte of what it served.
    /// </summary>
    [Fact]
    public void The_array_the_cap_reads_into_is_zeroed_after_a_success()
    {
        using var stream = new KeepingStream("MISTRAL_API_KEY=sk-test-canary\n"u8.ToArray());

        var read = RegularFiles.Capped(stream, 1024).Should().BeOfType<FileReadResult.Content>().Subject;

        read.Bytes.Should().Equal("MISTRAL_API_KEY=sk-test-canary\n"u8.ToArray(), "the copy handed out holds the file");
        stream.Given.Should().NotBeEmpty().And.OnlyContain(b => b == 0, "the array the cap read into must not keep the credential");
    }

    [Fact]
    public void The_array_the_cap_reads_into_is_zeroed_after_an_oversized_file()
    {
        using var stream = new KeepingStream(Enumerable.Repeat((byte)'k', 2048).ToArray());

        RegularFiles.Capped(stream, 1024).Should().BeOfType<FileReadResult.Unreadable>();

        stream.Given.Should().NotBeEmpty().And.OnlyContain(b => b == 0);
    }

    [Fact]
    public void The_array_the_cap_reads_into_is_zeroed_when_a_read_throws()
    {
        using var stream = new KeepingStream("MISTRAL_API_KEY=sk-test-canary\n"u8.ToArray()) { ThrowAfterFirstRead = true };

        var capped = () => RegularFiles.Capped(stream, 1024);

        capped.Should().Throw<IOException>();
        stream.Given.Should().NotBeEmpty().And.OnlyContain(b => b == 0, "an exception in the middle of a read must not leave the credential behind");
    }

    /// <summary>
    /// What a failed <c>open</c> is called, in words a person can act on, with the errno kept in parentheses (pure — every host proves it):
    /// no read permission, a name too long, a link never followed, a socket; a file that is not there is `Missing`, not a refusal.
    /// </summary>
    [Theory]
    [InlineData(13, "no read permission (errno 13)")]
    [InlineData(1, "no read permission (errno 1)")]
    [InlineData(36, "path too long (errno 36)")]
    [InlineData(40, "a symbolic link, never followed (errno 40)")]
    [InlineData(6, "not a regular file (a socket)")]
    [InlineData(5, "could not be opened (errno 5)")]
    public void A_failed_open_is_named_by_what_a_person_can_do_about_it(int errno, string reason)
    {
        RegularFiles.OwnedErrno(errno).Should().BeOfType<FileReadResult.Unreadable>().Which.Reason.Should().Be(reason);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(20)]
    public void A_name_that_is_not_there_is_Missing_by_errno(int errno)
    {
        RegularFiles.OwnedErrno(errno).Should().BeOfType<FileReadResult.Missing>();
    }
    /// <summary>glibc before 2.28 has no <c>statx</c> (and a libc may be missing altogether): a refusal with a reason, never an exception out of the launch.</summary>
    [Theory]
    [InlineData(typeof(EntryPointNotFoundException))]
    [InlineData(typeof(DllNotFoundException))]
    public void A_libc_without_statx_is_a_refusal_never_an_exception(Type failure)
    {
        var read = RegularFiles.Guarded(() => throw (Exception)Activator.CreateInstance(failure)!);

        read.Should().BeOfType<FileReadResult.Unreadable>().Which.Reason.Should().Be("statx is not available in this libc — Vibe on this distribution is unmeasured");
    }

    /// <summary>Serves its bytes in two reads and keeps the array it was handed.</summary>
    private sealed class KeepingStream(byte[] content) : Stream
    {
        private int _at;

        public byte[] Given { get; private set; } = [];

        public bool ThrowAfterFirstRead { get; init; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => _at; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Given = buffer;
            if (ThrowAfterFirstRead && _at > 0)
            {
                throw new IOException("Input/output error");
            }

            var n = Math.Min(Math.Min(count, ThrowAfterFirstRead ? 10 : count), content.Length - _at);
            Array.Copy(content, _at, buffer, offset, n);
            _at += n;

            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A file coai links is its owner's alone: another uid, or a group / other write bit, is a refusal naming which.</summary>
    [Theory]
    [InlineData(1000u, 0x81A4, 1000u, "")]
    [InlineData(1000u, 0x8180, 1000u, "")]
    [InlineData(1000u, 0x81A0, 1000u, "")]
    [InlineData(1000u, 0x81A4, 0u, "owned by uid 1000, not uid 0")]
    [InlineData(1000u, 0x81B4, 1000u, "writable by group or others (mode 664)")]
    [InlineData(1000u, 0x81A6, 1000u, "writable by group or others (mode 646)")]
    public void A_file_of_another_owner_or_a_wide_mode_is_refused(uint fileOwner, int mode, uint owner, string problem) =>
        RegularFiles.OwnershipProblem(fileOwner, mode, owner).Should().Be(problem, "a file owned by uid 1000 was admitted for another user, or a group-writable one was admitted");

    /// <summary>The offsets of the kernel's 256-byte <c>struct statx</c> — read from a buffer built here, so a Windows host proves them.</summary>
    [Fact]
    public void A_statx_buffer_reads_as_the_kernel_lays_it_out()
    {
        var buffer = new byte[256];
        BitConverter.GetBytes((ushort)0x81A4).CopyTo(buffer, 28);
        BitConverter.GetBytes(1000u).CopyTo(buffer, 20);
        BitConverter.GetBytes(41UL).CopyTo(buffer, 32);
        BitConverter.GetBytes(8u).CopyTo(buffer, 136);
        BitConverter.GetBytes(1u).CopyTo(buffer, 140);

        var status = FileStatus.FromStatx(buffer);

        status.Should().Be(new FileStatus(0x8000, 0x1A4, 1000, 41, 8, 1));
        status.IsRegular.Should().BeTrue();
        status.SameFileAs(status with { OwnerUid = 7 }).Should().BeTrue("one device and one inode are one file");
        status.SameFileAs(status with { Inode = 42 }).Should().BeFalse();
    }

    [Fact]
    public void A_host_that_is_not_Linux_answers_with_a_reason_never_a_throw()
    {
        Assert.SkipWhen(OperatingSystem.IsLinux(), "the non-Linux answer is not reachable on a Linux host");

        RegularFiles.ReadOwned(_root.At("anything"), DotenvNames.MaxBytes, 0).Should().BeOfType<FileReadResult.Unreadable>().Which.Reason.Should().Be(RegularFiles.LinuxOnly);
    }

    [Fact]
    public async Task A_fifo_a_socket_and_a_device_are_refused_at_once_never_waited_on_nor_streamed()
    {
        OnLinux();
        var fifo = _root.At("shown.fifo");
        using (var made = Process.Start(new ProcessStartInfo("mkfifo", [fifo]) { UseShellExecute = false })!)
        {
            await made.WaitForExitAsync(TestContext.Current.CancellationToken);
            made.ExitCode.Should().Be(0, "the fixture's FIFO must exist, or this proves nothing");
        }

        var socket = _root.At("shown.sock");
        using var bound = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        bound.Bind(new UnixDomainSocketEndPoint(socket));

        var read = Task.Run(() => (RegularFiles.ReadOwned(fifo, 64, Me), RegularFiles.ReadOwned("/dev/zero", 64, Me), RegularFiles.ReadOwned("/dev/null", 64, Me), RegularFiles.ReadOwned(socket, 64, Me)),
            TestContext.Current.CancellationToken);
        var finished = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)) == read;
        if (!finished)
        {
            await using var writer = new FileStream(fifo, FileMode.Open, FileAccess.Write); // releases the blocked reader
        }

        finished.Should().BeTrue("the judgement blocked on a FIFO: an open that could block is never made");
        var (fromFifo, fromZero, fromNull, fromSocket) = await read;
        fromFifo.Should().BeOfType<FileReadResult.Unreadable>().Which.Reason.Should().Contain("a FIFO");
        fromZero.Should().BeOfType<FileReadResult.Unreadable>().Which.Reason.Should().Contain("a device", "/dev/zero streams for ever if the type is not asked first");
        fromNull.Should().BeOfType<FileReadResult.Unreadable>().Which.Reason.Should().Contain("a device");
        fromSocket.Should().BeOfType<FileReadResult.Unreadable>().Which.Reason.Should().Contain("a socket");
    }

    [Fact]
    public void A_regular_file_of_my_own_is_read_up_to_the_cap_and_one_byte_more_is_refused()
    {
        OnLinux();
        var atCap = Own("at-cap", new byte[DotenvNames.MaxBytes]);
        var over = Own("over", new byte[DotenvNames.MaxBytes + 1]);

        RegularFiles.ReadOwned(atCap, DotenvNames.MaxBytes, Me).Should().BeOfType<FileReadResult.Content>().Which.Bytes.Should().HaveCount(65_536);
        RegularFiles.ReadOwned(over, DotenvNames.MaxBytes, Me).Should().BeOfType<FileReadResult.Unreadable>().Which.Reason.Should().Be("larger than 65536 bytes");
    }

    [Fact]
    public void A_symbolic_link_is_refused_never_followed()
    {
        OnLinux();
        var target = Own("target", "MISTRAL_API_KEY=sk-test-canary\n"u8.ToArray());
        var link = _root.At("link");
        File.CreateSymbolicLink(link, target);

        RegularFiles.ReadOwned(link, DotenvNames.MaxBytes, Me).Should().BeOfType<FileReadResult.Unreadable>().Which.Reason.Should().StartWith("a symbolic link, never followed");
        RegularFiles.ReadOwned(target, DotenvNames.MaxBytes, Me).Should().BeOfType<FileReadResult.Content>("the positive companion: the same bytes, read by the file's own name");
    }

    [Theory]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupWrite, "writable by group or others (mode 620)")]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherWrite, "writable by group or others (mode 602)")]
    public void A_file_other_accounts_may_write_is_refused_with_its_mode(UnixFileMode mode, string reason)
    {
        OnLinux();
        var path = Own("wide", "x"u8.ToArray(), mode);

        RegularFiles.ReadOwned(path, DotenvNames.MaxBytes, Me).Should().BeOfType<FileReadResult.Unreadable>().Which.Reason.Should().Be(reason);
    }

    [Theory]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead)]
    [InlineData(UnixFileMode.UserRead)]
    public void A_file_only_its_owner_may_write_is_read(UnixFileMode mode)
    {
        OnLinux();
        var path = Own("mine", "x"u8.ToArray(), mode);

        RegularFiles.ReadOwned(path, DotenvNames.MaxBytes, Me).Should().BeOfType<FileReadResult.Content>().Which.Bytes.Should().Equal("x"u8.ToArray());
    }

    [Fact]
    public void A_path_that_is_not_there_is_Missing_whether_it_is_absent_or_under_a_file()
    {
        OnLinux();
        var file = Own("a-file", "x"u8.ToArray());

        RegularFiles.ReadOwned(_root.At("absent"), DotenvNames.MaxBytes, Me).Should().BeOfType<FileReadResult.Missing>();
        RegularFiles.ReadOwned(Path.Combine(file, "below"), DotenvNames.MaxBytes, Me).Should().BeOfType<FileReadResult.Missing>("ENOTDIR is a person who has not signed in, not a refusal");
    }

    [Fact]
    public void A_directory_is_refused_as_not_a_regular_file()
    {
        OnLinux();

        RegularFiles.ReadOwned(_root.At(), DotenvNames.MaxBytes, Me).Should().BeOfType<FileReadResult.Unreadable>().Which.Reason.Should().Contain(RegularFiles.NotRegular).And.Contain("a directory");
    }

    /// <summary>A stream whose <see cref="Length"/> says one thing and whose reads say another.</summary>
    private sealed class LyingStream(long claimedLength, int actualBytes) : Stream
    {
        public int Served { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => claimedLength;

        public override long Position { get => Served; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = Math.Min(count, actualBytes - Served);
            Served += n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

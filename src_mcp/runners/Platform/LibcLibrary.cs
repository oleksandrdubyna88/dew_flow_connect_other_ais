namespace CoaiMcp.Runners.Platform;

/// <summary>The C library the Linux imports in the runners name — one spelling, the one <c>AppendOnlyFile</c> already loads on Linux.</summary>
internal static class LibcLibrary
{
    internal const string Name = "libc";
}

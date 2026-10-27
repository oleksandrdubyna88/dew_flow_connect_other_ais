namespace CoaiMcp.Runners.Kimi;

/// <summary>One running process, as a read-only listing gives it: nothing here can end it.</summary>
/// <remarks><see cref="Name"/> is the image name on Windows and the basename of the <c>/proc/&lt;pid&gt;/exe</c> link on Linux — or, where that link is hidden from this account, the basename of <c>argv[0]</c> (DL10); <see cref="Comm"/> is Linux's <c>comm</c> (the kernel's thread name, empty on Windows).</remarks>
public sealed record ProcessEntry(int Pid, int ParentPid, string Name, string Comm = "");

/// <summary>What a look at the machine's processes produced — a closed union, so "the host could not say" is never an empty list (DL10 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>).</summary>
public abstract record ProcessListing
{
    private ProcessListing()
    {
    }

    /// <summary>Every process this look could judge.</summary>
    public sealed record Listed(IReadOnlyList<ProcessEntry> Processes) : ProcessListing;

    /// <summary>The listing could not be trusted, and why. Never evidence that nobody is running.</summary>
    public sealed record Unreadable(string Reason) : ProcessListing;
}

/// <summary>The seam over "which processes are running": the host's listing in the product, a fake list in a test.</summary>
public interface IProcessList
{
    /// <summary>Every live process now, with its parent's id — or why the host cannot say. Read-only.</summary>
    ProcessListing Snapshot();
}

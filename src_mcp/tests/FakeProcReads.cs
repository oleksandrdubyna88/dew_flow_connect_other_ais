using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Tests;

/// <summary>A <c>/proc</c> that is a dictionary: pid → the text of its <c>stat</c> (or the exception reading it throws), its threads, its owner, its <c>exe</c> link.</summary>
internal sealed class FakeProcReads : IProcReads
{
    public Dictionary<int, Func<string>> Stats { get; } = [];

    public Dictionary<int, int[]> Tasks { get; } = [];

    public uint Owner { get; init; } = 1000;

    public IEnumerable<int> Pids() => Stats.Keys;

    public string StatText(int pid) => Stats.TryGetValue(pid, out var read) ? read() : throw new DirectoryNotFoundException($"/proc/{pid}");

    public IReadOnlyCollection<int> Threads(int pid) => Tasks.TryGetValue(pid, out var threads) ? threads : [pid];

    public uint OwnerUid(int pid) => Owner;

    public string ExeTarget(int pid) => string.Empty;

    public uint OwnUid() => 1000;

    /// <summary>A process with a plain <c>stat</c> line.</summary>
    public FakeProcReads With(int pid, string state, int parent = 1, string comm = "worker")
    {
        Stats[pid] = () => $"{pid} ({comm}) {state} {parent} {pid} {pid} 0 -1 4194560 100 0 0 0 1 2 0 0 20 0 1 0 4000 1000 100 18446744073709551615";

        return this;
    }
}

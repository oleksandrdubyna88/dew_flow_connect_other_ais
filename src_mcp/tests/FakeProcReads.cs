using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Tests;

/// <summary>A <c>/proc</c> that is a dictionary: pid → the text of its <c>stat</c> (or the exception reading it throws), its threads, its owner, its <c>exe</c> link (or the exception reading it throws).</summary>
internal sealed class FakeProcReads : IProcReads
{
    public Dictionary<int, Func<string>> Stats { get; } = [];

    public Dictionary<int, int[]> Tasks { get; } = [];

    public Dictionary<int, uint> Owners { get; } = [];

    public Dictionary<int, Func<string>> Exes { get; } = [];

    public uint Owner { get; init; } = 1000;

    public uint Me { get; init; } = 1000;

    public Func<IEnumerable<int>>? Listed { get; init; }

    public IEnumerable<int> Pids() => Listed is null ? Stats.Keys : Listed();

    public string StatText(int pid) => Stats.TryGetValue(pid, out var read) ? read() : throw new DirectoryNotFoundException($"/proc/{pid}");

    public IReadOnlyCollection<int> Threads(int pid) => Tasks.TryGetValue(pid, out var threads) ? threads : [pid];

    public uint OwnerUid(int pid) => Owners.TryGetValue(pid, out var owner) ? owner : Owner;

    public string ExeTarget(int pid) => Exes.TryGetValue(pid, out var read) ? read() : string.Empty;

    public uint OwnUid() => Me;

    /// <summary>A process with a plain <c>stat</c> line.</summary>
    public FakeProcReads With(int pid, string state, int parent = 1, string comm = "worker")
    {
        Stats[pid] = () => $"{pid} ({comm}) {state} {parent} {pid} {pid} 0 -1 4194560 100 0 0 0 1 2 0 0 20 0 1 0 4000 1000 100 18446744073709551615";

        return this;
    }

    /// <summary>A process with a plain <c>stat</c> line and the <c>exe</c> link it runs (<c>…/bin/kimi</c>, possibly ending in <c> (deleted)</c>).</summary>
    public FakeProcReads Running(int pid, int parent, string exe, string comm = "MainThread", string state = "S")
    {
        Exes[pid] = () => exe;

        return With(pid, state, parent, comm);
    }
}

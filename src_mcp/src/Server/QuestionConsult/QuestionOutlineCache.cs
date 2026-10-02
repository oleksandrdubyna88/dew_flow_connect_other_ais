using CoaiMcp.Runners.Context;

namespace CoaiMcp.Server;

/// <summary>
/// The outline of what exists at HEAD that an api <c>none</c> row is given (A9), built ONCE per repository and HEAD and
/// kept for the last <see cref="Capacity"/> of them (<c>todo/PLAN_question_consultant.md</c>, S4b item 11).
/// </summary>
/// <remarks>
/// <para>The outline costs about six seconds on this checkout (S2 deviation 13: 1,997 files against the empty tree). It
/// used to be built for every question with any <c>none</c> row, before ANY row launched; now it is built only for an
/// api <c>none</c> row and only that row waits for it — and the next question at the same HEAD, from any session of
/// this process, is handed the one already built.</para>
/// <para><b>Bounded</b> (reliability.md, everything that grows has an owner): the last four repository + HEAD pairs, least
/// recently asked first out — an outline is ~170 KB at its cap, so the cache holds well under a megabyte.</para>
/// <para><b>A build that fails is not kept</b>: an empty outline (git refused, nothing outlined) or a fault is forgotten, so
/// the next question tries again. Two questions asking for the same pair while it builds share the one build.</para>
/// </remarks>
public sealed class QuestionOutlineCache(int capacity = QuestionOutlineCache.Capacity)
{
    /// <summary>How many repository + HEAD outlines are kept.</summary>
    public const int Capacity = 4;

    /// <summary>The process's one cache — every fan-out is built per question, so the cache cannot live on one.</summary>
    public static QuestionOutlineCache Shared { get; } = new();

    private readonly Lock _gate = new();

    /// <summary>Most recently asked first.</summary>
    private readonly LinkedList<Entry> _recent = new();

    private sealed record Entry(string Key, Task<string> Outline);

    /// <summary>How many outlines are held right now — for a test of the bound.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _recent.Count;
            }
        }
    }

    /// <summary>The outline of <paramref name="repo"/> at <paramref name="head"/>: the one already built, or <paramref name="build"/>'s.</summary>
    public Task<string> GetAsync(string repo, string head, Func<Task<string>> build)
    {
        var key = RepositoryIdentity.Normalised(repo) + "@" + head;
        lock (_gate)
        {
            if (Find(key) is { } hit)
            {
                _recent.Remove(hit);
                _recent.AddFirst(hit);

                return hit.Value.Outline;
            }

            var source = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _recent.AddFirst(new Entry(key, source.Task));
            while (_recent.Count > capacity)
            {
                _recent.RemoveLast();
            }

            _ = FillAsync(key, source, build);

            return source.Task;
        }
    }

    private LinkedListNode<Entry>? Find(string key)
    {
        for (var node = _recent.First; node is not null; node = node.Next)
        {
            if (node.Value.Key == key)
            {
                return node;
            }
        }

        return null;
    }

    /// <summary>The detached build: its result or its fault reaches every caller through the source, and a failed one is forgotten.</summary>
    private async Task FillAsync(string key, TaskCompletionSource<string> source, Func<Task<string>> build)
    {
        try
        {
            await Task.Yield(); // never under the caller's lock
            var outline = await build();
            if (outline.Length == 0)
            {
                Forget(key, source.Task);
            }

            source.SetResult(outline);
        }
        catch (Exception e)
        {
            // The detached edge: nothing awaits this frame, so the fault travels to the callers through the source.
            Forget(key, source.Task);
            source.SetException(e);
        }
    }

    private void Forget(string key, Task<string> outline)
    {
        lock (_gate)
        {
            if (Find(key) is { } node && ReferenceEquals(node.Value.Outline, outline))
            {
                _recent.Remove(node);
            }
        }
    }
}

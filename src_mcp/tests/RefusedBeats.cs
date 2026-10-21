using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Server;
using Serilog.Core;
using Serilog.Events;

namespace CoaiMcp.Tests;

/// <summary>
/// The write road of a <see cref="QuestionConsultStore"/> that refuses chosen HEARTBEAT-ONLY writes — the write, before a byte
/// reaches the disk, on every OS (a share-mode hold is Windows' alone). A heartbeat-only write is one whose status, stamp and
/// rows equal the previous write's while its heartbeat differs.
/// </summary>
/// <remarks>
/// It remembers the previous write itself, so no test re-derives "is this a beat". <see cref="OnBeat"/> is called with the
/// number of the beat (1, 2, …) BEFORE the file is written; a throw from it is the refused write. The store's own
/// <c>projected</c> callback is not used: it runs AFTER the write, so throwing there would test only the catch (the risk consult
/// of epic 3, point 3).
/// </remarks>
internal sealed class RefusedBeats(Action<int> onBeat)
{
    private QuestionConsultRecord _previous = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
    private int _beats;

    /// <summary>How many heartbeat-only writes were attempted, refused or not.</summary>
    public int Beats => Volatile.Read(ref _beats);

    /// <summary>The store's <c>FileWriter</c>: the beat hook first, then the real write.</summary>
    public void Write(string path, string json)
    {
        var next = JsonSerializer.Deserialize(json, QuestionConsultJsonContext.Default.QuestionConsultRecord)!;
        var beat = IsHeartbeatOnly(_previous, next);
        _previous = next;
        if (beat)
        {
            onBeat(Interlocked.Increment(ref _beats));
        }

        AtomicJson.Write(path, json);
    }

    private static bool IsHeartbeatOnly(QuestionConsultRecord before, QuestionConsultRecord after) =>
        before.Id == after.Id
        && before.Status == after.Status
        && before.UpdatedUtc == after.UpdatedUtc
        && before.HeartbeatUtc != after.HeartbeatUtc
        && before.Rows.Select(r => (r.RowId, r.Status)).SequenceEqual(after.Rows.Select(r => (r.RowId, r.Status)));
}

/// <summary>A Serilog sink that keeps what was said and tells a test the moment a matching event arrives.</summary>
internal sealed class SaidSink(Action<LogEvent> seen) : ILogEventSink
{
    private readonly List<LogEvent> _events = [];

    /// <summary>Every event so far, in order.</summary>
    public IReadOnlyList<LogEvent> Events
    {
        get
        {
            lock (_events)
            {
                return [.. _events];
            }
        }
    }

    public void Emit(LogEvent logEvent)
    {
        lock (_events)
        {
            _events.Add(logEvent);
        }

        seen(logEvent);
    }
}

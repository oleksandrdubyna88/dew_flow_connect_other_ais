using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoaiMcp.Core.Cadence;

namespace CoaiMcp.Server;

/// <summary>A cadence record that could not be read or written — never read as empty, never written blind.</summary>
public sealed class CadenceStoreException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Where a plan's cadence is kept: one JSON file per (repository, plan) under <c>&lt;dataDir&gt;/cadence/</c>
/// (<c>todo/PLAN_consult_on_a_cadence.md</c>, story 2.3).
/// </summary>
/// <remarks>
/// <para><b>Keyed by repository and plan, never by session.</b> With one gate for the whole task a session
/// carries several epics; with one per epic every epic is its own session on its own branch — and in its own
/// worktree, which is why the repository is the git common dir (<see cref="Runners.Context.RepositoryIdentity"/>)
/// and the plan its file name (<see cref="EpicRef.PlanKey"/>, so promotion to <c>research/</c> keeps it).</para>
/// <para><b>Fails CLOSED (D7).</b> A file that exists and cannot be read throws <see cref="CadenceStoreException"/>
/// naming it: a gate that read it as empty would wave through a group nobody consulted on. A missing file is
/// simply a plan nothing was recorded for yet.</para>
/// <para><b>The turn is held across the whole read–modify–write</b> (the epic-1-3 consultation, point 6) —
/// <see cref="SessionTurn"/>, the OS lock file every process on this machine honours, released by the kernel
/// if a server dies holding it. A write-only lock loses one of two closes that arrive together. When the turn
/// cannot be had after its retries the update throws rather than writing blind.</para>
/// <para><b>Growth, stated rather than left open</b> (epic 2's plan round): one record per plan that declares
/// epics, under 2 KB at fourteen epics and three risk items, plus its <c>.turn</c> lock file. A server killed
/// between the temp write and the move leaves ONE <c>.tmp</c> beside the record, which the next write reuses
/// and moves — so nothing accumulates, and the record read is always the last whole one. Records are kept:
/// they are the evidence the plan's own DoD and the phase-0 table read.</para>
/// </remarks>
public sealed class CadenceStore(string dataDir)
{
    private string Directory => Path.Combine(dataDir, "cadence");

    /// <summary>The record's path — a digest, so the name is legal everywhere and says nothing about the repository.</summary>
    public string FileFor(string repoId, string plan)
    {
        // Length-prefixed, so a '#' inside either part cannot make two identities one (epic 2's code
        // round, codex: "/work/a#b" + "c" and "/work/a" + "b#c" were the same key).
        var key = $"{repoId.Length}:{repoId}#{EpicRef.PlanKey(plan)}";
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];

        return Path.Combine(Directory, $"cadence-{digest}.json");
    }

    /// <summary>The plan's cadence, a new one when nothing was recorded; throws when the record is unreadable.</summary>
    public CadenceState Load(string repoId, string plan)
    {
        var file = FileFor(repoId, plan);
        using var turn = SessionTurn.Take(file);

        return Read(file, plan);
    }

    /// <summary>
    /// Applies a change under the plan's turn and writes the result; throws, writing nothing, when the turn
    /// cannot be had or the record cannot be read.
    /// </summary>
    public CadenceState Update(string repoId, string plan, Func<CadenceState, CadenceState> change)
    {
        var file = FileFor(repoId, plan);
        System.IO.Directory.CreateDirectory(Directory);
        using var turn = SessionTurn.Take(file);
        if (!turn.Held)
        {
            throw new CadenceStoreException(
                $"the cadence record {file} is being written by another server and its turn could not be had — try again in a moment");
        }

        var next = change(Read(file, plan)) with { Plan = plan };
        Write(file, next);

        return next;
    }

    private static CadenceState Read(string file, string plan)
    {
        if (!File.Exists(file))
        {
            return new CadenceState { Plan = plan };
        }

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(file), CadenceJsonContext.Default.CadenceState)
                ?? throw new CadenceStoreException($"the cadence record {file} is empty — refusing to read it as a plan with nothing recorded");
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new CadenceStoreException(
                $"the cadence record {file} could not be read ({e.Message}) — refusing to read it as a plan with nothing recorded", e);
        }
    }

    private static void Write(string file, CadenceState state)
    {
        var temp = file + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(state, CadenceJsonContext.Default.CadenceState));
            File.Move(temp, file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new CadenceStoreException($"the cadence record {file} could not be written ({e.Message})", e);
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(CadenceState))]
internal sealed partial class CadenceJsonContext : JsonSerializerContext;

using System.Text.Json;
using CoaiMcp.Core.Context;
using CoaiMcp.Runners.Context;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A REAL <see cref="PanelService"/> over real git with FakeCli, after two live code rounds: what the size tests of E7 start from (S2 reconstructs the sizes,
/// S3 writes them). The repository and the data directory are <c>testsToDelete-…</c> temp folders.
/// </summary>
public abstract class LiveSizedGateBase : FakeCliGateBase
{
    protected const string Code = "CodeReview";

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected string DatabaseFile => Path.Combine(Data, RoundsDb.FileName);

    /// <summary>Open, a plan round, a code round, one more commit, a second code round: the live path records both sizes.</summary>
    protected async Task RecordTwoCodeRoundsLiveAsync()
    {
        var service = Service(_ => null, Temp.At("home"));
        await service.OpenAsync(Repo, "feature");
        await service.ReviewPlanAsync(Repo, "feature", "a plan nobody objects to");
        await service.ResolveAsync(Repo, "feature", "[]");
        Parse(await service.ReviewCodeAsync(Repo, "feature", "main", Scope)).TryGetProperty("error", out _).Should().BeFalse();
        await service.ResolveAsync(Repo, "feature", "[]");
        await File.AppendAllTextAsync(Path.Combine(Repo, "app.cs"), "one more\nand another\n", Ct);
        await Git("add", ".");
        await Git("commit", "-m", "the fix");
        Parse(await service.ReviewCodeAsync(Repo, "feature", "main", Scope, true)).TryGetProperty("error", out _).Should().BeFalse();
    }

    protected List<RoundRecord> LiveCodeRounds() => [.. Saved().Rounds.Where(round => round.Stage == Code)];

    protected static string Shape(RoundSize size) => JsonSerializer.Serialize(new { size.Whole, size.Delta });

    /// <summary>The database's own view of the rounds' sizes: states, origin, gitlinks and every cell row.</summary>
    protected List<string> SizeRows()
    {
        SqliteConnection.ClearAllPools();
        using var db = new SqliteConnection($"Data Source={DatabaseFile};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = """
            SELECT r.number, r.size_state, r.delta_state, r.delta_from, r.size_origin, r.size_gitlinks, c.scope, c.coverage, c.kind, c.files, c.added, c.removed
              FROM rounds r LEFT JOIN round_changes c ON c.round_id = r.id
             WHERE r.stage = 'CodeReview' ORDER BY r.number, c.scope, c.coverage, c.kind
            """;
        using var rows = read.ExecuteReader();
        var found = new List<string>();
        while (rows.Read())
        {
            found.Add(string.Join('|', Enumerable.Range(0, rows.FieldCount).Select(at => rows.IsDBNull(at) ? "NULL" : Convert.ToString(rows.GetValue(at), System.Globalization.CultureInfo.InvariantCulture))));
        }

        return found;
    }

    protected void StripSizes()
    {
        SqliteConnection.ClearAllPools();
        using (var db = new SqliteConnection($"Data Source={DatabaseFile};Pooling=False"))
        {
            db.Open();
            BackfillWorld.Run(db, "UPDATE rounds SET size_state = '', delta_state = '', delta_from = '', size_origin = '', size_gitlinks = -1; DELETE FROM round_changes;");
        }

        var store = new SessionStore(Data);
        var file = store.Load(Repo, "feature")!;
        store.Save(file with { Rounds = [.. file.Rounds.Select(round => round with { Size = null, Gitlinks = null })] });
        SqliteConnection.ClearAllPools();
    }

    /// <summary>Every code round of the stored history, sized in order — each size saved into the journal before the next round is measured.</summary>
    internal async Task<List<(StoredRound Round, SizeOutcome Outcome)>> BackfillSizesAsync()
    {
        using var db = AuthorPeers.OpenReadOnly(Data)!;
        var gate = new StoredHistory(db, new SessionStore(Data)).Gates().Single();
        var sizer = new StoredSizer(new ContextAssembler(Launcher), Launcher, Serilog.Core.Logger.None);
        var outcomes = new List<(StoredRound, SizeOutcome)>();
        foreach (var round in gate.Rounds.Where(round => round.Stage == Code))
        {
            var index = gate.IndexOf(round);
            var outcome = await sizer.MeasureAsync(round, gate.RepoPath, [.. gate.Journal.Take(index)], Ct);
            if (outcome is SizeOutcome.Measured measured)
            {
                gate.Journal[index] = gate.Journal[index] with { Size = measured.Size, Gitlinks = measured.Gitlinks };
            }

            outcomes.Add((round, outcome));
        }

        return outcomes;
    }

}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CoaiMcp.Core.Notices;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server.Keys;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s7 t4 — the pool: the next usable key after the persisted cursor for every fresh session, a cooldown after a limit, dead after a refusal with ONE notice, safe with many callers and processes, no wait when nothing
/// is usable (KEY_POOL_SPEC.md section 5). Scenarios K4, K5, K6, K8, K9 and K10 of the spec at the pool's own surface; the launch wiring of K7 and K9's route rules are story t5. The numbers in the names are the task's <c>dod</c> ids.
/// </summary>
[System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
public sealed class AKeyPoolTests
{
    private static KeyPool PoolOf(KeyWorld world) => new(world.Access());

    private static string IdOf(string tag) => KeyWorld.IdOf(KeyWorld.Token(tag));

    private static string PickedId(KeyPool pool) => pool.Pick().Should().BeOfType<PickResult.Picked>().Subject.Secret.Id;

    private static KeyRow Row(KeyWorld world, string tag) => world.Admin.List(false).Value!.Rows.Single(row => row.Id == IdOf(tag));

    private static KeyWorld ThreeKeys()
    {
        var world = new KeyWorld();
        world.AddKey("A", "a");
        world.AddKey("B", "b");
        world.AddKey("C", "c");

        return world;
    }

    // ---------- d1: the ring and the cursor ----------

    [Fact]
    public void D1_ThreeKeysAndSixPicks_GiveABCABC_AndEachAnswerIsTheWholeKey()
    {
        using var world = ThreeKeys();
        var pool = PoolOf(world);

        var picks = Enumerable.Range(0, 6).Select(_ => pool.Pick().Should().BeOfType<PickResult.Picked>().Subject).ToList();

        picks.Select(pick => pick.Secret.Id).Should().Equal(IdOf("a"), IdOf("b"), IdOf("c"), IdOf("a"), IdOf("b"), IdOf("c"));
        picks.Select(pick => pick.Label).Should().Equal("A", "B", "C", "A", "B", "C");
        picks.Select(pick => pick.Secret.Reveal()).Should().Equal(KeyWorld.Token("a"), KeyWorld.Token("b"), KeyWorld.Token("c"), KeyWorld.Token("a"), KeyWorld.Token("b"), KeyWorld.Token("c"));
    }

    [Fact]
    public void D1_ThePickWritesTheCursor_TheLastUsedTime_AndOneMoreLaunch_AndTheCursorSurvivesANewProcess()
    {
        using var world = ThreeKeys();
        PickedId(PoolOf(world)).Should().Be(IdOf("a"));
        world.Clock.Advance(TimeSpan.FromMinutes(5));
        PickedId(PoolOf(world)).Should().Be(IdOf("b"));

        var afterRestart = new KeyPool(new KeyStoreAccess(world.DataDir, world.Clock, world.Notices.Add));

        Row(world, "b").Launches.Should().Be(1);
        Row(world, "b").LastUsedUtc.Should().Be(world.Clock.Now.UtcDateTime);
        Row(world, "c").Launches.Should().Be(0);
        PickedId(afterRestart).Should().Be(IdOf("c"), "the cursor is in pool.json, not in the process");
        using var pool = JsonDocument.Parse(File.ReadAllText(Path.Combine(world.KeysRoot, "pool.json")));
        pool.RootElement.GetProperty("cursor").GetString().Should().Be(IdOf("c"));
    }

    [Fact]
    public void D1_TheRingIsOrderedByAddedTimeAndThenId()
    {
        using var world = new KeyWorld();
        foreach (var tag in new[] { "x", "y", "z" })
        {
            world.AddKey(tag, tag, step: TimeSpan.Zero);
        }

        var pool = PoolOf(world);
        var picked = Enumerable.Range(0, 3).Select(_ => PickedId(pool)).ToList();

        picked.Should().Equal(new[] { "x", "y", "z" }.Select(IdOf).OrderBy(id => id, StringComparer.Ordinal), "keys added at the same instant go by id");
    }

    [Fact]
    public void D1_ACursorWhoseKeyIsGone_StartsAtTheFirstKey()
    {
        using var world = ThreeKeys();
        var pool = PoolOf(world);
        PickedId(pool);
        PickedId(pool).Should().Be(IdOf("b"));
        world.Admin.Remove(IdOf("b")).Ok.Should().BeTrue();

        PickedId(pool).Should().Be(IdOf("a"), "the cursor named a key that is gone: the ring starts at its first key");
    }

    [Fact]
    public void D1_AKeyThatIsDisabled_Dead_Cooling_Missing_OrWideModeIsNotPicked()
    {
        using var world = new KeyWorld();
        world.AddKey("disabled", "d1");
        world.AddKey("dead", "d2");
        world.AddKey("cooling", "d3");
        world.AddKey("missing", "d4");
        world.AddKey("wide", "d5");
        world.AddKey("good", "g");
        var pool = PoolOf(world);
        world.Admin.Disable(IdOf("d1"));
        pool.Record(IdOf("d2"), new KeyLaunchReport(KeyLaunchEnd.AuthRefused));
        pool.Record(IdOf("d3"), new KeyLaunchReport(KeyLaunchEnd.Limit, "Resets in 2h"));
        File.Delete(world.KeyFile("d4"));
        File.SetUnixFileMode(world.KeyFile("d5"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);

        var picked = Enumerable.Range(0, 4).Select(_ => PickedId(pool)).ToList();

        picked.Should().OnlyContain(id => id == IdOf("g"));
    }

    [Fact]
    public void D1_APickOnADeviceThatNeverHadAKey_AnswersEmpty_AndMakesNoFolder()
    {
        using var world = new KeyWorld();

        PoolOf(world).Pick().Should().BeOfType<PickResult.Empty>();

        Directory.Exists(Path.Combine(world.DataDir, "keys")).Should().BeFalse();
        world.Notices.Should().BeEmpty("an empty pool is the launch road's notice, not the pool's");
    }

    [Fact]
    public void D1_APickAndACounter_NeverMoveTheListRevision()
    {
        using var world = new KeyWorld();
        world.AddKey("one", "a");
        var before = world.Admin.List(false).Value!.AdminRevision;
        var pool = PoolOf(world);

        pool.Pick().Should().BeOfType<PickResult.Picked>();
        pool.Record(IdOf("a"), new KeyLaunchReport(KeyLaunchEnd.Throttled));
        pool.Record(IdOf("a"), new KeyLaunchReport(KeyLaunchEnd.Succeeded));
        pool.Record(IdOf("a"), new KeyLaunchReport(KeyLaunchEnd.Limit, "Resets in 12m"));

        world.Admin.List(false).Value!.AdminRevision.Should().Be(before, "a pick and a counter are not a change by a person");
    }

    [Fact]
    public void D1_APickedKeyPrintsAsItsId()
    {
        using var world = ThreeKeys();

        var pick = PoolOf(world).Pick();

        pick.ToString().Should().NotContain(KeyWorld.Sentinel, "a record that holds the secret prints its ToString");
    }

    // ---------- d2: many callers, many processes ----------

    [Fact]
    public async Task D2_EightCallersPick400TimesAtOnce_EveryPickIsOneWholeKey_TheLaunchesDifferByOne_NoFileIsTorn()
    {
        using var world = ThreeKeys();
        var tokens = new HashSet<string>(new[] { "a", "b", "c" }.Select(KeyWorld.Token));
        var results = new ConcurrentBag<string>();
        var failures = new ConcurrentBag<string>();
        var torn = 0;
        var stop = false;
        var reader = Task.Run(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                foreach (var file in Directory.GetFiles(world.KeysRoot, "*.json"))
                {
                    try
                    {
                        using var _ = JsonDocument.Parse(File.ReadAllText(file));
                    }
                    catch (JsonException)
                    {
                        Interlocked.Increment(ref torn);
                    }
                }
            }
        }, TestContext.Current.CancellationToken);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            var pool = PoolOf(world);
            for (var turn = 0; turn < 50; turn++)
            {
                switch (pool.Pick())
                {
                    case PickResult.Picked picked:
                        results.Add(picked.Secret.Reveal());
                        break;
                    case var other:
                        failures.Add(other.GetType().Name);
                        break;
                }
            }
        }, TestContext.Current.CancellationToken)));
        Volatile.Write(ref stop, true);
        await reader;

        failures.Should().BeEmpty();
        results.Should().HaveCount(400);
        results.Should().OnlyContain(text => tokens.Contains(text), "every pick answers one whole key");
        var launches = new[] { "a", "b", "c" }.Select(tag => Row(world, tag).Launches).ToList();
        launches.Sum().Should().Be(400);
        (launches.Max() - launches.Min()).Should().BeLessThanOrEqualTo(1, "the launches of the keys differ by one at most");
        torn.Should().Be(0, "a file is written whole or not at all: a reader never sees half of it");
        Directory.GetFiles(world.KeysRoot, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task D2_AHolderThatIsKilledInTheMiddle_LeavesThePoolUsable_AndAPickWaitsNoLongerThanTheLockWindow()
    {
        using var world = ThreeKeys();
        var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli"), ["pool-lock-hold", Path.Combine(world.KeysRoot, "pool.lock"), "60000"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var holder = Process.Start(info)!;
        try
        {
            (await holder.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken)).Should().Be("held");
            var watch = Stopwatch.StartNew();

            var stopped = PoolOf(world).Pick();
            watch.Stop();

            stopped.Should().BeOfType<PickResult.Failed>().Which.Sentence.Should().Contain("busy");
            watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(8), "the lock is waited for two seconds, not for ever");
        }
        finally
        {
            holder.Kill(entireProcessTree: true);
            await holder.WaitForExitAsync(TestContext.Current.CancellationToken);
        }

        PickedId(PoolOf(world)).Should().Be(IdOf("a"), "the kernel freed the lock; nothing was half written");
    }

    [Fact]
    public async Task D2_EightRealProcessesAtOnce_AddTwentyFourKeys_NoneIsLostRefusedOrTorn()
    {
        using var data = TempDir.For("coai-keyspool-");
        var tags = Enumerable.Range(0, 24).Select(number => $"p{number:00}").ToList();

        var codes = await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
            tags.Where((_, index) => index % 8 == worker).Select(tag => RealAdd(data.Path, tag)).ToList(), TestContext.Current.CancellationToken)));

        codes.SelectMany(list => list).Should().OnlyContain(code => code == 0, "the lock is waited for, so a crowd of processes all get their turn");
        var list = OneShotProcess.Run(data.Path, "--keys", "list", "--json");
        using var answer = JsonDocument.Parse(list.Out);
        answer.RootElement.GetProperty("rows").GetArrayLength().Should().Be(24);
        answer.RootElement.GetProperty("adminRevision").GetInt32().Should().Be(24);
        answer.RootElement.GetProperty("rows").EnumerateArray().All(row => row.GetProperty("problem").GetString() == string.Empty).Should().BeTrue();
        Directory.GetFiles(Path.Combine(data.Path, "keys", "claude"), "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void D2_UnderASharedFolder_TheFilesStay0660_AndTheFolder2770_AfterPicks()
    {
        using var world = new KeyWorld(shared: true);
        File.SetUnixFileMode(world.DataDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.SetGroup);
        Assert.SkipUnless((File.GetUnixFileMode(world.DataDir) & UnixFileMode.SetGroup) != 0, "this file system does not keep the setgid bit");
        world.AddKey("A", "a");
        world.AddKey("B", "b");
        var pool = PoolOf(world);

        for (var turn = 0; turn < 6; turn++)
        {
            PickedId(pool);
        }

        File.GetUnixFileMode(world.KeysRoot).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.SetGroup);
        foreach (var file in Directory.GetFiles(world.KeysRoot).Where(file => !file.EndsWith("pool.lock", StringComparison.Ordinal)))
        {
            File.GetUnixFileMode(file).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite, $"{Path.GetFileName(file)} is rewritten on every pick and stays group-writable");
        }
        // NOT RUN HERE: a second operating-system user picking from the same folder. This machine has one user; the run is recorded as not made.
    }

    private static int RealAdd(string dataDir, string tag) => KeysBinary.Run(dataDir, KeyWorld.Token(tag) + "\n", "--keys", "add", "--label", tag).Code;

    // ---------- d3: limit, throttle, success ----------

    [Fact]
    public void D3_ALimitWithAStatedReset_KeepsTheKeyOutUntilThen_AndThenItIsPickedAgain_WithNoNotice()
    {
        using var world = ThreeKeys();
        var pool = PoolOf(world);
        var reset = world.Clock.Now.AddHours(2);

        var recorded = pool.Record(IdOf("b"), new KeyLaunchReport(KeyLaunchEnd.Limit, "weekly limit", reset));

        recorded.Value!.CooldownUntilUtc.Should().Be(reset.UtcDateTime);
        Row(world, "b").LimitStops.Should().Be(1);
        var seen = Enumerable.Range(0, 4).Select(_ => PickedId(pool)).ToList();
        seen.Should().Equal(IdOf("a"), IdOf("c"), IdOf("a"), IdOf("c")); // B is skipped while it cools
        world.Clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(1));
        Enumerable.Range(0, 3).Select(_ => PickedId(pool)).Should().Contain(IdOf("b"), "B is picked again after the reset");
        world.Notices.Should().BeEmpty("a limit is not a notice");
    }

    [Fact]
    public void D3_ALimitWithNoStatedReset_UsesTheParsersGuess_Doubling_AndTheVendorsOwnWordsWhenItNamedATime()
    {
        using var world = ThreeKeys();
        var pool = PoolOf(world);
        var start = world.Clock.Now;

        pool.Record(IdOf("a"), new KeyLaunchReport(KeyLaunchEnd.Limit, "rate limit exceeded"));
        Row(world, "a").CooldownUntilUtc.Should().Be((start + CooldownParser.Unparseable).UtcDateTime, "30 minutes");
        world.Clock.Advance(CooldownParser.Unparseable + TimeSpan.FromSeconds(1));
        pool.Record(IdOf("a"), new KeyLaunchReport(KeyLaunchEnd.Limit, "rate limit exceeded"));
        Row(world, "a").CooldownUntilUtc.Should().Be((world.Clock.Now + CooldownParser.Unparseable * 2).UtcDateTime, "the second one in a row doubles");
        Row(world, "a").ConsecutiveCooldowns.Should().Be(2);

        pool.Record(IdOf("b"), new KeyLaunchReport(KeyLaunchEnd.Limit, "Resets in 12m"));
        Row(world, "b").CooldownUntilUtc.Should().Be((world.Clock.Now + TimeSpan.FromMinutes(12)).UtcDateTime, "a time the vendor named is believed");
    }

    [Fact]
    public void D3_AThrottle_IsSixtySeconds_WithNoCount_AndNeverShortensALongerCooldown()
    {
        using var world = ThreeKeys();
        var pool = PoolOf(world);

        pool.Record(IdOf("a"), new KeyLaunchReport(KeyLaunchEnd.Throttled));
        Row(world, "a").CooldownUntilUtc.Should().Be(world.Clock.Now.UtcDateTime.AddSeconds(60));
        Row(world, "a").ConsecutiveCooldowns.Should().Be(0);
        PickedId(pool).Should().Be(IdOf("b"));
        world.Clock.Advance(TimeSpan.FromSeconds(61));
        Row(world, "a").Usable.Should().BeTrue();

        pool.Record(IdOf("c"), new KeyLaunchReport(KeyLaunchEnd.Limit, "Resets in 2h"));
        var longer = Row(world, "c").CooldownUntilUtc;
        pool.Record(IdOf("c"), new KeyLaunchReport(KeyLaunchEnd.Throttled));
        Row(world, "c").CooldownUntilUtc.Should().Be(longer, "a throttle never shortens a limit");
    }

    [Fact]
    public void D3_ALaunchThatEndedWell_ClearsTheCooldownAndTheCount()
    {
        using var world = ThreeKeys();
        var pool = PoolOf(world);
        pool.Record(IdOf("a"), new KeyLaunchReport(KeyLaunchEnd.Limit, "rate limit exceeded"));
        pool.Record(IdOf("a"), new KeyLaunchReport(KeyLaunchEnd.Limit, "rate limit exceeded"));

        pool.Record(IdOf("a"), new KeyLaunchReport(KeyLaunchEnd.Succeeded));

        Row(world, "a").CooldownUntilUtc.Should().BeNull();
        Row(world, "a").ConsecutiveCooldowns.Should().Be(0);
        Row(world, "a").LimitStops.Should().Be(2, "the stops stay counted");
    }

    [Fact]
    public void D3_ALimitOnOneKey_CoolsEveryKeyOfTheSameAccount_AndOnlyThose()
    {
        using var world = new KeyWorld();
        world.AddKey("one", "a", account: "me@example.com");
        world.AddKey("two", "b", account: "ME@example.com");
        world.AddKey("other", "c", account: "you@example.com");
        world.AddKey("none", "d");
        var pool = PoolOf(world);

        pool.Record(IdOf("a"), new KeyLaunchReport(KeyLaunchEnd.Limit, "Resets in 3h"));

        Row(world, "b").CooldownUntilUtc.Should().Be(Row(world, "a").CooldownUntilUtc, "the limit is the account's");
        Row(world, "b").ConsecutiveCooldowns.Should().Be(0, "only the key that hit it counts the stop");
        Row(world, "c").CooldownUntilUtc.Should().BeNull();
        Row(world, "d").CooldownUntilUtc.Should().BeNull();
    }

    [Fact]
    public void D3_TheVendorsWordsAreRecordedAsTheNote_WithAnyPartOfTheKeyScrubbed()
    {
        using var world = ThreeKeys();
        var pool = PoolOf(world);

        pool.Record(IdOf("a"), new KeyLaunchReport(KeyLaunchEnd.Limit, $"usage limit reached for {KeyWorld.Token("a")} Resets in 1h"));

        var note = Row(world, "a").Note;
        note.Should().NotBeEmpty();
        note.Should().NotContain(KeyWorld.Sentinel);
        File.ReadAllText(world.MetaFile("a")).Should().NotContain(KeyWorld.Sentinel);
    }

    // ---------- d4: dead ----------

    [Fact]
    public void D4_ARefusedKey_IsDead_WithOneNotice_NamingTheLabelTheIdAndTheCure_AndIsNeverPicked()
    {
        using var world = ThreeKeys();
        var pool = PoolOf(world);

        pool.Record(IdOf("b"), new KeyLaunchReport(KeyLaunchEnd.AuthRefused));
        pool.Record(IdOf("b"), new KeyLaunchReport(KeyLaunchEnd.AuthRefused));

        var row = Row(world, "b");
        row.State.Should().Be("dead");
        row.DeadReason.Should().Be("auth");
        row.AuthStops.Should().Be(2);
        var notice = world.Notices.Should().ContainSingle("ONE notice per key, not one per refusal").Subject;
        notice.Code.Should().Be(ServerNoticeCodes.ClaudeKeyDead);
        notice.Subject.Should().Be(IdOf("b"));
        (notice.Title + notice.Detail + notice.Cure).Should().Contain("'B'").And.Contain(IdOf("b")).And.Contain("--keys remove").And.Contain("--keys enable");
        ServerNoticeLine.Of(notice).Should().NotContain(KeyWorld.Sentinel);
        Enumerable.Range(0, 6).Select(_ => PickedId(pool)).Should().NotContain(IdOf("b"));
    }

    [Theory]
    [InlineData("Transient")]
    [InlineData("Throttled")]
    [InlineData("Succeeded")]
    [InlineData("Limit")]
    public void D4_ANetworkErrorA5xxATimeoutOrALimit_NeverMakesAKeyDead(string endName)
    {
        using var world = ThreeKeys();

        PoolOf(world).Record(IdOf("a"), new KeyLaunchReport(Enum.Parse<KeyLaunchEnd>(endName), "HTTP 503 upstream connect error or timeout"));

        Row(world, "a").State.Should().Be("enabled");
        world.Notices.Should().BeEmpty();
    }

    [Fact]
    public void D4_Enable_EndsDead_AndTheKeyIsPickedAgain()
    {
        using var world = ThreeKeys();
        var pool = PoolOf(world);
        pool.Record(IdOf("a"), new KeyLaunchReport(KeyLaunchEnd.AuthRefused));

        world.Admin.Enable(IdOf("a"));

        PickedId(pool).Should().Be(IdOf("a"));
    }

    [Fact]
    public void D4_AnExposedKey_IsDeadWithTheReasonExposed_AndItsOwnNotice_AndALaterRefusalDoesNotHideIt()
    {
        using var world = ThreeKeys();
        var pool = PoolOf(world);

        pool.Record(IdOf("c"), new KeyLaunchReport(KeyLaunchEnd.Exposed));
        pool.Record(IdOf("c"), new KeyLaunchReport(KeyLaunchEnd.AuthRefused));

        Row(world, "c").DeadReason.Should().Be("exposed");
        var notice = world.Notices.Should().ContainSingle().Subject;
        notice.Code.Should().Be(ServerNoticeCodes.ClaudeKeyExposed);
        notice.Cure.Should().Contain("Revoke");
        ServerNoticeLine.Of(notice).Should().NotContain(KeyWorld.Sentinel);
        PoolOf(world).Pick().Should().BeOfType<PickResult.Picked>();
    }

    [Fact]
    public void D4_ARefusedKeyThatWasAlreadyDeadByExposure_StillHasOneNoticeOfEachKindAtMost()
    {
        using var world = ThreeKeys();
        var pool = PoolOf(world);
        pool.Record(IdOf("a"), new KeyLaunchReport(KeyLaunchEnd.AuthRefused));

        pool.Record(IdOf("a"), new KeyLaunchReport(KeyLaunchEnd.Exposed));
        pool.Record(IdOf("a"), new KeyLaunchReport(KeyLaunchEnd.Exposed));

        world.Notices.Select(notice => notice.Code).Should().Equal(ServerNoticeCodes.ClaudeKeyDead, ServerNoticeCodes.ClaudeKeyExposed);
    }

    [Fact]
    public void D4_TheThreeCodes_AreInTheListOfCodes_SoTheRedactorGuardSeesThem()
    {
        ServerNoticeCodes.All.Should().Contain([ServerNoticeCodes.ClaudeKeyDead, ServerNoticeCodes.ClaudeKeyExposed, ServerNoticeCodes.ClaudeKeysExhausted]);
        new[] { ServerNoticeCodes.ClaudeKeyDead, ServerNoticeCodes.ClaudeKeyExposed, ServerNoticeCodes.ClaudeKeysExhausted }.Should().OnlyContain(code => Redaction.SafeText(code, 100) == code);
    }

    // ---------- d5: nothing usable ----------

    [Fact]
    public void D5_WhenEveryKeyCools_APickAnswersNone_WithTheEarliestFreeTime_TheCounts_AndOneNotice()
    {
        using var world = ThreeKeys();
        var pool = PoolOf(world);
        pool.Record(IdOf("a"), new KeyLaunchReport(KeyLaunchEnd.Limit, "Resets in 3h"));
        pool.Record(IdOf("b"), new KeyLaunchReport(KeyLaunchEnd.Limit, "Resets in 1h"));
        pool.Record(IdOf("c"), new KeyLaunchReport(KeyLaunchEnd.Limit, "Resets in 2h"));
        var watch = Stopwatch.StartNew();

        var none = pool.Pick().Should().BeOfType<PickResult.Unusable>().Subject;
        watch.Stop();

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2), "no wait: the answer is at once");
        none.EarliestFreeUtc.Should().Be(world.Clock.Now.UtcDateTime.AddHours(1));
        none.Counts.Should().Be(new KeyCounts(0, 3, 0, 0, 0));
        var notice = world.Notices.Should().ContainSingle().Subject;
        notice.Code.Should().Be(ServerNoticeCodes.ClaudeKeysExhausted);
        notice.Title.Should().Contain("3 cooling").And.Contain("free at");
    }

    [Fact]
    public void D5_TheExhaustedNotice_IsWrittenAtMostOnceInThirtyMinutes()
    {
        using var world = ThreeKeys();
        var pool = PoolOf(world);
        foreach (var tag in new[] { "a", "b", "c" })
        {
            pool.Record(IdOf(tag), new KeyLaunchReport(KeyLaunchEnd.Limit, "Resets in 10h"));
        }

        pool.Pick();
        world.Clock.Advance(TimeSpan.FromMinutes(29));
        pool.Pick();
        pool.Pick();
        world.Notices.Should().ContainSingle("29 minutes later it is still the same news");
        world.Clock.Advance(TimeSpan.FromMinutes(2));
        pool.Pick();

        world.Notices.Should().HaveCount(2);
        world.Notices.Should().OnlyContain(notice => notice.Code == ServerNoticeCodes.ClaudeKeysExhausted);
    }

    [Fact]
    public void D5_ADisabledOrDeadKey_IsStillAKey_SoThePoolIsNotEmpty()
    {
        using var world = new KeyWorld();
        world.AddKey("one", "a");
        world.AddKey("two", "b");
        var pool = PoolOf(world);
        world.Admin.Disable(IdOf("a"));
        pool.Record(IdOf("b"), new KeyLaunchReport(KeyLaunchEnd.AuthRefused));

        var none = pool.Pick().Should().BeOfType<PickResult.Unusable>().Subject;

        none.EarliestFreeUtc.Should().BeNull("only a person ends these");
        none.Counts.Should().Be(new KeyCounts(0, 0, 1, 1, 0));
    }

    // ---------- d6: a session keeps its key ----------

    [Fact]
    public void D6_AResumedTurn_GetsTheSameKey_AlsoWhenItIsDisabledOrCooling_AndItIsNotACountedLaunch()
    {
        using var world = ThreeKeys();
        var pool = PoolOf(world);
        var picked = pool.Pick().Should().BeOfType<PickResult.Picked>().Subject.Secret;
        world.Admin.Disable(picked.Id);
        pool.Record(picked.Id, new KeyLaunchReport(KeyLaunchEnd.Limit, "Resets in 2h"));

        var resumed = pool.Resume(picked.Id);

        resumed.Ok.Should().BeTrue(resumed.Refusal);
        resumed.Value!.Id.Should().Be(picked.Id);
        resumed.Value.Reveal().Should().Be(KeyWorld.Token("a"));
        Row(world, "a").Launches.Should().Be(1, "a resume is the same launch's next turn");
    }

    [Fact]
    public void D6_ARemovedDeadOrMissingKey_AnswersGone()
    {
        using var world = new KeyWorld();
        world.AddKey("removed", "r");
        world.AddKey("dead", "d");
        world.AddKey("lost", "l");
        var pool = PoolOf(world);
        world.Admin.Remove(IdOf("r"));
        pool.Record(IdOf("d"), new KeyLaunchReport(KeyLaunchEnd.AuthRefused));
        File.Delete(world.KeyFile("l"));

        foreach (var tag in new[] { "r", "d", "l" })
        {
            var gone = pool.Resume(IdOf(tag));
            gone.Ok.Should().BeFalse(tag);
            gone.Refusal.Should().StartWith("gone");
        }
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("k-0123456789ab/../../x")]
    [InlineData("")]
    [InlineData("not an id")]
    public void D6_AnIdThatIsNotAnId_AnswersGone_AndOpensNoPath(string id)
    {
        using var world = ThreeKeys();

        PoolOf(world).Resume(id).Ok.Should().BeFalse();
        PoolOf(world).Record(id, new KeyLaunchReport(KeyLaunchEnd.AuthRefused)).Ok.Should().BeFalse();
    }
}

using CoaiServer;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// One question, one job — even when the answer to the submit was lost on the way back.
/// </summary>
public sealed class IdempotencyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private static JobRecord Job(
        string id, string key, string prompt = "explain this", string email = "dev@example.com") =>
        new(id, email, "codex", "m", "Architecture", prompt, JobStatus.Queued,
            Now, Now + JobTransitions.DefaultQueueWait, TimeSpan.FromMinutes(2),
            IdempotencyKey: key,
            Fingerprint: Idempotency.Fingerprint("codex", "m", "Architecture", prompt, JobKind.Review, 120));

    [Fact]
    public void RepeatingOneKeyReturnsTheFirstJobAndMakesNoSecond()
    {
        var jobs = new JobStore();

        var (first, _, _) = jobs.Submit(Job("a", "turn-1"));
        var (again, refusal, position) = jobs.Submit(Job("b", "turn-1"));

        refusal.Should().Be(SubmitRefusal.None, "a retry is not an error");
        again!.Id.Should().Be(first!.Id, "the client cannot tell a retry from the original, which is the point");
        position.Should().Be(1);
        jobs.All().Should().ContainSingle("the second submit must not have queued anything");
    }

    [Fact]
    public void OneKeyReusedForADifferentQuestionIsRefusedRatherThanAnswered()
    {
        // Silently returning the first job would hand somebody the answer to a question they did not
        // ask, while looking exactly like success — which is worse than any error. (codex and gemini,
        // plan round, independently.)
        var jobs = new JobStore();
        jobs.Submit(Job("a", "turn-1", "explain this"));

        var (job, refusal, _) = jobs.Submit(Job("b", "turn-1", "explain something else"));

        job.Should().BeNull();
        refusal.Should().Be(SubmitRefusal.KeyUsedForSomethingElse);
        jobs.All().Should().ContainSingle();
    }

    [Fact]
    public void TwoPeopleMayChooseTheSameKey()
    {
        // A key is whatever a client feels like generating, so they will collide. Handing one person
        // the other's job would be a data leak wearing an optimisation's clothes.
        var jobs = new JobStore();

        jobs.Submit(Job("a", "turn-1", email: "one@example.com"));
        var (mine, refusal, _) = jobs.Submit(Job("b", "turn-1", email: "two@example.com"));

        refusal.Should().Be(SubmitRefusal.None);
        mine!.Id.Should().Be("b");
        jobs.All().Should().HaveCount(2);
    }

    [Fact]
    public void AJobWithNoKeyIsNeverMatchedAgainstAnother()
    {
        // Sending no key means "I accept that a retry may make a second job", which is what every
        // client did before this existed. Two of them must not collapse into one.
        var jobs = new JobStore();

        jobs.Submit(Job("a", ""));
        jobs.Submit(Job("b", ""));

        jobs.All().Should().HaveCount(2);
    }

    [Fact]
    public void TheKeyDiesWithTheJobItNamed()
    {
        // The mapping lives on the record rather than in an index beside it, so there is no second
        // lifetime to keep in step — and no way for a repeat to be handed the id of a job that has
        // already been forgotten. (gemini, plan round.)
        var jobs = new JobStore(keepFinished: TimeSpan.FromMinutes(5));
        jobs.Submit(Job("a", "turn-1"));
        jobs.Cancel("a", "dev@example.com", Now);

        jobs.Sweep(Now.AddMinutes(6));
        jobs.All().Should().BeEmpty();

        var (fresh, refusal, _) = jobs.Submit(Job("b", "turn-1"));

        refusal.Should().Be(SubmitRefusal.None);
        fresh!.Id.Should().Be("b", "the key was forgotten with the job, so this is a new question");
    }

    [Fact]
    public void ARepeatOfAFinishedJobStillAnswersWithIt()
    {
        // Within the keep window a retry must find the ANSWER, not start the work again. This is the
        // ordinary shape of the failure: the submit was accepted, the response was lost, and by the
        // time the person presses send again the vendor has already answered.
        var jobs = new JobStore();
        jobs.Submit(Job("a", "turn-1"));
        var claimed = jobs.TryClaim("codex", "slot", Now);
        jobs.Finish(JobTransitions.Succeed(claimed!.Value.Job, "the answer", 1, 2, Now));

        var (again, refusal, _) = jobs.Submit(Job("b", "turn-1"));

        refusal.Should().Be(SubmitRefusal.None);
        again!.Id.Should().Be("a");
        again.Answer.Should().Be("the answer");
    }

    [Fact]
    public void AFingerprintTellsTwoRequestsApartWhereverTheyDiffer()
    {
        var baseline = Idempotency.Fingerprint("codex", "m", "Architecture", "p", JobKind.Review, 60);

        Idempotency.Fingerprint("codex", "m", "Architecture", "p", JobKind.Review, 60)
            .Should().Be(baseline, "the same request twice is the same fingerprint");

        foreach (var different in new[]
        {
            Idempotency.Fingerprint("claude", "m", "Architecture", "p", JobKind.Review, 60),
            Idempotency.Fingerprint("codex", "n", "Architecture", "p", JobKind.Review, 60),
            Idempotency.Fingerprint("codex", "m", "Conventions", "p", JobKind.Review, 60),
            Idempotency.Fingerprint("codex", "m", "Architecture", "q", JobKind.Review, 60),
            Idempotency.Fingerprint("codex", "m", "Architecture", "p", JobKind.Chat, 60),
            Idempotency.Fingerprint("codex", "m", "Architecture", "p", JobKind.Review, 61),
        })
        {
            different.Should().NotBe(baseline);
        }
    }

    [Fact]
    public void FieldsCannotBeShuffledAcrossTheirBoundaries()
    {
        // Length-prefixed, so ("ab","c") and ("a","bc") are two fingerprints. A prompt is a whole
        // conversation quoted from somewhere else, so no separator is safe on its own.
        Idempotency.Fingerprint("ab", "c", "r", "p", JobKind.Review, 60)
            .Should().NotBe(Idempotency.Fingerprint("a", "bc", "r", "p", JobKind.Review, 60));
    }

    [Fact]
    public void AKeyIsCheckedForShapeBeforeItIsUsed()
    {
        Idempotency.Refusal(null).Should().BeNull("not sending one is allowed");
        Idempotency.Refusal("").Should().BeNull();
        Idempotency.Refusal("3afb5834-0c1e-4a9b-9f2d-5c7e8a1b2c3d").Should().BeNull();
        Idempotency.Refusal("turn_1.2:3").Should().BeNull();

        Idempotency.Refusal("has a space").Should().Contain("idempotency key");
        Idempotency.Refusal("../../etc").Should().NotBeNull();
        Idempotency.Refusal(new string('k', 129)).Should().NotBeNull();
        Idempotency.Refusal(new string('k', 128)).Should().BeNull("128 is the bound, not past it");
    }
}

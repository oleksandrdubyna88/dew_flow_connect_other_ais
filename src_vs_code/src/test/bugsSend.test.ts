import assert from 'node:assert/strict';
import { test } from 'node:test';

import { EXITS, mayStart, outcomeOf, readSummary, sendLabel, waiting } from '../bugsSend';
import { EMPTY_CORPUS, type BugCorpus, type SendRun } from '../roundsDb';

/**
 * What a send is allowed to be, before anything is spawned and after it comes back.
 *
 * <p>Every one of these is a plan-round finding. The three the reviewers converged on — no durable
 * in-flight state, no preflight before the credential moves, and an outcome matrix missing the exits
 * that are about THIS EXTENSION rather than about the pairs — are the first three groups below.</p>
 */

const corpus = (over: Partial<BugCorpus> = {}): BugCorpus => ({
  ...EMPTY_CORPUS,
  funnel: { ...EMPTY_CORPUS.funnel, collected: 4 },
  // What a send would OFFER, which is not what has been kept: after a successful send the kept
  // count is unchanged and this is zero, which is the whole of what seven findings were about.
  sendable: 4,
  read: true,
  ...over,
});

const send = (over: Partial<SendRun> = {}): SendRun => ({ ...EMPTY_CORPUS.lastSend, ...over });

const running = (over: Partial<SendRun> = {}): SendRun =>
  send({ id: 's1', state: 'running', server: 'https://bugs.example', offered: 4, ...over });

test('a send with a server, a key and something to send may start', () => {
  assert.equal(mayStart({ server: 'https://bugs.example', key: 'k', corpus: corpus() }), undefined);
});

/** THE ONE THE PREFLIGHT EXISTS FOR: the address is judged before the credential moves. */
test('an address a key must not cross is refused before anything is spawned', () => {
  const refusal = mayStart({ server: 'http://collector.example', key: 'k', corpus: corpus() });

  assert.equal(refusal?.kind, 'unsafe-server');
  assert.match(refusal?.why ?? '', /clear text|will not be sent/u);
});

test('loopback over plain http is allowed, because nothing leaves the machine', () => {
  assert.equal(mayStart({ server: 'http://127.0.0.1:8110', key: 'k', corpus: corpus() }), undefined);
});

test('no server at all is its own sentence, not a bad-arguments error', () => {
  const refusal = mayStart({ server: '   ', key: 'k', corpus: corpus() });

  assert.equal(refusal?.kind, 'no-server');
  assert.match(refusal?.why ?? '', /nothing can be sent to nowhere/u);
});

test('no key is its own sentence, and says where a key is kept', () => {
  const refusal = mayStart({ server: 'https://bugs.example', key: '', corpus: corpus() });

  assert.equal(refusal?.kind, 'no-key');
  assert.match(refusal?.why ?? '', /secret storage/u);
  assert.match(refusal?.why ?? '', /never in settings/u, 'the reason a person can act on');
});

test('nothing waiting is refused rather than sent as an empty batch', () => {
  const nothing = corpus({ sendable: 0 });

  assert.equal(mayStart({ server: 'https://bugs.example', key: 'k', corpus: nothing })?.kind,
    'nothing-to-send');
});

/**
 * THE DURABLE ONE: a send that is already happening refuses the second.
 *
 * <p>Three plan reviewers found this independently. The funnel cannot answer it — a pair is marked
 * only on an acknowledgement, so during a send the counts say what they said before it — which is
 * why the server records the run and this reads THAT.</p>
 */
test('a send that is already running refuses to start a second one', () => {
  const refusal = mayStart({
    server: 'https://bugs.example',
    key: 'k',
    corpus: corpus({ lastSend: running() }),
  });

  assert.equal(refusal?.kind, 'already-sending');
  assert.match(refusal?.why ?? '', /survives a reload/u);
});

test('a send that has finished does not block the next one', () => {
  const done = corpus({ lastSend: running({ state: 'done', finishedUtc: 'u' }) });

  assert.equal(mayStart({ server: 'https://bugs.example', key: 'k', corpus: done }), undefined);
});

/** A state this build has never heard of means KEEP WAITING, which is the safe direction. */
test('a send in a state this build does not know is treated as still running', () => {
  const strange = corpus({ lastSend: running({ state: 'paused-for-approval' }) });

  assert.equal(mayStart({ server: 'https://bugs.example', key: 'k', corpus: strange })?.kind,
    'already-sending');
});

test('the summary is read field by field, and a renamed field is a zero rather than a crash', () => {
  const read = readSummary('{"offered":5,"accepted":3,"duplicate":1,"refused":1,"trouble":""}');

  assert.deepEqual(read, { offered: 5, accepted: 3, duplicate: 1, refused: 1, trouble: '' });
  assert.deepEqual(readSummary('{"offered":"five"}'),
    { offered: 0, accepted: 0, duplicate: 0, refused: 0, trouble: '' });
});

/**
 * THE ONE THE LIVE CONTRACT TEST FOUND: the real summary is pretty-printed.
 *
 * <p>The reader took the last LINE, and the server writes its summary with indentation on — so the
 * last line of the real output is `}`. Every test here passed, because every fixture was written on
 * one line: both sides agreed with each other and disagreed with the binary.</p>
 */
test('a summary printed across several lines is read, because that is what the server prints', () => {
  const real = ['{', '  "offered": 0,', '  "accepted": 0,', '  "duplicate": 0,',
    '  "refused": 0,', '  "trouble": ""', '}'].join('\r\n');

  assert.deepEqual(readSummary(real), { offered: 0, accepted: 0, duplicate: 0, refused: 0, trouble: '' });
});

test('the summary is found after progress above it, wherever the lines fall', () => {
  const stdout = 'sending 3 pair(s)\n3 accepted, 0 already held, 0 refused\n{"offered":3,"accepted":3}';

  assert.equal(readSummary(stdout)?.accepted, 3);
});

test('something that is not JSON at all is no summary, rather than an empty one', () => {
  assert.equal(readSummary('the process died'), undefined);
  assert.equal(readSummary(''), undefined);
  assert.equal(readSummary('[1,2,3]'), undefined, 'an array is not a summary either');
});

test('a clean run says what it did, in the counts the CLI itself reports', () => {
  const outcome = outcomeOf(EXITS.fine, '{"offered":3,"accepted":3,"duplicate":0,"refused":0}');

  assert.equal(outcome.kind, 'sent');
  assert.match(outcome.said, /3 sent, 0 already held, 0 refused/u);
});

test('a run that refused some of them says a refusal is OUR defect, not the pair’s', () => {
  const outcome = outcomeOf(EXITS.fine, '{"offered":3,"accepted":2,"duplicate":0,"refused":1}');

  assert.equal(outcome.kind, 'partly');
  assert.match(outcome.said, /our own normaliser/u);
});

test('already held is reported as the success it is', () => {
  const outcome = outcomeOf(EXITS.fine, '{"offered":2,"accepted":0,"duplicate":2,"refused":0}');

  assert.equal(outcome.kind, 'sent');
  assert.match(outcome.said, /2 already held/u);
});

/** A transport failure is not a bad pair, and the sentence has to say the retry is safe. */
test('a server that could not be reached says try again, and why that is safe', () => {
  const outcome = outcomeOf(EXITS.couldNotReach, '{"offered":3,"trouble":"connection refused"}');

  assert.equal(outcome.kind, 'trouble');
  assert.match(outcome.said, /connection refused/u);
  assert.match(outcome.said, /Nothing was marked as sent/u);
  assert.match(outcome.said, /already holds/u, 'the reason pressing Send again cannot duplicate');
});

/**
 * And a run that got some through before it broke must not claim nothing was marked.
 *
 * <p>Each batch is marked as the server acknowledges it, so a send that failed on its third batch
 * has two batches’ worth already sent. Telling the person otherwise sends them looking for
 * pairs that are not missing. (Code round, codex.)</p>
 */
test('a transport failure after some batches says what got through', () => {
  const outcome = outcomeOf(EXITS.couldNotReach,
    '{"offered":400,"accepted":180,"duplicate":20,"refused":0,"trouble":"connection reset"}');

  assert.equal(outcome.kind, 'trouble');
  assert.match(outcome.said, /200 got through before it stopped/u);
  assert.doesNotMatch(outcome.said, /Nothing was marked/u, 'two hundred pairs were');
});

/** An installed server older than the send path, which is about this machine, not the pairs. */
test('an old binary says to update it, and that nothing was lost', () => {
  const outcome = outcomeOf(EXITS.tooOld, '');

  assert.equal(outcome.kind, 'too-old');
  assert.match(outcome.said, /older than sending/u);
  assert.match(outcome.said, /nothing was lost/u);
});

test('a refused request names the address or the key rather than the pairs', () => {
  const outcome = outcomeOf(EXITS.refusedTheRequest, '');

  assert.equal(outcome.kind, 'refused');
  assert.match(outcome.said, /address or the key/u);
});

test('a database that could not be opened is its own answer', () => {
  assert.equal(outcomeOf(EXITS.noDatabase, '').kind, 'no-database');
});

/** A run whose result nobody can read is never reported as a success or as a refusal. */
test('a finished run with no readable summary claims nothing', () => {
  const outcome = outcomeOf(EXITS.fine, 'killed');

  assert.equal(outcome.kind, 'unreadable');
  assert.match(outcome.said, /would not say what it did/u);
  assert.doesNotMatch(outcome.said, /sent/u, 'it must not claim a count it never read');
});

test('the button says what is happening, with the denominator when there is one', () => {
  assert.equal(sendLabel(running({ sent: 1, offered: 4 }), corpus()), 'Sending… 1 of 4');
  assert.equal(sendLabel(running({ offered: 0 }), corpus()), 'Sending…');
  assert.equal(sendLabel(send(), corpus()), 'Send 4 pair(s)');
  assert.equal(sendLabel(send(), corpus({ sendable: 0 })), 'Send');
});

/**
 * A SERVER TOO OLD TO SAY must not read as an empty queue.
 *
 * <p>Collapsing an absent count into zero disabled the Send button for ever: the send was never
 * attempted, the CLI never answered 64, and nobody was ever told that the thing to do was update the
 * server. The fallback is deliberately optimistic, because it sends the person into the one path
 * that can explain itself. (Code round 2, codex.)</p>
 */
test('a server too old to count falls back to what was kept, so the send can still be tried', () => {
  const old = corpus({ funnel: { ...EMPTY_CORPUS.funnel, collected: 7 }, sendable: undefined });

  assert.equal(waiting(old), 7);
  assert.equal(mayStart({ server: 'https://bugs.example', key: 'k', corpus: old }), undefined,
    'the run must be allowed to start so that exit 64 can say what is wrong');
  assert.equal(sendLabel(send(), old), 'Send 7 pair(s)');
});

/** THE ONE SEVEN FINDINGS WERE ABOUT: what is waiting is what a send would OFFER. */
test('what is waiting is the unsent count, not everything ever kept', () => {
  assert.equal(waiting(corpus()), 4);
  assert.equal(waiting(EMPTY_CORPUS), 0);

  // Everything kept, nothing left to send: the button must not offer to send it again.
  const allSent = corpus({ funnel: { ...EMPTY_CORPUS.funnel, collected: 9 }, sendable: 0 });
  assert.equal(waiting(allSent), 0);
  assert.equal(sendLabel(send(), allSent), 'Send');
  assert.equal(mayStart({ server: 'https://bugs.example', key: 'k', corpus: allSent })?.kind,
    'nothing-to-send');
});

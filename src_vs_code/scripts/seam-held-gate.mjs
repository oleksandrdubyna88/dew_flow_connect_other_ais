/**
 * The seam leg for a HELD GATE: the notice the REAL `coai-mcp` writes at `call_human` is read by the extension's own
 * parser, the answer window offers exactly the decisions the notice lists — Proceed included — and a Proceed the
 * extension's own writer produces is the only thing that releases the gate (`todo/PLAN_only_a_person_releases_the_gate.md`,
 * §11.2).
 *
 * <p>The notice is a file written by C# and read by TypeScript, and the answer is a file written by TypeScript and read by C#.
 * The two suites each hold their own half against a fixture they wrote themselves, which is exactly how a renamed field stays
 * green on both sides: the server could call the list `offers`, or the extension could read `decision` where the file says
 * `decisions`, and every test would still pass. So nothing here is a fixture. A held gate is produced by the shipped binary
 * over stdio — a plan round against the stand-in vendor CLI that answers one blocking finding, with the panel's own writer
 * saying a single round and a threshold of zero — and what it left on disk is handed to the extension's real parser and its
 * real {@link answerChoices}.</p>
 *
 * <p>Four claims, in order; the first that fails is the answer.</p>
 * <ol>
 *   <li>the round ends in `call_human` and writes ONE notice card;</li>
 *   <li>the extension's parser keeps the notice's `decisions`, it names Proceed, and the answer window offers exactly the
 *   listed decisions in the listed order;</li>
 *   <li>the caller's own `humanDecision: "proceed"` is refused over the wire with the sentence that names the card, and the gate
 *   is still held;</li>
 *   <li>the extension's OWN `answerJson` for a Proceed, written beside the notice, releases the hold through `resolve` with no
 *   argument — the stage advances.</li>
 * </ol>
 *
 * <p>The home directory, the Windows profile and `CODEX_HOME` are pointed at a scratch folder, so nothing the person has signed in to
 * on this machine can be reached from here; the stand-in CLI is the only vendor and nothing is billed.</p>
 *
 * <p><b>Its own module</b>, as every leg after the sixth is: `run-seam.mjs` is past the repository's 800-line ceiling. What it needs
 * from the runner — the session factory, the reply reader, the binary and the stand-in CLI — is handed in.</p>
 */
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

// The EXTENSION's own writer, parser and answer window — every one of them imported, none re-derived.
const { serverSettingsJson } = await import('../out/serverSettingsFile.js');
const { vendorsFrom } = await import('../out/vendors.js');
const { DEFAULTS } = await import('../out/settingsShape.js');
const { parseEscalation } = await import('../out/escalations.js');
const { answerChoices, answerJson } = await import('../out/escalationAnswer.js');

const PLAN = '# PLAN — the held gate\n\n## Build order\n\n1. one\n2. two\n';

const ONE_BLOCKING_FINDING = JSON.stringify({
  findings: [{
    severity: 'blocking', category: 'reliability', file: '', line: 0,
    title: 'the poll has no timeout', why: 'a hung peer hangs the loop', fix: 'add one',
  }],
});

const ROW = {
  id: 'codex', runtime: 'codex', model: '', enabled: true, plan: true, code: true, baseUrl: '',
  pricePerMillionIn: 0, pricePerMillionOut: 0,
};

/**
 * Run the leg. Answers what it saw when every claim held; ends the process through `fail` when one did not.
 *
 * @param {{serverSession: Function, answerOf: Function, fakeCli: string, fail: Function}} runner
 */
export async function heldGateSeam({ serverSession, answerOf, fakeCli, fail }) {
  const root = mkdtempSync(join(tmpdir(), 'coai-seam-held-'));
  const home = mkdtempSync(join(tmpdir(), 'coai-seam-held-home-'));
  const repo = mkdtempSync(join(tmpdir(), 'coai-seam-held-repo-'));
  const forget = forgetting([root, home, repo]);
  const stop = async (session, why) => {
    await session.killAndWait();
    forget();
    fail(why);
  };

  try {
    scratchRepo(repo);
  } catch (e) {
    forget();
    fail(`the scratch repository for the held-gate leg could not be made: ${e.message}`);
  }
  // The panel's own writer: one plan round, a threshold of zero, the stand-in CLI as the only vendor. Nothing is typed as JSON here.
  const settings = { ...DEFAULTS, thresholds: { ...DEFAULTS.thresholds, PlanCritique: 0 } };
  writeFileSync(join(root, 'settings.json'), serverSettingsJson(settings, vendorsFrom([{ ...ROW, executablePath: fakeCli }]), '9.9.9'), 'utf8');

  const session = serverSession({
    COAI_DATA_DIR: root,
    HOME: home,
    USERPROFILE: home,
    CODEX_HOME: join(home, '.codex'),
    FAKECLI_MODE: 'vendor',
    FAKECLI_STDOUT: ONE_BLOCKING_FINDING,
    FAKECLI_OUTFILE_TEXT: ONE_BLOCKING_FINDING,
    FAKECLI_EXIT: '0',
  });

  const held = await hold(session, repo, answerOf);
  if (held !== '') {
    await stop(session, held);
  }
  const noticed = notice(root);
  if (noticed.why !== '') {
    await stop(session, noticed.why);
  }
  const offered = offers(noticed.card);
  if (offered.why !== '') {
    await stop(session, offered.why);
  }
  const claimed = await theClaimIsRefused(session, repo, answerOf);
  if (claimed !== '') {
    await stop(session, claimed);
  }
  const released = await thePersonsProceedReleases(session, repo, root, noticed.card, answerOf);
  if (released.why !== '') {
    await stop(session, released.why);
  }

  await session.close();
  forget();

  return { decisions: offered.decisions, stage: released.stage };
}

/** Claim 1: `open`, then a plan round that ends in `call_human`. Answers why it did not, or `''`. */
async function hold(session, repo, answerOf) {
  try {
    await session.ready;
    await session.call('open', { repoPath: repo, branch: 'main' });
    const round = answerOf(await session.call('review_plan', { repoPath: repo, branch: 'main', planText: PLAN }));

    return round.verdict === 'call_human'
      ? ''
      : `the plan round did not end in call_human, so there was no held gate to look at. It answered: ${JSON.stringify(round).slice(0, 400)}`;
  } catch (e) {
    return `the binary could not run a plan round to a held gate: ${e.message}`;
  }
}

/** The one notice card the server wrote, parsed by the EXTENSION's parser — or why there is none. */
function notice(root) {
  const dir = join(root, 'escalations');
  const files = existsSync(dir)
    ? readdirSync(dir).filter((name) => name.endsWith('.json') && !name.endsWith('.answer.json'))
    : [];
  if (files.length !== 1) {
    return { why: `a call_human writes ONE notice card, and the server left ${files.length} under ${dir}: ${files.join(', ') || '(nothing)'}`, card: undefined };
  }
  const card = parseEscalation(readFileSync(join(dir, files[0]), 'utf8'), dir);

  return card === undefined || card.kind !== 'notice'
    ? { why: `the extension's parser did not read the server's notice as a notice (${files[0]}): ${JSON.stringify(card ?? null).slice(0, 300)}`, card: undefined }
    : { why: '', card };
}

/** Claim 2: the notice lists Proceed, and the answer window offers exactly what the notice lists. */
function offers(card) {
  const listed = card.decisions;
  if (!Array.isArray(listed) || !listed.includes('proceed')) {
    return {
      why: `the notice the server wrote does not list the decisions it offers, or does not list proceed — or the extension reads another field name. `
        + `It lists: ${JSON.stringify(listed ?? null)}`,
      decisions: [],
    };
  }
  const shown = answerChoices(card).map((choice) => choice.decision);
  if (JSON.stringify(shown) !== JSON.stringify(listed)) {
    return {
      why: `the answer window offers ${JSON.stringify(shown)} for a notice that lists ${JSON.stringify(listed)}: the two halves disagree about which decisions a card offers`,
      decisions: [],
    };
  }

  return { why: '', decisions: shown };
}

/** Claim 3: the caller's own argument, over the real wire, is refused with the sentence that names the card — and the gate holds. */
async function theClaimIsRefused(session, repo, answerOf) {
  const decisions = JSON.stringify([{ finding: 0, action: 'reject', reason: 'the timeout lives in the caller' }]);
  let said;
  try {
    said = answerOf(await session.call('resolve', { repoPath: repo, branch: 'main', decisions, humanDecision: 'proceed' }));
  } catch (e) {
    return `the binary could not answer a resolve that carried humanDecision: ${e.message}`;
  }
  if (typeof said.stage === 'string') {
    return `the caller's own humanDecision released the gate: the stage is now ${said.stage}. Only an answer on the notice card may.`;
  }

  return String(said.error ?? '').includes('Proceed despite the findings')
    ? ''
    : `the argument was refused, but not with the sentence that names the card. It said: ${JSON.stringify(said).slice(0, 300)}`;
}

/** Claim 4: the extension's own `answerJson` for a Proceed, beside the notice, releases the hold through a plain `resolve`. */
async function thePersonsProceedReleases(session, repo, root, card, answerOf) {
  const label = answerChoices(card).find((choice) => choice.decision === 'proceed')?.label ?? '';
  writeFileSync(join(root, 'escalations', `${card.id}.answer.json`), answerJson(card.id, label, new Date().toISOString(), 'proceed'), 'utf8');
  let said;
  try {
    said = answerOf(await session.call('resolve', { repoPath: repo, branch: 'main', decisions: '[]' }));
  } catch (e) {
    return { why: `the binary could not answer a resolve after the person's Proceed: ${e.message}`, stage: '' };
  }

  return said.stage === 'CodeReview'
    ? { why: '', stage: said.stage }
    : {
      why: `the extension's own Proceed answer did not release the gate (the file's key for the decision, the card id or the notice rule changed on one side). `
        + `resolve said: ${JSON.stringify(said).slice(0, 400)}`,
      stage: '',
    };
}

/** A throwaway repository with one commit on `main`: `open` needs a real checkout to resolve a branch in. */
function scratchRepo(dir) {
  const git = (...args) => execFileSync('git', args, { cwd: dir, stdio: ['ignore', 'ignore', 'pipe'] });
  git('init', '--initial-branch=main');
  git('config', 'user.email', 'seam@example.invalid');
  git('config', 'user.name', 'seam');
  mkdirSync(join(dir, 'src'), { recursive: true });
  writeFileSync(join(dir, 'src', 'Poll.cs'), 'class Poll { }\n', 'utf8');
  git('add', '-A');
  git('commit', '-m', 'the committed state');
}

/** Remove the leg's directories, and never let that removal be what the run reports (the sixth leg's rule). */
function forgetting(dirs) {
  return () => {
    for (const dir of dirs) {
      try {
        rmSync(dir, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
      } catch (e) {
        console.error(`seam: could not remove ${dir} (${e.code ?? e.message}); it is left behind`);
      }
    }
  };
}

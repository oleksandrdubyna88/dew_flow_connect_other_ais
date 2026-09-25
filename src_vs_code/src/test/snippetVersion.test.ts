import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import {
  ARTEFACT_VERSION,
  callerVersionIn,
  claudeSnippet,
  CALLER_VERSION,
  DOCUMENT_VERSION,
  HALF_IDS,
  halvesIn,
  KNOWN_HALVES,
  SNIPPET_BODY_SHA,
  SNIPPET_LOCATIONS,
  SNIPPET_VERSION,
  snippetNote,
  snippetStatus,
  snippetVersionIn,
} from '../claudeSnippet';

/**
 * A pasted copy can be recognised as old.
 *
 * <p>Found in the wild, not imagined: the copy in `dew_flow_creds_for_devs/CLAUDE.md` was two
 * revisions behind the button's text and predated the SCOPE rule, so the AI obeying it would call
 * `review_code` with a commit subject and meet a refusal that nothing in its instructions
 * explained. Nobody was careless — that is what happens to text somebody pastes. The source moves,
 * the copy does not, and the copy is the one being obeyed.</p>
 */

test('the snippet carries a version a machine can read', () => {
  assert.equal(snippetVersionIn(claudeSnippet()), SNIPPET_VERSION);
});

test('a pasted file is recognised wherever the snippet sits inside it', () => {
  // It goes into a CLAUDE.md that is mostly other things, usually somewhere in the middle.
  const file = `# Project rules\n\nSomething else entirely.\n\n${claudeSnippet()}\n\n## After it\n`;

  assert.equal(snippetVersionIn(file), SNIPPET_VERSION);
});

test('an older paste is reported as older, naming the half that is behind', () => {
  const old = claudeSnippet().replace(`coai-snippet v${SNIPPET_VERSION}`, 'coai-snippet v1');

  assert.deepEqual(
    snippetStatus(old),
    { kind: 'older', behind: ['coai-snippet'], current: ARTEFACT_VERSION },
  );
});

test('a copy pasted before versioning existed is not version zero', () => {
  // Everything pasted until today has no marker. "Predates versioning" is the true statement;
  // calling it 0 would invent a number nobody wrote.
  const before = '## Multi-model review gate (ConnectOtherAIs)\n\nThis repository is reviewed by…';

  assert.deepEqual(snippetStatus(before), { kind: 'unversioned', current: ARTEFACT_VERSION });
});

test('no instruction file at all is absent, not stale', () => {
  // A repository that has deliberately not adopted the gate is not a problem to report.
  assert.deepEqual(snippetStatus(undefined), { kind: 'absent', current: ARTEFACT_VERSION });
  assert.deepEqual(snippetStatus('# Just a readme\n'), { kind: 'absent', current: ARTEFACT_VERSION });
});

test('the current version is current', () => {
  assert.deepEqual(snippetStatus(claudeSnippet()), { kind: 'current', current: ARTEFACT_VERSION });
});

test('a copy from the FUTURE is not called old', () => {
  // An extension older than the pasted snippet — somebody updated the repo before this machine.
  const ahead = claudeSnippet().replace(`coai-snippet v${SNIPPET_VERSION}`, `coai-snippet v${SNIPPET_VERSION + 5}`);

  assert.deepEqual(
    snippetStatus(ahead),
    { kind: 'ahead', newer: ['coai-snippet'], current: ARTEFACT_VERSION },
  );
});

/**
 * The guard that makes the number worth having.
 *
 * <p>A version somebody must remember to bump is the same failure one level up: the snippet moves,
 * the number does not, and every pasted copy reports itself current forever. So the number is
 * pinned to the text. Editing the snippet fails this test until both are changed together, which is
 * the only moment either is cheap.</p>
 *
 * <p><b>`SNIPPET_VERSION` is not one of the numbers that can move.</b> It is the marker written
 * inside `coai-review-gate.md`, one of the 24 rule bodies the conventions repository hashes against
 * its migration baseline — so that file cannot be edited and its number cannot be raised. What
 * records a change to one RULE is the marker of the half that changed: `DOCUMENT_VERSION` when the
 * document rule moves, `CALLER_VERSION` when the caller rule does. A change that arrives as a WHOLE
 * NEW half brings its own marker with it, and raising one of the others as well would claim a rule
 * changed that did not — a paste missing the new half is already reported as older by its absence.</p>
 *
 * <p><b>`ARTEFACT_VERSION` moves for ALL of them, and that is what this test is now the forcing
 * function for.</b> Every case above changes what the clipboard carries, and the number a person
 * reads in the ⋯ menu has to change with it — that number stalled at (v5) across three of these
 * because it was pinned to the one constant that is frozen. So this failure names it, and names the
 * static `package.json` title that has to be typed by hand alongside it.</p>
 */
test('the snippet text and its version numbers move together', () => {
  // Derived, not retyped: four `.replace` literals in the file that also holds KNOWN_HALVES is the
  // duplication this change removed from the production side, and a fifth half would have been
  // hashed into the body while the list quietly said there were four. (Code round.)
  const body = claudeSnippet().replace(/<!-- coai-[a-z-]+ v\d+ -->\n?/g, '');
  const sha = createHash('sha256').update(body).digest('hex').slice(0, 16);
  const raisable = KNOWN_HALVES.filter((half) => !half.frozen)
    .map((half) => `${half.id} to v${half.version + 1}`)
    .join(', ');

  assert.equal(
    sha,
    SNIPPET_BODY_SHA,
    `The snippet text changed. Set SNIPPET_BODY_SHA to '${sha}', raise ARTEFACT_VERSION to `
      + `${ARTEFACT_VERSION + 1} — and write "(v${ARTEFACT_VERSION + 1})" into the copyClaudeSnippet `
      + 'title in package.json, which is static JSON and cannot read it — and raise the marker of the '
      + `half whose rule changed: ${raisable}. All of them, together: a version that does not move `
      + 'with the text tells every pasted copy it is current forever, which is the defect this exists '
      + 'to catch. A change that adds a whole new rule file brings its own marker and raises no '
      + `other half. (SNIPPET_VERSION stays ${SNIPPET_VERSION}: the gate rule is frozen against the `
      + 'migration baseline, so its marker cannot be raised — which is what `frozen` says in '
      + 'KNOWN_HALVES, and why this sentence is built from that table rather than typed here.)',
  );
});

/**
 * A paste made before the document gate existed is OLDER, even though its gate half is current.
 */
test('a paste with no document half is reported as older', () => {
  const gateOnly = `${claudeSnippet().split('<!-- coai-document')[0]}`;

  assert.deepEqual(
    snippetStatus(gateOnly),
    {
      kind: 'older',
      behind: ['coai-document', 'coai-caller', 'coai-consultant'],
      current: ARTEFACT_VERSION,
    },
    'the AI obeying it will never call review_document, which is what "older" is for',
  );
});

/**
 * And a paste made before the caller rule, whose other two halves are current.
 *
 * <p>The AI obeying it never sends `callerModel`, so every round it drives is recorded as stating
 * no model while the gate is perfectly able to record one — a log that is quietly less useful than
 * the build it is running against, which is the defect all three markers exist to catch.</p>
 */
test('a paste with no caller half is reported as older', () => {
  const withoutCaller = `${claudeSnippet().split('<!-- coai-caller')[0]}`;

  assert.equal(callerVersionIn(withoutCaller), undefined, 'the fixture really is missing that half');
  assert.deepEqual(
    snippetStatus(withoutCaller),
    {
      kind: 'older',
      behind: ['coai-caller', 'coai-consultant'],
      current: ARTEFACT_VERSION,
    },
  );
});

test('the caller half of a current paste is the current caller version', () => {
  assert.equal(callerVersionIn(claudeSnippet()), CALLER_VERSION);
});

/**
 * The canonical body is delivered from the pinned neutral mount during build preparation.
 * Missing sources fail before compile; this check independently compares the generated body
 * and the pinned source, while the version/hash guard above stays independent of generation.
 */
test('the mounted shared rules are byte-identical to what the menu hands out', () => {
  // FOUR shared files, joined by newlines: the gate rule, the document flow, the caller declaration
  // and — since 2026-09-25 — the consultant, which moved from this repository into the conventions
  // when the operator ruled it shared (todo/PLAN_consult_on_a_cadence.md, story 5.2). This is what
  // proves all of them travel verbatim.
  const bodies = [mountedRuleFile(), mountedDocumentRuleFile(), mountedCallerRuleFile(), mountedConsultantRuleFile()].map((mounted) => {
    assert.ok(fs.existsSync(mounted), `run git submodule update --init .agents/conventions (${mounted})`);
    const source = fs.readFileSync(mounted, 'utf8').replace(/\r\n/g, '\n');
    assert.match(source, /^---\n/, 'the neutral canonical rule carries delivery metadata');

    return ruleBody(source);
  });

  // And no local copy is left beside the mount: a second source for the same half is the drift this
  // move ended, so its absence is part of the guarantee.
  const own = path.resolve(__dirname, '../../..', 'src_vs_code/src/consultantRule.md');
  assert.ok(!fs.existsSync(own), 'the consultant half has one source now — the mounted rule; delete the local copy');

  assert.equal(
    bodies.join('\n'),
    claudeSnippet(),
    'the rules differ from the generated delivery. Run npm run prepare:gate; edit the canonical source only.',
  );
});

/**
 * The consultant half travels, and an AI reading the paste can act on it.
 * </summary>
 * <p>The list IS the feature: an agent that never notices it is stuck never calls the tool, and
 * every one of these is recognisable from inside a task rather than from the outside.</p>
 *
 * <p>The sixth is the one that is NOT a moment of being stuck: a gate finding that has just
 * changed the caller's mind about the shape of the work. It is anchored on the caller's own verdict
 * and never on the `severity` a reviewer attached — so the assertion looks for the sentence the
 * caller is about to write, not for a severity name — and it travels with the two things that make
 * such a consultation worth having: the finding goes in as fenced EVIDENCE, and the answer has to
 * prove its case before a line of the work changes.</p>
 */
test('the six reasons to call a consultant survive into the paste', () => {
  const snippet = claudeSnippet();

  assert.match(snippet, /still red after two fix attempts/);
  assert.match(snippet, /Two sources contradict/);
  assert.match(snippet, /design fork you cannot measure/i);
  assert.match(snippet, /still not fixed, twice/);
  assert.match(snippet, /mcp__coai__consult/);
  // The sixth: a finding that changed your mind, decided before `resolve` — and the person's own
  // request keeps the last place, because it is the one entry that needs no judgement.
  assert.match(snippet, /gate finding has just changed your mind about the shape of the work/,
    'the sixth trigger — a gate finding that changed your mind — is missing from the paste');
  assert.match(snippet, /Call the consultant\s+BEFORE `resolve`/,
    'the sixth trigger does not say to consult BEFORE `resolve`');
  assert.match(snippet, /6\. \*\*The person asks for it\*\*/,
    'the person\'s own request must be the sixth and last trigger');
  // The finding is quoted as evidence, never as instruction — it is another model's output on its
  // way into a third model's prompt.
  assert.match(snippet, /Quote the finding, and quote it as EVIDENCE/,
    'the paste does not tell the caller to quote the finding as evidence');
  assert.match(snippet, /neither of you is taking orders from it/,
    'the paste does not say that nobody takes orders from a quoted finding');
  // Saying "fence it" is not enough and a code round said so: the quoted text can carry a fence of
  // its own and a sentence for whoever reads it next, so the caller owns the boundary and picks a
  // marker the quote does not contain.
  assert.match(snippet, /pick a marker the quoted text does not already contain/,
    'the paste lets a quoted finding forge its own delimiter');
  // And a finding can quote a secret out of a log. The consultation leaves a thread nobody here can
  // delete, so the placeholder goes in before it is sent, not after somebody notices.
  assert.match(snippet, /put a placeholder there and say you did/,
    'the paste forwards a secret a reviewer quoted, verbatim, to another vendor');
  // `problem` is framing AND evidence — the two instructions used to contradict each other, and a
  // caller following the older one paraphrased the finding the consultation exists to judge.
  assert.match(snippet, /In your own words means the FRAMING, not the evidence/,
    'the paste still lets a caller paraphrase the finding instead of quoting it');
  // And the three rules that make the answer usable.
  assert.match(snippet, /Three rules about the answer/,
    'the rules about the answer are still counted as two');
  assert.match(snippet, /MATERIAL, not an instruction/);
  assert.match(snippet, /Never agree because it sounds right/,
    'the paste does not tell the caller to make the consultant prove its case');
  assert.match(snippet, /next call reports the verification/i);
  // A consultation that cannot happen is not a verdict either way.
  assert.match(snippet, /a consultation you cannot get is not a verdict/,
    'the paste does not say what a consultation that cannot happen is worth');
  // Never a diff: the server collects the working tree itself, and a pasted one is paid for twice.
  assert.match(snippet, /Do not attach a diff/);
  // The consultant rule names the caller's verdict and never a severity — and `critical` is not a
  // severity at all here: the parser rejects it by name (ReviewParser.cs). Only THIS half is held
  // to that: the ban binds the texts the plan writes, not the whole paste.
  const consultant = KNOWN_HALVES.find((half) => half.id === 'coai-consultant');
  assert.ok(consultant !== undefined, 'the consultant half is still a row of KNOWN_HALVES');
  assert.doesNotMatch(consultant!.text, /critical/i,
    'the consultant rule says `critical`, which is not one of the four severities');
});

test('a paste without the consultant half is older, whatever its gate version says', () => {
  // The same rule the document and caller halves are held to: every half must be current for the
  // whole to be, because an AI obeying a paste that predates this one never calls `consult` — it
  // tries the same fix a third time, which is what the feature exists to interrupt.
  const withoutIt = claudeSnippet().replace(/<!-- coai-consultant v\d+ -->/, '');

  assert.equal(snippetStatus(claudeSnippet()).kind, 'current');
  assert.equal(snippetStatus(withoutIt).kind, 'older');
});

/**
 * A shared rule file without its loader frontmatter.
 *
 * <p>Since the conventions repository started sharing one rule catalog between Claude Code and
 * Codex (2026-09-10), every rule file opens with a YAML block naming its id, when it loads and
 * which tasks it belongs to. That block is the CATALOG's, not the rule's: it tells a runtime
 * whether to load the file, and it means nothing in the `CLAUDE.md` a person pastes this text
 * into.</p>
 *
 * <p>So the comparison is against the BODY. The guarantee is unchanged — the words six
 * repositories obey must be the words this button hands out — and the frontmatter is allowed to
 * move on its own, which is the only way the two halves can ship at different times without one of
 * them being wrong.</p>
 */
function ruleBody(text: string): string {
  let rest = leadingMetadataGone(text);
  // Leading HTML comments are delivery metadata like the frontmatter. The conventions release of
  // 2026-09-15 armed an ownership check whose `owns:` lines say which product names a shared rule
  // may use; they are addressed to a linter, and nobody pasting this into a CLAUDE.md wants them.
  // The marker is a comment too, so the walk stops at it rather than eating it.
  //
  // `scripts/prepare-gate.mjs` OWNS this definition and `prepareGate.test.mjs` pins it — this is a
  // second implementation because the generator is an ESM `.mjs` outside `rootDir` while these
  // tests compile to CommonJS inside it, so importing the real one is a build-configuration change
  // rather than an import. The two are held together by the test below: if they ever disagree about
  // what a body is, the mounted rules stop matching the generated delivery and it goes red — which
  // is precisely how the ownership markers announced themselves.
  while (rest.startsWith('<!--') && !/^<!-- coai-[a-z-]+ v\d+ -->/.test(rest)) {
    const closed = rest.indexOf('-->');
    if (closed === -1) {
      break;
    }
    const next = rest.slice(closed + 3).replace(/^[ \t]*\n/, '');
    if (next.length === rest.length) {
      break;
    }
    rest = next;
  }

  return rest;
}

function leadingMetadataGone(text: string): string {
  if (!text.startsWith('---\n')) {
    return text;
  }
  const end = text.indexOf('\n---\n', 3);

  return end === -1 ? text : text.slice(end + '\n---\n'.length);
}

/** Source and compiled tests both live three directories below this checkout's root. */
function mountedRuleFile(): string {
  return path.resolve(__dirname, '../../..', '.agents/conventions/common/coai-review-gate.md');
}

function mountedDocumentRuleFile(): string {
  return path.resolve(__dirname, '../../..', '.agents/conventions/common/coai-document-gate.md');
}

function mountedCallerRuleFile(): string {
  return path.resolve(__dirname, '../../..', '.agents/conventions/common/coai-caller-model.md');
}

function mountedConsultantRuleFile(): string {
  return path.resolve(__dirname, '../../..', '.agents/conventions/common/coai-consultant.md');
}

/**
 * The seventh reason reaches the paste: a review reply that ORDERS a consultation on a cadence.
 *
 * <p>Story 5.2 of todo/PLAN_consult_on_a_cadence.md. The server orders it and, in `require`, refuses
 * the group's code round until it is taken — but an AI obeying a paste without this trigger has never
 * been told what the order is, and meets a refusal nothing in its instructions explains.</p>
 */
test('the seventh reason — the cadence — survives into the paste', () => {
  const snippet = claudeSnippet();

  assert.match(snippet, /7\. \*\*A review reply orders one — the cadence\.\*\*/,
    'the cadence trigger is missing from the paste');
  assert.match(snippet, /`CONSULT ON A CADENCE\.`/, 'the paste does not name the order a reply carries');
  assert.match(snippet, /`kind` \(`cadence` or `risk`\), `plan` and\s+`epics`/, 'the paste does not say what to call it with');
  assert.match(snippet, /mcp__coai__close_consult/, 'the paste does not say to close it with an outcome');
});

test('the same text pasted into a CLAUDE.md is still a stale paste, with the old advice', () => {
  // The distinction is WHERE it was found, not what it says: the identical body in an instruction
  // file is a copy somebody made, and replacing it is exactly right.
  const mounted = fs.readFileSync(mountedRuleFile(), 'utf8').replace(/\r\n/g, '\n');

  assert.deepEqual(
    snippetStatus(mounted),
    {
      kind: 'older',
      behind: ['coai-document', 'coai-caller', 'coai-consultant'],
      current: ARTEFACT_VERSION,
    },
  );
});

test('the instruction files are searched BEFORE the mounted rule', () => {
  // A stale paste in CLAUDE.md is what the AI here reads. Reporting the mounted rule's version
  // instead would be a green light over the text actually being obeyed.
  assert.deepEqual(SNIPPET_LOCATIONS.slice(0, 4), [
    'CLAUDE.md',
    'AGENTS.md',
    'GEMINI.md',
    '.github/copilot-instructions.md',
  ]);
  assert.ok(SNIPPET_LOCATIONS.includes('.claude/rules/shared/common/coai-review-gate.md'));
  assert.ok(SNIPPET_LOCATIONS.includes('.claude/rules/common/coai-review-gate.md'));
});

test('the panel says nothing when the paste is current or absent', () => {
  // Two silences with different reasons, and both are correct: a workspace that never adopted the
  // gate is entitled not to, and one that is current has nothing to be told.
  assert.equal(snippetNote({ kind: 'current', current: ARTEFACT_VERSION }), '');
  assert.equal(snippetNote({ kind: 'absent', current: ARTEFACT_VERSION }), '');
});

test('a stale paste is told what it is missing, and what the menu hands out', () => {
  const note = snippetNote({ kind: 'older', behind: ['coai-consultant'], current: 4 });

  assert.match(note, /the consultant/);
  assert.match(note, /v4/);
  assert.match(note, /copy it again/);
});

test('an unversioned paste is told it is behind without inventing a number', () => {
  const note = snippetNote({ kind: 'unversioned', current: 4 });

  assert.match(note, /predates versioning/);
  assert.doesNotMatch(note, /v0/, 'a version nobody ever wrote');
});

test('a paste from the future says to update the extension, not to overwrite the repo', () => {
  const note = snippetNote({ kind: 'ahead', newer: ['coai-consultant'], current: 4 });

  assert.match(note, /update this one rather than pasting over it/);
});

/**
 * The sentence six repositories in this family actually see.
 *
 * <p>They mount the gate rule from the conventions submodule and have never pasted anything, so the
 * gate half is current and the other three are absent. Before this was fixed the panel printed "The
 * CLAUDE.md snippet in this workspace is v5; v5 is current", which is not a sentence anybody can
 * act on — the gate half's frozen marker was being used as the version of the whole paste. A paste
 * carries no number for the artefact as a whole, so the honest answer names the halves.</p>
 */
test('a mounting repository is not told two different numbers', () => {
  const gateOnly = claudeSnippet().split('<!-- coai-document')[0];

  const note = snippetNote(snippetStatus(gateOnly));

  assert.doesNotMatch(
    note,
    /v5\b/,
    `the note invents a version for a paste that carries no artefact number: "${note}"`,
  );
  assert.match(note, /the document gate/, 'it names the halves that are missing');
  assert.match(note, /the consultant/);
  assert.ok(
    note.includes(`v${ARTEFACT_VERSION}`),
    `and the version the menu now hands out: "${note}"`,
  );
});

/**
 * Pasting the new block without deleting the old one must not change the answer.
 *
 * <p>The refuted design summed every marker in the file, so two blocks in one CLAUDE.md read as v12
 * against our v8 and the panel told the person their newer copy was behind. A half is compared at
 * the LOWEST version the file carries for `older` and the HIGHEST for `ahead`, which cannot inflate
 * the way a sum does — two copies of v1 are still v1, and this is what stops that idea coming
 * back.</p>
 */
test('a duplicated block does not change what the panel reports', () => {
  const once = claudeSnippet();

  assert.deepEqual(snippetStatus(`${once}\n\n${once}`), snippetStatus(once));
});

/**
 * A paste from a build NEWER than this one is `ahead`, even when the newer part is a half this
 * build has never heard of.
 *
 * <p>Found by the code round, by one vendor in three roles. A repository pasted from a future build
 * carries our four halves plus its own new one; comparing only the halves we know about answered
 * `current`, the panel said nothing, and the notification after a copy said the repository was
 * already on it — while pasting would have deleted a rule the person's newer extension put there.
 * The shipped status has no name for a half it does not know, so it reports the id.</p>
 */
test('a paste carrying a half this build has never heard of is ahead, not current', () => {
  const fromTheFuture = `${claudeSnippet()}\n<!-- coai-escalation v1 -->\n`;

  assert.equal(snippetStatus(fromTheFuture).kind, 'ahead');
});

/**
 * And a NEWER half in a second block is noticed, even though the first block is current.
 *
 * <p>The other half of the same finding. A reader that took the first match stopped a duplicated
 * block inflating anything — and left a newer block appended below a current one invisible. A half
 * is judged at the HIGHEST version the file carries for `ahead`, and at the lowest for `older`, so
 * neither block can hide the other and nothing accumulates: two copies of v1 are still v1.</p>
 */
test('a newer half in a second block is not hidden by the first', () => {
  const current = claudeSnippet();
  // Derived from the constant, not retyped: these literals were `coai-document v1`, and the day
  // DOCUMENT_VERSION moved to 2 the replace matched nothing, the second block came out identical to
  // the first, and a test about a NEWER half quietly asserted nothing.
  const newerBelow = `${current}\n\n${current.replace(
    `coai-document v${DOCUMENT_VERSION}`,
    `coai-document v${DOCUMENT_VERSION + 1}`,
  )}`;

  assert.equal(snippetStatus(newerBelow).kind, 'ahead');
});

/**
 * And a STALE block above a current one is still reported, which is the other direction.
 *
 * <p>The two findings pull opposite ways: one asks that a newer block below a current one be seen,
 * the other that a stale block ABOVE a current one not be blessed — and the AI in that repository
 * reads the stale one first. A half is therefore compared at its LOWEST version for "behind" and its
 * HIGHEST for "newer", so both are true of the same file and neither reading has to lose.</p>
 */
test('a stale block above a current one is still reported as older', () => {
  const current = claudeSnippet();
  const staleAbove = `${current.replace(`coai-snippet v${SNIPPET_VERSION}`, 'coai-snippet v1')}\n\n${current}`;

  assert.equal(snippetStatus(staleAbove).kind, 'older');
});

/**
 * The precedence itself, which nothing pinned.
 *
 * <p>Found by review: swapping the two filters in `snippetStatus` left the whole suite green,
 * because every `ahead` fixture was otherwise complete and every `older` fixture had nothing newer
 * in it. A copy that is BOTH — newer in one half, missing another — is the case the ordering exists
 * for, and telling that person to paste over what they have would lose the newer half.</p>
 */
test('a copy that is newer in one half and missing another is ahead, not older', () => {
  const aheadAndIncomplete = claudeSnippet()
    .replace(`coai-document v${DOCUMENT_VERSION}`, 'coai-document v9')
    .replace(/<!-- coai-consultant v\d+ -->\n?/, '');

  assert.equal(snippetStatus(aheadAndIncomplete).kind, 'ahead');
});

/**
 * A fifth half must not be able to arrive unread.
 *
 * <p>This is the defect that produced this whole change, generalised: the consultant half was added
 * to the paste and nothing anywhere moved to say so — no number, no list, no test. The reader now
 * holds its own list of halves, and this compares it with what the artefact actually carries.</p>
 *
 * <p>The second assertion is the companion a scanning test needs: it proves the scan still SEES a
 * half it was not taught about, rather than passing because its pattern matches nothing.</p>
 */
test("the artefact's halves are exactly the halves the reader knows", () => {
  // IN ORDER, not as sets: the order of KNOWN_HALVES is the order the artefact is composed in and
  // the order the panel lists what is missing, so a row moved in the table without the text moving
  // with it is a drift worth a red test. (Raised on the code round as a silent-drift risk.)
  assert.deepEqual(
    halvesIn(claudeSnippet()).map((half) => half.id),
    [...HALF_IDS],
    'a half in the paste that KNOWN_HALVES does not list is a half nothing reads, versions, or reports',
  );

  const withAFifth = `${claudeSnippet()}\n<!-- coai-escalation v1 -->\n`;

  assert.notDeepEqual(
    halvesIn(withAFifth).map((half) => half.id),
    [...HALF_IDS],
    'the scan must still notice a half the reader does not know — otherwise it passes forever',
  );
});

/**
 * And it must not arrive at v0, which would leave every number unmoved.
 *
 * <p>Raised on the plan round against the version arithmetic the design started with. The
 * arithmetic is gone, but the observation survives it: a half numbered from zero is a half whose
 * marker says nothing, and the convention in this file is that a version starts at 1.</p>
 */
test('no half of the artefact is numbered from zero', () => {
  // Through `halvesIn` rather than a fifth copy of the marker grammar: the scan belongs to the
  // module that owns the markers, and a test re-spelling it is the duplication this change removed.
  const halves = halvesIn(claudeSnippet());

  assert.equal(halves.length, HALF_IDS.length, 'the fixture really does carry every half');
  for (const half of halves) {
    assert.ok(half.version >= 1, `${half.id} is at v${half.version}; a half's version starts at 1`);
  }
});

/**
 * The table is the source of truth for the artefact, not a description of it.
 *
 * <p>`claudeSnippet()` composes from `KNOWN_HALVES` now, so this asserts the thing that could still
 * drift: that every row's `text` really is the rule its id and version claim. A row pointing at the
 * wrong constant would compose a paste whose markers disagree with its bodies, and every other test
 * here would go on passing.</p>
 */
test('every row of the table carries the rule its own marker names', () => {
  for (const half of KNOWN_HALVES) {
    const found = halvesIn(half.text);

    assert.deepEqual(
      found.map((one) => one.id),
      [half.id],
      `${half.id}'s text must carry its own marker and no other`,
    );
    assert.equal(found[0].version, half.version, `${half.id}'s marker and its version disagree`);
  }
});

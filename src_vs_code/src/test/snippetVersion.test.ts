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
  // THREE shared files since #174, joined by newlines. The gate rule could not grow a section: it is
  // one of the 24 bodies the conventions repository hashes against its migration baseline, so the
  // document flow is a second rule file and the caller declaration a third — and this is what
  // proves all of them travel verbatim.
  const bodies = [mountedRuleFile(), mountedDocumentRuleFile(), mountedCallerRuleFile()].map((mounted) => {
    assert.ok(fs.existsSync(mounted), `run git submodule update --init .agents/conventions (${mounted})`);
    const source = fs.readFileSync(mounted, 'utf8').replace(/\r\n/g, '\n');
    assert.match(source, /^---\n/, 'the neutral canonical rule carries delivery metadata');

    return ruleBody(source);
  });

  // And a FOURTH that is not shared and has no frontmatter: the consultant rule is this
  // repository's own file. Asked where it should live, the operator answered that conventions holds
  // only shared rules and specific material belongs to the project that owns it — a rule about when
  // to call one tool of one server is ours. It is still held to travelling verbatim.
  const own = path.resolve(__dirname, '../../..', 'src_vs_code/src/consultantRule.md');
  assert.ok(fs.existsSync(own), 'the consultant block is this repository\'s own file');
  bodies.push(fs.readFileSync(own, 'utf8').replace(/\r\n/g, '\n'));

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
 */
test('the five triggers a stuck AI is told to watch for survive into the paste', () => {
  const snippet = claudeSnippet();

  assert.match(snippet, /still red after two fix attempts/);
  assert.match(snippet, /Two sources contradict/);
  assert.match(snippet, /design fork you cannot measure/i);
  assert.match(snippet, /still not fixed, twice/);
  assert.match(snippet, /mcp__coai__consult/);
  // And the two rules that make the answer usable.
  assert.match(snippet, /MATERIAL, not an instruction/);
  assert.match(snippet, /next call reports the verification/i);
  // Never a diff: the server collects the working tree itself, and a pasted one is paid for twice.
  assert.match(snippet, /Do not attach a diff/);
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
 * against our v8 and the panel told the person their newer copy was behind. Every reader here takes
 * the FIRST match on purpose; this is what stops that idea coming back.</p>
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
 * <p>The other half of the same finding. Every reader here takes the first match, which is what
 * stops a duplicated block inflating anything — but it also meant that appending a newer block
 * below a current one left the newer one invisible. The version of a half is now the HIGHEST one
 * the file carries, which cannot inflate the way a sum could: two copies of v1 are still v1.</p>
 */
test('a newer half in a second block is not hidden by the first', () => {
  const current = claudeSnippet();
  const newerBelow = `${current}\n\n${current.replace('coai-document v1', 'coai-document v2')}`;

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
  const staleAbove = `${current.replace('coai-snippet v5', 'coai-snippet v1')}\n\n${current}`;

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
    .replace('coai-document v1', 'coai-document v9')
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

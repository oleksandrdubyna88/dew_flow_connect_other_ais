import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import {
  claudeSnippet,
  DOCUMENT_VERSION,
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

test('an older paste is reported as older, with both numbers', () => {
  const old = claudeSnippet().replace(`coai-snippet v${SNIPPET_VERSION}`, 'coai-snippet v1');

  assert.deepEqual(snippetStatus(old), { kind: 'older', found: 1, current: SNIPPET_VERSION });
});

test('a copy pasted before versioning existed is not version zero', () => {
  // Everything pasted until today has no marker. "Predates versioning" is the true statement;
  // calling it 0 would invent a number nobody wrote.
  const before = '## Multi-model review gate (ConnectOtherAIs)\n\nThis repository is reviewed by…';

  assert.deepEqual(snippetStatus(before), { kind: 'unversioned', current: SNIPPET_VERSION });
});

test('no instruction file at all is absent, not stale', () => {
  // A repository that has deliberately not adopted the gate is not a problem to report.
  assert.deepEqual(snippetStatus(undefined), { kind: 'absent', current: SNIPPET_VERSION });
  assert.deepEqual(snippetStatus('# Just a readme\n'), { kind: 'absent', current: SNIPPET_VERSION });
});

test('the current version is current', () => {
  assert.deepEqual(snippetStatus(claudeSnippet()), { kind: 'current', current: SNIPPET_VERSION });
});

test('a copy from the FUTURE is not called old', () => {
  // An extension older than the pasted snippet — somebody updated the repo before this machine.
  const ahead = claudeSnippet().replace(`coai-snippet v${SNIPPET_VERSION}`, `coai-snippet v${SNIPPET_VERSION + 5}`);

  assert.deepEqual(snippetStatus(ahead), { kind: 'ahead', found: SNIPPET_VERSION + 5, current: SNIPPET_VERSION });
});

/**
 * The guard that makes the number worth having.
 *
 * <p>A version somebody must remember to bump is the same failure one level up: the snippet moves,
 * the number does not, and every pasted copy reports itself current forever. So the number is
 * pinned to the text. Editing the snippet fails this test until both are changed together, which is
 * the only moment either is cheap.</p>
 *
 * <p><b>There are TWO numbers now, and only one of them can move.</b> `SNIPPET_VERSION` is the
 * marker written inside `coai-review-gate.md`, one of the 24 rule bodies the conventions repository
 * hashes against its migration baseline — so that file cannot be edited and its number cannot be
 * raised. Whatever changes about the pasted text, `DOCUMENT_VERSION` is the half that records it,
 * until the inventory retires and the two rules become one again.</p>
 */
test('the snippet text and its version numbers move together', () => {
  const body = claudeSnippet()
    .replace(/<!-- coai-snippet v\d+ -->\n?/, '')
    .replace(/<!-- coai-document v\d+ -->\n?/, '');
  const sha = createHash('sha256').update(body).digest('hex').slice(0, 16);

  assert.equal(
    sha,
    SNIPPET_BODY_SHA,
    `The snippet text changed. Raise DOCUMENT_VERSION to ${DOCUMENT_VERSION + 1} and set `
      + `SNIPPET_BODY_SHA to '${sha}'. Both, together — a version that does not move with the text `
      + 'tells every pasted copy it is current forever, which is the defect this exists to catch. '
      + `(SNIPPET_VERSION stays ${SNIPPET_VERSION}: the gate rule is frozen against the migration `
      + 'baseline, so its marker cannot be raised.)',
  );
});

/**
 * A paste made before the document gate existed is OLDER, even though its gate half is current.
 */
test('a paste with no document half is reported as older', () => {
  const gateOnly = `${claudeSnippet().split('<!-- coai-document')[0]}`;

  assert.deepEqual(
    snippetStatus(gateOnly),
    { kind: 'older', found: SNIPPET_VERSION, current: SNIPPET_VERSION },
    'the AI obeying it will never call review_document, which is what "older" is for',
  );
});

/**
 * The canonical body is delivered from the pinned neutral mount during build preparation.
 * Missing sources fail before compile; this check independently compares the generated body
 * and the pinned source, while the version/hash guard above stays independent of generation.
 */
test('the mounted shared rules are byte-identical to what the menu hands out', () => {
  // TWO files since plan 4, joined by a newline. The gate rule could not grow a section: it is one
  // of the 24 bodies the conventions repository hashes against its migration baseline, so the
  // document flow is a second rule file — and this is what proves both of them travel verbatim.
  const bodies = [mountedRuleFile(), mountedDocumentRuleFile()].map((mounted) => {
    assert.ok(fs.existsSync(mounted), `run git submodule update --init .agents/conventions (${mounted})`);
    const source = fs.readFileSync(mounted, 'utf8').replace(/\r\n/g, '\n');
    assert.match(source, /^---\n/, 'the neutral canonical rule carries delivery metadata');

    return ruleBody(source);
  });

  assert.equal(
    bodies.join('\n'),
    claudeSnippet(),
    'the mounted rules differ from the generated delivery. Run npm run prepare:gate; edit the canonical source only.',
  );
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
  assert.equal(snippetNote({ kind: 'current', current: SNIPPET_VERSION }), '');
  assert.equal(snippetNote({ kind: 'absent', current: SNIPPET_VERSION }), '');
});

test('a stale paste is told what to do, with both numbers', () => {
  const note = snippetNote({ kind: 'older', found: 1, current: 4 });

  assert.match(note, /v1/);
  assert.match(note, /v4/);
  assert.match(note, /Copy it again/);
});

test('an unversioned paste is told it is behind without inventing a number', () => {
  const note = snippetNote({ kind: 'unversioned', current: 4 });

  assert.match(note, /predates versioning/);
  assert.doesNotMatch(note, /v0/, 'a version nobody ever wrote');
});

test('a paste from the future says to update the extension, not to overwrite the repo', () => {
  const note = snippetNote({ kind: 'ahead', found: 9, current: 4 });

  assert.match(note, /update this one rather than pasting over it/);
});

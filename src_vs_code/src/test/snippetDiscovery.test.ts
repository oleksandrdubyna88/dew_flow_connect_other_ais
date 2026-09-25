import assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as os from 'node:os';
import * as path from 'node:path';
import { test } from 'node:test';
import {
  ARTEFACT_VERSION,
  claudeSnippet,
  CONSULTANT_VERSION,
  readSnippetStatus,
  SNIPPET_LOCATIONS,
  SNIPPET_VERSION,
} from '../claudeSnippet';

test('neutral shared rules are discovered and older project/local copies take priority', async t => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'coai-snippet-'));
  t.after(() => fs.rm(root, { recursive: true, force: true }));
  const write = async (name: string, text: string): Promise<void> => {
    const file = path.join(root, name);
    await fs.mkdir(path.dirname(file), { recursive: true });
    await fs.writeFile(file, text);
  };
  const read = async (name: string): Promise<string> => {
    try {
      return await fs.readFile(path.join(root, name), 'utf8');
    } catch (error) {
      if (error instanceof Error && 'code' in error && error.code === 'ENOENT') { return ''; }
      throw error;
    }
  };
  const legacy = '.claude/rules/shared/common/coai-review-gate.md';
  await write(legacy, claudeSnippet());
  assert.equal((await readSnippetStatus(read)).kind, 'current', 'a legacy-only shared mount remains discoverable');
  await fs.unlink(path.join(root, legacy));
  await write('.agents/conventions/common/coai-review-gate.md', claudeSnippet());
  assert.equal((await readSnippetStatus(read)).kind, 'current');
  const older = claudeSnippet().replace(`coai-snippet v${SNIPPET_VERSION}`, 'coai-snippet v1');
  for (const name of ['CLAUDE.md', '.agents/PROJECT.md', '.agents/rules/common/review-gate.md', '.agents/rules/common/coai-review-gate.md', '.claude/rules/common/review-gate.md', '.claude/rules/common/coai-review-gate.md']) {
    await write(name, older);
    assert.deepEqual(await readSnippetStatus(read), { kind: 'older', behind: ['coai-snippet'], current: ARTEFACT_VERSION }, name);
    await fs.unlink(path.join(root, name));
  }
});


test('all candidate reads start together but a slower older root copy still wins', async () => {
  const started: string[] = [];
  let releaseRoot: (text: string) => void = () => { throw new Error('root read has not started'); };
  const root = new Promise<string>(resolve => { releaseRoot = resolve; });
  const pending = readSnippetStatus(async name => {
    started.push(name);
    return name === 'CLAUDE.md' ? root : claudeSnippet();
  });
  try {
    assert.deepEqual(started, SNIPPET_LOCATIONS, 'candidate I/O must not wait for the first file');
  } finally {
    releaseRoot(claudeSnippet().replace(`coai-snippet v${SNIPPET_VERSION}`, 'coai-snippet v1'));
  }
  assert.deepEqual(await pending, { kind: 'older', behind: ['coai-snippet'], current: ARTEFACT_VERSION });
});

/**
 * A repository that MOUNTS the rules and has pasted nothing is current — its halves are the mount's
 * sibling files, not one file (todo/PLAN_consult_on_a_cadence.md, story 5.2; the epics 4–6
 * consultation, point 5).
 *
 * <p>The reader found the first file carrying the gate marker and read every half from it. A mount
 * holds the gate rule and its three siblings as four files, so a repository that pasted nothing and
 * mounted everything was told it was behind on three halves it had. The fixtures above put the whole
 * snippet into the gate file, which no real mount does — these use the real mounted files.</p>
 */
test('a mount is read with its sibling rules, and an actual paste still takes precedence', async t => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'coai-snippet-mount-'));
  t.after(() => fs.rm(root, { recursive: true, force: true }));
  const write = async (name: string, text: string): Promise<void> => {
    const file = path.join(root, name);
    await fs.mkdir(path.dirname(file), { recursive: true });
    await fs.writeFile(file, text);
  };
  const read = async (name: string): Promise<string> => {
    try {
      return await fs.readFile(path.join(root, name), 'utf8');
    } catch (error) {
      if (error instanceof Error && 'code' in error && error.code === 'ENOENT') { return ''; }
      throw error;
    }
  };
  const real = path.resolve(__dirname, '../../..', '.agents/conventions/common');
  const rules = ['coai-review-gate.md', 'coai-document-gate.md', 'coai-caller-model.md', 'coai-consultant.md'];
  const mount = async (at: string, which: readonly string[] = rules): Promise<void> => {
    for (const name of which) {
      await write(`${at}/${name}`, await fs.readFile(path.join(real, name), 'utf8'));
    }
  };

  await mount('.agents/conventions/common');
  assert.deepEqual(await readSnippetStatus(read), { kind: 'current', current: ARTEFACT_VERSION },
    'four mounted rules and no paste are current — the siblings are the other three halves');

  await write('CLAUDE.md', claudeSnippet().replace(`coai-consultant v${CONSULTANT_VERSION}`, 'coai-consultant v2'));
  assert.deepEqual(await readSnippetStatus(read), { kind: 'older', behind: ['coai-consultant'], current: ARTEFACT_VERSION },
    'a stale paste is what the AI in that repository reads, so it wins over a current mount');

  await write('CLAUDE.md', claudeSnippet().split('<!-- coai-consultant')[0]);
  assert.deepEqual(await readSnippetStatus(read), { kind: 'older', behind: ['coai-consultant'], current: ARTEFACT_VERSION },
    'a paste with no consultant half is older, whatever the mount holds');
});

test('the legacy mount location is read with its siblings too', async t => {
  // Two mount folders are known, and a reader tested through one of them says nothing about the other.
  // (Story 5.2's plan round, codex.)
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'coai-snippet-legacy-mount-'));
  t.after(() => fs.rm(root, { recursive: true, force: true }));
  const real = path.resolve(__dirname, '../../..', '.agents/conventions/common');
  const files = new Map<string, string>();
  for (const name of ['coai-review-gate.md', 'coai-document-gate.md', 'coai-caller-model.md', 'coai-consultant.md']) {
    files.set(`.claude/rules/shared/common/${name}`, await fs.readFile(path.join(real, name), 'utf8'));
  }

  assert.deepEqual(await readSnippetStatus(async (name) => files.get(name) ?? ''), { kind: 'current', current: ARTEFACT_VERSION });
});

test('a missing half is never filled from ANOTHER mount', async t => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'coai-snippet-two-mounts-'));
  t.after(() => fs.rm(root, { recursive: true, force: true }));
  const files = new Map<string, string>();
  const real = path.resolve(__dirname, '../../..', '.agents/conventions/common');
  const text = async (name: string): Promise<string> => fs.readFile(path.join(real, name), 'utf8');
  // The neutral mount carries only its gate rule; a legacy mount beside it carries the siblings.
  files.set('.agents/conventions/common/coai-review-gate.md', await text('coai-review-gate.md'));
  for (const name of ['coai-document-gate.md', 'coai-caller-model.md', 'coai-consultant.md']) {
    files.set(`.claude/rules/shared/common/${name}`, await text(name));
  }

  const status = await readSnippetStatus(async (name) => files.get(name) ?? '');

  assert.deepEqual(status, { kind: 'older', behind: ['coai-document', 'coai-caller', 'coai-consultant'], current: ARTEFACT_VERSION },
    'the halves are read from the mount whose gate rule was selected, and nowhere else');
});

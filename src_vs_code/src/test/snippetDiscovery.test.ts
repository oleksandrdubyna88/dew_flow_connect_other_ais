import assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as os from 'node:os';
import * as path from 'node:path';
import { test } from 'node:test';
import { claudeSnippet, readSnippetStatus, SNIPPET_LOCATIONS, SNIPPET_VERSION } from '../claudeSnippet';

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
    assert.deepEqual(await readSnippetStatus(read), { kind: 'older', found: 1, current: SNIPPET_VERSION }, name);
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
  assert.deepEqual(await pending, { kind: 'older', found: 1, current: SNIPPET_VERSION });
});

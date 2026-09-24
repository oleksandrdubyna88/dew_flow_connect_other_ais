import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { promptNames, promptTexts, readPromptOrAbsent } from '../configPromptFiles';

/**
 * The prompt files an export reads and an import touches — issue #467, the code round (our own reviewer,
 * gemini, codex). Real directories, because every finding here was about what a real one can hold.
 */

function folder(): string {
  return mkdtempSync(join(tmpdir(), 'coai-prompts-'));
}

test('only regular .md files with a valid prompt name are prompts; a folder named like one is not', async () => {
  const dir = folder();
  try {
    writeFileSync(join(dir, 'role2-general.md'), 'Is it met?');
    writeFileSync(join(dir, 'notes.txt'), 'not a prompt');
    writeFileSync(join(dir, 'Bad Name.md'), 'refused by the id guard');
    mkdirSync(join(dir, 'folder.md'));

    assert.deepEqual(await promptNames(dir), ['role2-general']);
    assert.deepEqual(await promptTexts(dir), { 'role2-general': 'Is it met?' });
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('no prompts folder at all is no prompts, not a failure', async () => {
  const dir = join(folder(), 'never-made');

  assert.deepEqual(await promptNames(dir), []);
  assert.deepEqual(await promptTexts(dir), {});
});

test('a symbolic link in the prompts folder is not read into an export', async (t) => {
  // It could point anywhere, and the export is a file meant to be shared. (codex, the code round.)
  const dir = folder();
  try {
    writeFileSync(join(dir, 'secret.txt'), 'private');
    try {
      symlinkSync(join(dir, 'secret.txt'), join(dir, 'linked.md'));
    } catch {
      t.skip('this machine does not let a test make a symbolic link');

      return;
    }

    assert.deepEqual(await promptTexts(dir), {});
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a prompt that is not there is ABSENT, and one that cannot be read is an error — never "absent"', async () => {
  // "Absent" is what a rollback DELETES. A file that exists but failed to read, taken as absent, would be
  // deleted by the restore that was meant to protect it. (our own reviewer, the code round.)
  const dir = folder();
  try {
    assert.equal(await readPromptOrAbsent(join(dir, 'missing.md')), undefined);
    writeFileSync(join(dir, 'there.md'), 'text');
    assert.equal(await readPromptOrAbsent(join(dir, 'there.md')), 'text');
    mkdirSync(join(dir, 'unreadable.md'));
    await assert.rejects(readPromptOrAbsent(join(dir, 'unreadable.md')));
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

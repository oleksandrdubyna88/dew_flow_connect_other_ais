import assert from 'node:assert/strict';
import { readFileSync, readdirSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import {
  isWsl,
  mountPathOf,
  networkingModeOf,
  previewOf,
  writeWslconfig,
  wslconfigWith,
} from '../wslNetwork';

/**
 * The Windows-side half of a local reviewer running in WSL.
 *
 * <p>Every string below is captured from the machine the symptom was measured on
 * (2026-09-03, Ubuntu under WSL 2.7.10, Windows 11 26200) rather than invented — the
 * `/proc/version` line, the `.wslconfig` this product writes, and the path shape interop
 * gives back. The file under test is pure except for two functions that touch the world,
 * which is what makes these assertions shapes instead of a machine.</p>
 */

const WSL_PROC_VERSION =
  'Linux version 6.18.33.2-2 (root@a1b2c3) (gcc (GCC) 15.2.0, GNU ld 2.44) '
  + '#1 SMP PREEMPT_DYNAMIC Fri Aug 15 00:00:00 UTC 2026 Microsoft WSL2';

const NATIVE_PROC_VERSION =
  'Linux version 6.11.0-19-generic (buildd@lcy02-amd64-021) '
  + '(x86_64-linux-gnu-gcc-13 (Ubuntu 13.3.0), GNU ld 2.42) #19-Ubuntu SMP Mon Feb 17 11:51:52 UTC 2026';

// ---------- which kernel this is ----------

test('a WSL kernel is recognised by the banner Microsoft puts in it', () => {
  assert.equal(isWsl(WSL_PROC_VERSION), true);
});

test('a native Linux kernel is not WSL, however Linux it is', () => {
  assert.equal(isWsl(NATIVE_PROC_VERSION), false);
  assert.equal(isWsl(''), false);
});

// ---------- what the file currently says ----------

test('a file with no networkingMode key points nowhere in particular', () => {
  assert.equal(networkingModeOf(''), 'none');
  assert.equal(networkingModeOf('[wsl2]\nmemory=8GB\n'), 'none');
});

test('the mode is read out of the wsl2 section, whatever the spacing', () => {
  assert.equal(networkingModeOf('[wsl2]\n  networkingMode = mirrored\n'), 'mirrored');
  assert.equal(networkingModeOf('[wsl2]\nnetworkingMode=NAT\n'), 'nat');
});

test('a networkingMode outside the wsl2 section is not the wsl2 mode', () => {
  // `[experimental]` had its own keys for years and people still have them; a key read out
  // of the wrong section would make the button offer to "revert" something it never set.
  assert.equal(networkingModeOf('[experimental]\nnetworkingMode=mirrored\n'), 'none');
});

// ---------- merging into it ----------

test('an empty file gains the section and the key', () => {
  const merged = wslconfigWith('', 'mirrored');

  assert.equal(merged.refused, '');
  assert.equal(merged.changed, true);
  assert.match(merged.text, /^\[wsl2\]\r?\nnetworkingMode=mirrored\r?\n$/);
});

test('an existing wsl2 section keeps every key it had', () => {
  const existing = '[wsl2]\nmemory=16GB\nprocessors=8\n';
  const merged = wslconfigWith(existing, 'mirrored');

  assert.equal(merged.changed, true);
  assert.match(merged.text, /memory=16GB/);
  assert.match(merged.text, /processors=8/);
  assert.match(merged.text, /networkingMode=mirrored/);
});

test('a mode that is already set is replaced in place, not appended beside itself', () => {
  const merged = wslconfigWith('[wsl2]\nnetworkingMode=nat\nmemory=16GB\n', 'mirrored');

  assert.equal(merged.changed, true);
  assert.equal(merged.text, '[wsl2]\nnetworkingMode=mirrored\nmemory=16GB\n');
  assert.equal((merged.text.match(/networkingMode/g) ?? []).length, 1);
});

test('a file already in the asked mode comes back byte-identical and says so', () => {
  const existing = '[wsl2]\r\nnetworkingMode=mirrored\r\n';
  const merged = wslconfigWith(existing, 'mirrored');

  assert.equal(merged.changed, false);
  assert.equal(merged.text, existing, 'nothing to change means nothing to write');
});

test('the key lands INSIDE the wsl2 section even when another section follows it', () => {
  // Appending at the end of the file would have put a wsl2 key under `[experimental]`, where
  // WSL ignores it — and the person would have restarted for nothing.
  const merged = wslconfigWith('[wsl2]\nmemory=16GB\n\n[experimental]\nsparseVhd=true\n', 'mirrored');
  const lines = merged.text.split(/\r?\n/);

  assert.ok(lines.indexOf('networkingMode=mirrored') > lines.indexOf('[wsl2]'));
  assert.ok(lines.indexOf('networkingMode=mirrored') < lines.indexOf('[experimental]'));
});

test('the line endings of the file are the line endings of the edit', () => {
  const merged = wslconfigWith('[wsl2]\r\nmemory=16GB\r\n', 'mirrored');

  assert.ok(merged.text.includes('\r\nnetworkingMode=mirrored'), merged.text);
  assert.ok(!/[^\r]\n/.test(merged.text), 'a mixed-ending file is a file Notepad shows as one line');
});

test('the way back is the same merge with the other mode', () => {
  const merged = wslconfigWith('[wsl2]\nnetworkingMode=mirrored\n', 'nat');

  assert.equal(merged.changed, true);
  assert.equal(networkingModeOf(merged.text), 'nat');
});

test('a file this build cannot read as text is refused, not merged', () => {
  // PowerShell's `>` still writes UTF-16LE. Read as UTF-8 that arrives as NULs between the
  // characters — and a "merge" of it would write a global networking file back as rubbish.
  const utf16 = '[\u0000w\u0000s\u0000l\u00002\u0000]\u0000';
  const merged = wslconfigWith(utf16, 'mirrored');

  assert.notEqual(merged.refused, '');
  assert.equal(merged.changed, false);
  assert.equal(merged.text, utf16, 'a refusal leaves the original exactly as it was');
});

// ---------- where the file is ----------

test('a Windows profile path becomes the mount path WSL reads it through', () => {
  assert.equal(mountPathOf('C:\\Users\\strug'), '/mnt/c/Users/strug');
  assert.equal(mountPathOf('D:\\home\\jinx\\'), '/mnt/d/home/jinx');
});

test('anything that is not a drive path yields nothing rather than a guess', () => {
  assert.equal(mountPathOf(''), '');
  assert.equal(mountPathOf('\\\\server\\share'), '');
  assert.equal(mountPathOf('/home/jinx'), '');
});

// ---------- what the confirmation shows before anything is written ----------

test('the preview is the whole merged file, so other settings can be seen surviving', () => {
  // Showing only the two lines being added cannot answer the question somebody has before approving
  // a change to a file every distro reads. Found by GPT-5.6-Luna in the code round.
  const merged = wslconfigWith('[wsl2]\r\nmemory=16GB\r\nprocessors=8\r\n', 'mirrored');

  const shown = previewOf(merged.text);
  assert.match(shown, /memory=16GB/);
  assert.match(shown, /processors=8/);
  assert.match(shown, /networkingMode=mirrored/);
  assert.ok(!shown.includes('\r'), 'a dialog is not a text editor; the endings are normalised for it');
});

test('a long file is cut, and the cut says so', () => {
  const long = `[wsl2]\n${Array.from({ length: 60 }, (_, i) => `key${i}=${i}`).join('\n')}\n`;

  const shown = previewOf(long, 10);
  assert.equal(shown.split('\n').length, 11, 'ten lines and the sentence that admits the rest');
  assert.match(shown, /51 more lines, unchanged/);
});

// ---------- what a write reports ----------

test('a write that could not even start says nothing was changed', async () => {
  // A directory that does not exist: the temporary file cannot be created, so the target was never
  // touched and the person must be told exactly that.
  const outcome = await writeWslconfig(
    join(__dirname, 'no-such-directory-here', '.wslconfig'),
    '[wsl2]\nnetworkingMode=mirrored\n',
  );

  assert.equal(outcome.written, false);
  assert.match(outcome.message, /nothing was changed|could not be written/);
});

test('a write that landed reports success, and reports it as WRITTEN either way', async () => {
  // The defect both code reviewers found independently: the rename is atomic, the read-back after
  // it is not, so a read-back that fails used to be reported as a FAILED write — for a file that
  // had already been replaced. The person would then press the button again and undo it.
  const path = join(tmpdir(), `coai-wslconfig-${process.pid}.test`);
  const text = '[wsl2]\nnetworkingMode=mirrored\n';
  try {
    const outcome = await writeWslconfig(path, text);

    assert.equal(outcome.written, true);
    assert.equal(outcome.message, '', 'a clean write has nothing to say');
    assert.equal(readFileSync(path, 'utf8'), text);
  } finally {
    rmSync(path, { force: true });
  }
});

// ---------- the guarantee that nothing is written on its own ----------

test('the config writer is reachable only from the panel command, never from activation', () => {
  // The finding this test exists for (codex, Blocking-adjacent, plan round 2026-09-03): a
  // regression that called the writer during activation or discovery would change a global
  // networking file with nobody's consent, and every OTHER test here would still pass,
  // because they all test pure merging. Only the call graph can catch that.
  const dir = join(__dirname, '..', '..', 'src');
  const callers = readdirSync(dir)
    .filter((f) => f.endsWith('.ts'))
    .filter((f) => f !== 'wslNetwork.ts')
    .filter((f) => readFileSync(join(dir, f), 'utf8').includes('writeWslconfig'));

  assert.deepEqual(callers, ['panelProvider.ts'],
    'only the panel command may write .wslconfig; a new caller must be a deliberate decision');
});

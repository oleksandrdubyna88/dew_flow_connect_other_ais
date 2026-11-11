import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { ChildProcess, spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import {
  chmodSync, existsSync, mkdirSync, readFileSync, readdirSync, rmSync, statSync, symlinkSync, writeFileSync,
} from 'node:fs';
import { delimiter, join } from 'node:path';
import test from 'node:test';
import { projectDirNames } from '../claudeSessions';
import { RemoteRead, readRemote } from '../remoteFrame';
import { REMOTE_SCRIPT, REMOTE_TOOLS, RemoteCall, TAIL_MAX } from '../remoteScript';

/**
 * The REAL script, run by a real `sh -s` (E2.S2): the same text that is sent, against a temporary HOME with a fake `tmux` and
 * a fake `ps` on a `PATH` that holds nothing else, and its stdout read by the real parser. The pure tests of `remoteFrame.test.ts`
 * say what each frame MEANS; these say what the script really PRINTS — an empty glob that ate the rest of the script, a tool that
 * read the script as its input, a closed reader that did not stop it.
 *
 * <p><b>What a fake cannot prove</b> (said in `research/module_tests.md`): the real `comm` of the Claude CLI, the real `ps --ppid`,
 * the real registry file's `procStart` and the real tmux (M-O5, M-O6). The fakes hold what was MEASURED on 2026-11-11: the
 * exact target `=<name>:` (anything else exits 2) and `comm` `claude`.</p>
 *
 * <p>Each test runs once for every shell that is there: `sh`, and `bash --posix`, which reads a script byte by byte where `dash`
 * reads it in blocks (M-S3). No `sh` → skipped with that reason, and on the Linux runner of CI a missing `sh` FAILS (S2-T44).</p>
 */

const S = 'small-tasks';
const ID = '0b2e6a52-1f6c-4d0e-8a43-1c9d1d5e7f10';
const ID2 = '7c1d44b0-2a9e-4c55-9b3e-5f0a8e2d6c21';
const CALL: RemoteCall = { kind: 'session', session: S };
const ID_CALL: RemoteCall = { kind: 'id', session: S, id: ID };
const KB = '/home/git/knowledge_base';
const PANE_PID = '99000001';

/** A file of the PATH of this test run, or nothing. */
function onPath(name: string): string | undefined {
  return (process.env['PATH'] ?? '').split(delimiter).map((dir) => join(dir, name)).find((path) => existsSync(path));
}

interface Shell {
  readonly label: string;
  /** An ABSOLUTE path: node looks a bare command up on the `PATH` of `env`, and that is `bin/`, which holds no shell. */
  readonly path: string;
  readonly args: readonly string[];
}

const SHELLS: readonly Shell[] = ((): readonly Shell[] => {
  const found: Shell[] = [];
  const sh = onPath('sh');
  const bash = onPath('bash');
  if (sh !== undefined) {
    found.push({ label: 'sh', path: sh, args: [] });
  }
  if (bash !== undefined) {
    found.push({ label: 'bash --posix', path: bash, args: ['--posix'] });
  }

  return found;
})();

const NO_SH = 'no POSIX sh on this machine';

/** One test for each shell that is there; with none, one skipped test that says why. */
function shellTest(name: string, body: (shell: Shell) => Promise<void>): void {
  if (SHELLS.length === 0) {
    test(name, { skip: NO_SH }, () => undefined);

    return;
  }
  for (const shell of SHELLS) {
    test(`${name} [${shell.label}]`, () => body(shell));
  }
}

test('S2-T44: on the Linux runner of CI the sh tests RUN — a missing sh is a failure, not a skip', () => {
  if (process.env['CI'] === 'true' && process.platform === 'linux') {
    assert.ok(SHELLS.some((shell) => shell.label === 'sh'), `${NO_SH}: the sh tests of this file would be skipped in CI`);
  }
});

interface Tree {
  readonly root: string;
  readonly home: string;
  readonly bin: string;
  readonly fx: string;
}

const runFolder = (): string => {
  const folder = process.env['COAI_TEST_RUN_FOLDER'];
  assert.ok(folder !== undefined && folder.length > 0, 'the run folder is not set');

  return folder;
};

/** The two fakes. Their own `PATH` is the system's, so what the script under test sees is `bin/` alone. */
function fake(name: string, fx: string, body: string): string {
  return `#!/bin/sh
PATH=/usr/bin:/bin
echo "${name} $$ $* stdin=$(readlink /proc/$$/fd/0)" >> "${fx}/log"
[ -e "${fx}/eat" ] && cat >/dev/null
${body}
`;
}

const TMUX = (fx: string): string => fake('tmux', fx, `case "$1" in
  list-panes)
    case "$4" in
      =*:) name=\${4#=}; name=\${name%:}; [ -f "${fx}/$name.panes" ] || exit 1; cat "${fx}/$name.panes" ;;
      *) exit 2 ;;
    esac ;;
  display-message)
    case "$4" in
      # A session target shows its ACTIVE pane, which is the first row; a pane target shows that pane's folder.
      =*:) name=\${4#=}; name=\${name%:}; [ -f "${fx}/$name.panes" ] || exit 1; head -n 1 "${fx}/$name.panes" ;;
      *) n=\${4#%}; [ -f "${fx}/$n.cwd" ] || exit 1; cat "${fx}/$n.cwd" ;;
    esac ;;
  *) exit 2 ;;
esac`);

const PS = (fx: string): string => fake('ps', fx, `[ -e "${fx}/slow" ] && sleep 0.1
case "$3" in
  -p) awk -v p="$4" '$1 == p { print $3 }' "${fx}/ps.txt" ;;
  --ppid) awk -v p="$4" '$2 == p { print $1 }' "${fx}/ps.txt" ;;
  *) exit 2 ;;
esac`);

/** The tree of one test: a HOME, a `bin/` with the fakes and a link to each other tool, and the fakes' own folder. */
function tree(): Tree {
  const root = join(runFolder(), `remote-${randomUUID()}`);
  const home = join(root, 'home');
  const bin = join(root, 'bin');
  const fx = join(root, 'fx');
  for (const dir of [join(home, '.claude', 'sessions'), join(home, '.claude', 'projects'), bin, fx]) {
    mkdirSync(dir, { recursive: true });
  }
  for (const [name, text] of [['tmux', TMUX(fx)], ['ps', PS(fx)]] as const) {
    writeFileSync(join(bin, name), text, { mode: 0o755 });
  }
  writeFileSync(join(fx, 'ps.txt'), '');
  for (const name of [...REMOTE_TOOLS.filter((tool) => tool !== 'tmux' && tool !== 'ps'), 'stat']) {
    const real = onPath(name);
    assert.ok(real !== undefined, `the tool ${name} is not on this machine's PATH`);
    symlinkSync(real, join(bin, name));
  }

  return { root, home, bin, fx };
}

/** A tree for the length of `body`, removed after it whatever happens. */
async function withTree(body: (t: Tree) => Promise<void>): Promise<void> {
  const t = tree();
  try {
    await body(t);
  } finally {
    rmSync(t.root, { recursive: true, force: true });
  }
}

const panes = (t: Tree, session: string, ...rows: readonly string[]): void => writeFileSync(join(t.fx, `${session}.panes`), `${rows.join('\n')}\n`);
const processes = (t: Tree, ...rows: readonly string[]): void => writeFileSync(join(t.fx, 'ps.txt'), `${rows.join('\n')}\n`);
const paneCwd = (t: Tree, n: number, cwd: string): void => writeFileSync(join(t.fx, `${n}.cwd`), `${cwd}\n`);

function registry(t: Tree, pid: number | string, over: Readonly<Record<string, unknown>> = {}): void {
  const file = { pid: Number(pid), sessionId: ID, cwd: KB, name: 'kb', ...over };
  writeFileSync(join(t.home, '.claude', 'sessions', `${pid}.json`), JSON.stringify(file));
}

function transcript(t: Tree, folder: string, id: string, text: string): string {
  const dir = join(t.home, '.claude', 'projects', folder);
  mkdirSync(dir, { recursive: true });
  const file = join(dir, `${id}.jsonl`);
  writeFileSync(file, text);

  return file;
}

const folderOf = (cwd: string): string => projectDirNames(cwd)[0];

/** One pane running bash that runs claude, with its registry file and its transcript: the registry road, ready. */
function registryRoad(t: Tree, lines: readonly string[] = ['{"a":1}', '{"b":2}']): void {
  panes(t, S, `0.0 %1 ${PANE_PID}`);
  processes(t, `${PANE_PID} 1 bash`, `99000002 ${PANE_PID} claude`);
  registry(t, 99000002);
  transcript(t, folderOf(KB), ID, `${lines.join('\n')}\n`);
}

interface Outcome {
  readonly code: number;
  readonly stdout: string;
}

function started(shell: Shell, t: Tree, operands: readonly string[]): ChildProcess {
  return spawn(shell.path, [...shell.args, '-s', '--', ...operands], { env: { HOME: t.home, PATH: t.bin }, stdio: ['pipe', 'pipe', 'ignore'] });
}

/** Run the real script by `<shell> -s -- <operands>` with the script on stdin; the environment holds NOTHING else. */
function run(shell: Shell, t: Tree, operands: readonly string[]): Promise<Outcome> {
  return new Promise((resolve, reject) => {
    const child = started(shell, t, operands);
    let stdout = '';
    child.stdout?.setEncoding('utf8');
    child.stdout?.on('data', (text: string) => {
      stdout += text;
    });
    const timer = setTimeout(() => {
      child.kill('SIGKILL');
      reject(new Error(`the script did not end within 20 s: ${stdout.slice(-300)}`));
    }, 20_000);
    child.on('close', (code) => {
      clearTimeout(timer);
      resolve({ code: code ?? -1, stdout });
    });
    child.stdin?.end(REMOTE_SCRIPT);
  });
}

const operandsOfCall = (call: RemoteCall): readonly string[] => {
  if (call.kind === 'session') {
    return [call.session];
  }

  return call.kind === 'id' ? [call.session, 'id', call.id] : [call.session, 'titles', ...call.folders];
};

async function read(shell: Shell, t: Tree, call: RemoteCall): Promise<{ readonly outcome: Outcome; readonly read: RemoteRead }> {
  const outcome = await run(shell, t, operandsOfCall(call));

  return { outcome, read: readRemote(outcome, call) };
}

const logLines = (t: Tree): readonly string[] => (existsSync(join(t.fx, 'log')) ? readFileSync(join(t.fx, 'log'), 'utf8').split('\n').filter((line) => line.length > 0) : []);
const calls = (t: Tree, name: string): readonly string[] => logLines(t).filter((line) => line.startsWith(`${name} `));
const marker = (outcome: Outcome, name: string): readonly string[] => outcome.stdout.split('\n').filter((line) => line.startsWith(`${name} `) || line === name);

const whole = (outcome: Outcome): void => {
  assert.ok(outcome.stdout.startsWith('COAI-BEGIN 1\n') && outcome.stdout.endsWith('COAI-END\n'), `the frame is not whole: ${JSON.stringify(outcome.stdout.slice(-200))}`);
  assert.equal(outcome.code, 0);
};

shellTest('S2-T34: the registry path, end to end, through the real parser', async (shell) => {
  await withTree(async (t) => {
    // The second line is the sentinel itself, and the last line has no line end: neither may move the frame.
    const lines = ['{"a":1}', 'COAI-BEGIN 1', 'COAI-END', '{"d":4}'];
    panes(t, S, `0.0 %1 ${PANE_PID}`);
    processes(t, `${PANE_PID} 1 bash`, `99000002 ${PANE_PID} claude`);
    registry(t, 99000002);
    transcript(t, folderOf(KB), ID, lines.join('\n'));

    const { outcome, read: got } = await read(shell, t, CALL);

    whole(outcome);
    assert.ok(got.kind === 'found' && got.via === 'registry', `not found by the registry: ${JSON.stringify(got)}`);
    assert.deepEqual(got.kind === 'found' ? got.lines : [], lines, 'the transcript lines are not the 4 lines of the file');
    assert.equal(got.kind === 'found' && got.cut, false);
    assert.equal(calls(t, 'tmux').length, 1, 'one tmux call');
    assert.equal(calls(t, 'ps').length, 3, 'three ps calls: the pane, its children, the claude');
  });
});

shellTest('S2-T12: an empty glob does not eat the script, and no tool reads it', async (shell) => {
  await withTree(async (t) => {
    rmSync(join(t.home, '.claude', 'projects'), { recursive: true, force: true });
    writeFileSync(join(t.fx, 'eat'), '');
    panes(t, S, `0.0 %1 ${PANE_PID}`);
    processes(t, `${PANE_PID} 1 bash`);
    paneCwd(t, 1, '/home/git/kb');

    const fallback = await read(shell, t, CALL);
    whole(fallback.outcome);
    assert.deepEqual(fallback.read, { kind: 'fallback', paneCwd: '/home/git/kb', folders: [], skipped: 0, more: false }, 'a missing projects folder is not a fallback with no folders');

    const gone = await read(shell, t, ID_CALL);
    whole(gone.outcome);
    assert.deepEqual(gone.read, { kind: 'gone', sessionId: ID }, 'a missing projects folder became a failure instead of gone (the -f test of each match)');

    assert.ok(logLines(t).length > 0);
    if (process.platform === 'linux') {
      for (const line of logLines(t)) {
        assert.ok(line.endsWith('stdin=/dev/null'), `a tool call whose stdin is not /dev/null — the script could be read as its input: ${line}`);
      }
    }
  });
});

shellTest('S2-T13: a claude with a child is one candidate', async (shell) => {
  await withTree(async (t) => {
    panes(t, S, `0.0 %1 ${PANE_PID}`);
    processes(t, `${PANE_PID} 1 bash`, `99000002 ${PANE_PID} claude`, '99000003 99000002 node', '99000004 99000003 claude');
    registry(t, 99000002);
    transcript(t, folderOf(KB), ID, '{"a":1}\n');

    const one = await read(shell, t, CALL);
    assert.deepEqual(marker(one.outcome, 'COAI-CLAUDE'), ['COAI-CLAUDE 0.0 99000002 -'], 'one Claude CLI was read as two sessions');
    assert.ok(!calls(t, 'ps').some((line) => line.includes('--ppid 99000002')), 'the children of the claude were asked for');
    assert.equal(one.read.kind, 'found');

    processes(t, `${PANE_PID} 1 bash`, `99000002 ${PANE_PID} claude`, `99000005 ${PANE_PID} claude`);
    registry(t, 99000005, { sessionId: ID2, cwd: '/home/git/b', name: 'B' });
    const two = await read(shell, t, CALL);
    assert.ok(two.read.kind === 'ambiguous' && two.read.why === 'processes' && two.read.candidates.length === 2, `two sibling claude: ${JSON.stringify(two.read)}`);

    processes(t, `${PANE_PID} 1 claude`);
    registry(t, PANE_PID);
    const own = await run(shell, t, [S]);
    assert.deepEqual(marker(own, 'COAI-CLAUDE'), [`COAI-CLAUDE 0.0 ${PANE_PID} -`], 'the pane\'s own process is claude: one candidate, that pid');
  });
});

shellTest('S2-T14: several panes are a list, never a pick (change 1.1 point 2)', async (shell) => {
  const noPick = (outcome: Outcome): void => {
    for (const word of ['COAI-TRANSCRIPT', 'COAI-CWD', 'COAI-FOLDERS']) {
      assert.deepEqual(marker(outcome, word), [], `${word} was printed for a session with several panes: a pick was made`);
    }
  };
  await withTree(async (t) => {
    // Two panes in one window, and two windows.
    for (const second of ['0.1 %2 99000011', '1.0 %2 99000011']) {
      panes(t, S, `0.0 %1 ${PANE_PID}`, second);
      processes(t, `${PANE_PID} 1 bash`, '99000011 1 bash');
      const got = await read(shell, t, CALL);
      assert.ok(got.read.kind === 'ambiguous' && got.read.why === 'panes' && got.read.candidates.length === 0, `${second}: ${JSON.stringify(got.read)}`);
      noPick(got.outcome);
    }

    // One pane with two claude: every candidate that has a registry file.
    panes(t, S, `0.0 %1 ${PANE_PID}`);
    processes(t, `${PANE_PID} 1 bash`, `99000002 ${PANE_PID} claude`, `99000003 ${PANE_PID} claude`);
    registry(t, 99000002);
    registry(t, 99000003, { sessionId: ID2, cwd: '/home/git/b', name: 'B' });
    const twice = await read(shell, t, CALL);
    assert.ok(twice.read.kind === 'ambiguous' && twice.read.candidates.length === 2, JSON.stringify(twice.read));
    noPick(twice.outcome);

    // Two panes, ONE runs claude with a valid registry and a transcript: still a list with that one, and no transcript line.
    panes(t, S, `0.0 %1 ${PANE_PID}`, '0.1 %2 99000011');
    processes(t, `${PANE_PID} 1 bash`, `99000002 ${PANE_PID} claude`, '99000011 1 bash');
    transcript(t, folderOf(KB), ID, '{"a":1}\n');
    const one = await read(shell, t, CALL);
    assert.ok(one.read.kind === 'ambiguous' && one.read.candidates.length === 1, `a two-pane session was read without a choice: ${JSON.stringify(one.read)}`);
    noPick(one.outcome);

    // The companion: the same registry and transcript with ONE pane is found.
    panes(t, S, `0.0 %1 ${PANE_PID}`);
    processes(t, `${PANE_PID} 1 bash`, `99000002 ${PANE_PID} claude`);
    assert.equal((await read(shell, t, CALL)).read.kind, 'found');
  });
});

shellTest('S2-T15: nothing is left behind — no command waits for input, and nothing outlives the script', async (shell) => {
  await withTree(async (t) => {
    registryRoad(t);
    writeFileSync(join(t.fx, 'eat'), '');
    const child = started(shell, t, [S]);
    let stdout = '';
    child.stdout?.setEncoding('utf8');
    child.stdout?.on('data', (text: string) => {
      stdout += text;
    });
    const closed = new Promise<number>((resolve) => child.on('close', (code) => resolve(code ?? -1)));
    try {
      // The script is written and stdin is NOT closed: a command that reads stdin would wait here for the end of input.
      child.stdin?.write(REMOTE_SCRIPT);
      const deadline = Date.now() + 5_000;
      while (!stdout.includes('COAI-END\n') && Date.now() < deadline) {
        await new Promise((resolve) => setTimeout(resolve, 25));
      }
      assert.ok(stdout.includes('COAI-END\n'), `COAI-END did not come within 5 s with stdin still open — a tool waited for input: ${JSON.stringify(stdout.slice(-200))}`);

      child.stdin?.end();
      const code = await Promise.race([closed, new Promise<number>((resolve) => setTimeout(() => resolve(-999), 5_000))]);
      assert.equal(code, 0, 'the sh process did not end by itself when its stdin closed');
      for (const line of logLines(t)) {
        const pid = Number(line.split(' ')[1]);
        assert.throws(() => process.kill(pid, 0), `a fake is still alive: ${line}`);
      }
    } finally {
      // A red must not leave a shell and a fake waiting on an open pipe (a break-it that lets a tool read the script would hang the run).
      child.stdin?.destroy();
      child.kill('SIGKILL');
    }
  });
});

shellTest('S2-T35: every arm comes out of the real script', async (shell) => {
  await withTree(async (t) => {
    // fallback: a pane, no claude, three folders and one named with a space.
    panes(t, S, `0.0 %1 ${PANE_PID}`);
    processes(t, `${PANE_PID} 1 bash`);
    paneCwd(t, 1, '/home/git/kb');
    const names = ['-home-git-a', '-home-git-b', '-home-git-c', 'has space'];
    for (const name of names) {
      mkdirSync(join(t.home, '.claude', 'projects', name), { recursive: true });
    }
    const fallback = await read(shell, t, CALL);
    assert.deepEqual(fallback.read, { kind: 'fallback', paneCwd: '/home/git/kb', folders: names.slice(0, 3), skipped: 1, more: false });

    // titles: an ai-title, a custom-title, the ai-title again, and one raw line of 3000 bytes that holds "custom-title".
    const long = `{"type":"custom-title","customTitle":"${'x'.repeat(3000)}"}`;
    const rows = [JSON.stringify({ type: 'ai-title', aiTitle: 'A' }), JSON.stringify({ type: 'custom-title', customTitle: S }), long, JSON.stringify({ type: 'ai-title', aiTitle: 'A' })];
    transcript(t, '-home-git-a', ID, `${rows.join('\n')}\n`);
    const titles = await read(shell, t, { kind: 'titles', session: S, folders: ['-home-git-a', '-home-git-b'] });
    assert.ok(titles.read.kind === 'titles', JSON.stringify(titles.read));
    if (titles.read.kind === 'titles') {
      assert.equal(titles.read.sessions.length, 1);
      assert.deepEqual(titles.read.sessions[0]?.names, { latest: 'A', former: [S] });
      assert.equal(titles.read.scan.tooLong, 1);
      assert.equal(titles.read.scan.why, 'all');
    }

    // gone, and absent (no such tmux session).
    rmSync(join(t.home, '.claude', 'projects'), { recursive: true, force: true });
    assert.deepEqual((await read(shell, t, ID_CALL)).read, { kind: 'gone', sessionId: ID });
    assert.deepEqual((await read(shell, t, { kind: 'session', session: 'other-session' })).read, { kind: 'absent', what: 'session' });

    // absent transcript: a valid registry and no file; ambiguous transcripts: one id in two folders.
    registryRoad(t);
    rmSync(join(t.home, '.claude', 'projects'), { recursive: true, force: true });
    assert.deepEqual((await read(shell, t, CALL)).read, { kind: 'absent', what: 'transcript' });
    panes(t, S, `0.0 %1 ${PANE_PID}`);
    processes(t, `${PANE_PID} 1 bash`);
    transcript(t, '-home-git-a', ID, '{"a":1}\n');
    transcript(t, '-home-git-b', ID, '{"a":1}\n');
    assert.deepEqual((await read(shell, t, ID_CALL)).read, { kind: 'ambiguous', why: 'transcripts', candidates: [], unnamed: 0 });

    // too-many: a chain of 20 processes under the pane.
    const chain = Array.from({ length: 20 }, (_, index) => `${99000001 + index} ${index === 0 ? 1 : 99000000 + index} bash`);
    processes(t, ...chain);
    assert.deepEqual((await read(shell, t, CALL)).read, { kind: 'unreadable', why: 'too-many' });
  });
});

shellTest('S2-T35: a registry file that cannot be opened is registry-denied', async (shell) => {
  if (typeof process.getuid === 'function' && process.getuid() === 0) {
    return; // root reads a file of mode 000: there is nothing to observe
  }
  await withTree(async (t) => {
    registryRoad(t);
    chmodSync(join(t.home, '.claude', 'sessions', '99000002.json'), 0o000);

    assert.deepEqual((await read(shell, t, CALL)).read, { kind: 'unreadable', why: 'registry-denied' });
  });
});

shellTest('S2-T36: the session name is exact', async (shell) => {
  await withTree(async (t) => {
    panes(t, 'small', '0.0 %1 99000001');
    panes(t, 'small-tasks', '0.0 %7 99000007', '0.1 %8 99000008');
    processes(t, '99000001 1 bash');
    paneCwd(t, 1, '/home/git/kb');

    const got = await read(shell, t, { kind: 'session', session: 'small' });

    assert.deepEqual(marker(got.outcome, 'COAI-PANE'), ['COAI-PANE 0.0 %1 99000001'], 'the session name was not exact: small read small-tasks');
    for (const line of calls(t, 'tmux').filter((entry) => entry.includes('list-panes'))) {
      assert.ok(line.includes('-t =small:'), `a tmux list-panes without the exact target =<name>: — ${line}`);
    }
    assert.equal(got.read.kind, 'fallback');
  });
});

/** A listing of a whole tree: path, size, mtime and mode of every entry but those in `skip` (the fakes' folder and their log, which the fakes write). */
function listing(root: string, skip: readonly string[]): readonly string[] {
  const out: string[] = [];
  const walk = (dir: string): void => {
    for (const name of readdirSync(dir).sort()) {
      const path = join(dir, name);
      const info = statSync(path);
      if (!skip.includes(path)) {
        out.push(`${path} ${info.size} ${info.mtimeMs} ${info.mode}`);
      }
      if (info.isDirectory()) {
        walk(path);
      }
    }
  };
  walk(root);

  return out;
}

shellTest('S2-T37: the script only reads', async (shell) => {
  await withTree(async (t) => {
    registryRoad(t);
    transcript(t, '-home-git-a', ID2, `${JSON.stringify({ type: 'ai-title', aiTitle: 'A' })}\n`);
    const skip = [t.fx, join(t.fx, 'log')];
    const before = listing(t.root, skip);
    for (const call of [CALL, ID_CALL, { kind: 'titles', session: S, folders: ['-home-git-a'] } as const]) {
      await run(shell, t, operandsOfCall(call));
    }

    assert.deepEqual(listing(t.root, skip), before, 'a run changed the tree: the script wrote something');
    assert.deepEqual(readdirSync(t.root).sort(), ['bin', 'fx', 'home'], 'a new entry appeared in the run folder');
  });
});

shellTest('S2-T38: a tool that is not there is said, never read as "nothing found"', async (shell) => {
  const ninth = REMOTE_TOOLS.filter((tool) => tool !== 'tmux' && tool !== 'ps');
  const setup = (t: Tree): void => {
    registryRoad(t);
    transcript(t, '-home-git-a', ID2, `${JSON.stringify({ type: 'ai-title', aiTitle: 'A' })}\n`);
  };
  const forms: ReadonlyArray<{ readonly name: string; readonly call: RemoteCall; readonly runsTmux: boolean }> = [
    { name: 'session', call: CALL, runsTmux: true },
    { name: 'id', call: { kind: 'id', session: S, id: ID2 }, runsTmux: true },
    { name: 'titles', call: { kind: 'titles', session: S, folders: ['-home-git-a'] }, runsTmux: false },
  ];

  await withTree(async (t) => {
    setup(t);
    assert.equal((await read(shell, t, CALL)).read.kind, 'found', 'companion: with all the tools there, the session is found');
    const titles = await read(shell, t, forms[2].call);
    assert.ok(titles.read.kind === 'titles' && titles.read.sessions.length === 1, 'companion: with all the tools there, the titles answer with rows');
    panes(t, S, `0.0 %1 ${PANE_PID}`);
    processes(t, `${PANE_PID} 1 bash`);
    rmSync(join(t.home, '.claude', 'sessions'), { recursive: true, force: true });
    assert.equal((await read(shell, t, { kind: 'id', session: S, id: ID2 })).read.kind, 'found', 'companion: the id form is found');
    rmSync(join(t.home, '.claude', 'projects'), { recursive: true, force: true });
    assert.equal((await read(shell, t, { kind: 'id', session: S, id: ID2 })).read.kind, 'gone', 'companion: the id form with no file is gone');
  });

  for (const tool of REMOTE_TOOLS) {
    await withTree(async (t) => {
      setup(t);
      rmSync(join(t.bin, tool));
      for (const form of forms) {
        const got = await read(shell, t, form.call);
        const checked = form.runsTmux || ninth.includes(tool);
        if (checked) {
          assert.deepEqual(got.read, { kind: 'unreadable', why: 'tool' }, `${tool} is missing and the ${form.name} form did not say so: a missing tool was read as no session`);
        } else {
          assert.equal(got.read.kind, 'titles', `${tool} is not run by the titles form, which must still answer its rows`);
        }
      }
    });
  }
});

shellTest('S2-T39: a reader that is gone stops the script', async (shell) => {
  const manyPanes = (t: Tree): void => {
    const rows = Array.from({ length: 12 }, (_, index) => `0.${index} %${index + 1} ${99000100 + index}`);
    panes(t, 'many', ...rows);
    processes(t, ...rows.map((row) => `${row.split(' ')[2]} 1 bash`));
    writeFileSync(join(t.fx, 'slow'), '');
  };
  /** Starts the script, reads its first line, then closes the reader's end of stdout. */
  const readerGoes = async (t: Tree, command: string, args: readonly string[]): Promise<void> => {
    const child = spawn(command, args, { env: { HOME: t.home, PATH: t.bin }, stdio: ['pipe', 'pipe', 'ignore'] });
    const closed = new Promise<void>((resolve) => child.on('close', () => resolve()));
    child.stdout?.once('data', () => child.stdout?.destroy());
    child.stdin?.on('error', () => undefined);
    child.stdin?.end(REMOTE_SCRIPT);
    const ended = await Promise.race([closed.then(() => true), new Promise<boolean>((resolve) => setTimeout(() => resolve(false), 5_000))]);
    if (!ended) {
      child.kill('SIGKILL');
    }
    assert.ok(ended, 'the script went on after its reader was gone (it did not end within 5 s)');
    assert.ok(calls(t, 'ps').length < 6, `a closed reader did not stop the script: ${calls(t, 'ps').length} of 24 ps calls ran`);
  };

  await withTree(async (t) => {
    manyPanes(t);
    await readerGoes(t, shell.path, [...shell.args, '-s', '--', 'many']);
  });
  await withTree(async (t) => {
    manyPanes(t);
    // SIGPIPE ignored, as a parent can start a shell: write errors then come back as a status, and `|| return 0` ends the loop.
    await readerGoes(t, '/bin/sh', ['-c', `trap '' PIPE; exec ${shell.path} ${shell.args.join(' ')} -s -- many`]);
  });
});

shellTest('S2-T40: a tail over 4 MiB is cut, and says so', async (shell) => {
  await withTree(async (t) => {
    panes(t, S, `0.0 %1 ${PANE_PID}`);
    processes(t, `${PANE_PID} 1 bash`, `99000002 ${PANE_PID} claude`);
    registry(t, 99000002);
    const text = 'ж'.repeat(50);
    const all = Array.from({ length: 52_000 }, (_, index) => JSON.stringify({ i: index, t: text }));
    transcript(t, folderOf(KB), ID, `${all.join('\n')}\n`);

    const got = await read(shell, t, CALL);

    assert.ok(got.read.kind === 'found' && got.read.cut, `a 5 MiB transcript was not cut: ${got.read.kind}`);
    if (got.read.kind === 'found') {
      assert.doesNotThrow(() => got.read.kind === 'found' && got.read.lines.forEach((line) => JSON.parse(line)), 'a line of the cut tail does not parse: a part of a line was kept');
      assert.ok(Buffer.byteLength(got.read.lines.join('\n')) <= TAIL_MAX);
      assert.equal(got.read.lines[got.read.lines.length - 1], all[all.length - 1], 'the last line is not the file\'s last line');
    }
  });
});

shellTest('S2-T41: a name the script cannot frame is left out, not sent', async (shell) => {
  await withTree(async (t) => {
    panes(t, S, `0.0 %1 ${PANE_PID}`);
    processes(t, `${PANE_PID} 1 bash`);
    paneCwd(t, 1, '/home/git/kb');
    mkdirSync(join(t.home, '.claude', 'projects', 'a\nb'), { recursive: true });
    mkdirSync(join(t.home, '.claude', 'projects', 'my proj'), { recursive: true });
    mkdirSync(join(t.home, '.claude', 'projects', '-home-git-ok'), { recursive: true });

    const got = await read(shell, t, CALL);

    whole(got.outcome);
    assert.deepEqual(got.read, { kind: 'fallback', paneCwd: '/home/git/kb', folders: ['-home-git-ok'], skipped: 2, more: false });

    // A folder whose name holds a space is read on the registry path, by its exact name.
    const cwd = '/home/git/my proj';
    panes(t, S, `0.0 %1 ${PANE_PID}`);
    processes(t, `${PANE_PID} 1 bash`, `99000002 ${PANE_PID} claude`);
    registry(t, 99000002, { cwd });
    transcript(t, '-home-git-my proj', ID, '{"a":1}\n');
    const found = await read(shell, t, CALL);
    assert.ok(found.read.kind === 'found' && found.read.folder === '-home-git-my proj', JSON.stringify(found.read));
  });
});

shellTest('S2-T42: the start time in the frame is the process\'s own (Linux)', async (shell) => {
  if (process.platform !== 'linux') {
    return;
  }
  const stat = readFileSync('/proc/self/stat', 'utf8');
  const start = stat.slice(stat.lastIndexOf(') ') + 2).split(' ')[19];
  assert.match(start, /^\d+$/u, 'the test could not read field 22 of /proc/self/stat');
  await withTree(async (t) => {
    panes(t, S, `0.0 %1 ${PANE_PID}`);
    processes(t, `${PANE_PID} 1 bash`, `${process.pid} ${PANE_PID} claude`);
    transcript(t, folderOf(KB), ID, '{"a":1}\n');

    registry(t, process.pid, { procStart: start });
    assert.equal((await read(shell, t, CALL)).read.kind, 'found', 'a registry file with the process\'s own start time was refused');

    registry(t, process.pid, { procStart: String(Number(start) + 1) });
    assert.deepEqual((await read(shell, t, CALL)).read, { kind: 'unreadable', why: 'registry-stale' });
  });
});

shellTest('S2-T43: the title scan stops at its bounds', async (shell) => {
  await withTree(async (t) => {
    const dir = join(t.home, '.claude', 'projects', '-home-git-a');
    mkdirSync(dir, { recursive: true });
    const row = `${JSON.stringify({ type: 'ai-title', aiTitle: 'A' })}\n`;
    for (let index = 0; index < 251; index += 1) {
      writeFileSync(join(dir, `${randomUUID()}.jsonl`), row);
    }
    const call: RemoteCall = { kind: 'titles', session: S, folders: ['-home-git-a'] };

    // A clock that stands still, so that 251 files cannot hit the 8 s of the scan on a busy machine: the COUNT bound is what is tested here.
    rmSync(join(t.bin, 'date'));
    writeFileSync(join(t.bin, 'date'), '#!/bin/sh\necho 100\n', { mode: 0o755 });
    const count = await read(shell, t, call);
    assert.ok(count.read.kind === 'titles' && count.read.scan.why === 'count' && count.read.sessions.length === 250, `251 files: ${count.read.kind === 'titles' ? `${count.read.scan.why} ${count.read.sessions.length}` : count.read.kind}`);

    // A fake `date` that answers 100 and then 109: the second call is past the 8 s of the scan.
    rmSync(join(t.bin, 'date'));
    writeFileSync(join(t.bin, 'date'), `#!/bin/sh
n=0
[ -f "${t.fx}/date" ] && n=$(cat "${t.fx}/date")
echo $((n + 1)) > "${t.fx}/date"
if [ "$n" -eq 0 ]; then echo 100; else echo 109; fi
`, { mode: 0o755 });
    const time = await read(shell, t, call);
    assert.ok(time.read.kind === 'titles' && time.read.scan.why === 'time', `a clock 9 s on did not stop the scan: ${JSON.stringify(time.read).slice(0, 200)}`);
    assert.ok(time.read.kind === 'titles' && time.read.sessions.length < 250, 'the scan went on after its time ran out');
  });
});

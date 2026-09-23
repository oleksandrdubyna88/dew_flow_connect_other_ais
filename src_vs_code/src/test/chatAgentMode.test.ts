import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { test } from 'node:test';
import { ChatLaunch, NEW_CONVERSATION } from '../chatAdapter';
import { agyAdapter, AGY_ARGS } from '../agyAdapter';
import { claudeAdapter, CLAUDE_ARGS } from '../claudeAdapter';
import { codexAdapter } from '../codexAdapter';
import { CHAT_RUNTIMES, isFolder, launchSpecFor } from '../cliChatLaunch';
import { AGENT_BUDGETS, DEFAULT_BUDGETS, budgetsFor } from '../chatSession';
import { Vendor } from '../vendors';
import { accessDropped, accessNotice, accessOn, agentOffered, agentQuestion, agentRefusal } from '../chatAccessRules';
import { CONVERSATION_VERSION, recordFrom } from '../chatStore';
import { chatCommandOf } from '../chatMessages';
import { chatAgentToggleHtml } from '../chatAgentToggle';

/**
 * Agent mode — issue #289. A person asked a chat to translate a FILE and the chat could not open one:
 * every local vendor was launched text-only by design. Agent mode is the second launch, chosen per
 * conversation, with full access to the computer (the operator's decision of 2026-09-23). The flags are
 * the ones each CLI's own `--help` printed on that day.
 */

const AGENT: ChatLaunch = { ...NEW_CONVERSATION, access: 'agent' };

function vendor(runtime: Vendor['runtime']): Vendor {
  return {
    id: runtime,
    runtime,
    model: '',
    enabled: true,
    plan: true,
    code: true,
    baseUrl: '',
    executablePath: '',
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
  };
}

test('text mode is byte-identical to what every vendor was launched with before agent mode existed', () => {
  assert.deepEqual(agyAdapter.argv(NEW_CONVERSATION), AGY_ARGS);
  assert.deepEqual(claudeAdapter.argv(NEW_CONVERSATION), CLAUDE_ARGS);
  assert.deepEqual(codexAdapter.argv(NEW_CONVERSATION), ['exec', '--json', '-', '--skip-git-repo-check']);
});

test('agent mode lets claude act without asking', () => {
  const args = claudeAdapter.argv(AGENT);

  assert.deepEqual(args.slice(0, CLAUDE_ARGS.length), CLAUDE_ARGS);
  assert.deepEqual(args.slice(CLAUDE_ARGS.length), ['--permission-mode', 'bypassPermissions']);
});

test('agent mode takes agy out of plan mode and auto-approves its tools, keeping slash commands off', () => {
  const args = agyAdapter.argv(AGENT);

  assert.equal(args.includes('plan'), false, `plan mode writes nothing: ${args.join(' ')}`);
  assert.equal(args.includes('--mode'), false);
  assert.equal(args.includes('--dangerously-skip-permissions'), true);
  assert.equal(args.includes('--disable-slash-commands'), true);
  assert.deepEqual(args.slice(-4), ['--input-format', 'stream-json', '--output-format', 'stream-json']);
});

test('agent mode runs codex outside its sandbox, on a first turn and on a resumed one alike', () => {
  const first = codexAdapter.argv(AGENT);
  const resumed = codexAdapter.argv({ ...AGENT, resume: '0198a2b4-thread' });

  for (const args of [first, resumed]) {
    assert.equal(args.includes('--dangerously-bypass-approvals-and-sandbox'), true, args.join(' '));
    // `-s` is what `exec resume` refuses (CodexConsultant.cs), so it must never be the flag used.
    assert.equal(args.includes('-s'), false);
    // The bare `-` is the positional that makes codex read stdin; options go before it.
    assert.equal(args.at(-2), '-');
  }
  assert.deepEqual(resumed.slice(0, 3), ['exec', 'resume', '0198a2b4-thread']);
});

test('agent mode with a model still passes the model, for every runtime', () => {
  for (const runtime of CHAT_RUNTIMES) {
    const spec = launchSpecFor(vendor(runtime), os.tmpdir(), { ...AGENT, model: 'm-1' }, '', 'linux', os.tmpdir());

    assert.equal(spec.refusal, '', runtime);
    assert.equal(spec.args.includes('m-1'), true, `${runtime}: ${spec.args.join(' ')}`);
  }
});

test('agent mode runs in the conversation\'s workspace; text mode stays in the empty directory', () => {
  const workspace = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-agent-ws-'));
  const empty = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-agent-empty-'));
  try {
    for (const runtime of CHAT_RUNTIMES) {
      assert.equal(launchSpecFor(vendor(runtime), empty, AGENT, '', 'linux', workspace).cwd, workspace, runtime);
      assert.equal(launchSpecFor(vendor(runtime), empty, NEW_CONVERSATION, '', 'linux', workspace).cwd, empty, runtime);
    }
  } finally {
    fs.rmSync(workspace, { recursive: true, force: true });
    fs.rmSync(empty, { recursive: true, force: true });
  }
});

test('agent mode with no workspace is refused rather than let loose in the home directory', () => {
  const spec = launchSpecFor(vendor('claude'), os.tmpdir(), AGENT, '', 'linux', '');

  assert.equal(spec.executable, '');
  assert.match(spec.refusal, /agent mode needs a workspace folder/i);
});

test('agent mode in a folder that no longer exists is refused, naming the folder', () => {
  const gone = path.join(os.tmpdir(), 'coai-agent-deleted-folder-that-is-not-there');
  const spec = launchSpecFor(vendor('codex'), os.tmpdir(), AGENT, '', 'linux', gone);

  assert.equal(spec.executable, '');
  assert.ok(spec.refusal.includes(gone), spec.refusal);
});

test('a remote row is never launched in agent mode', () => {
  const remote: Vendor = { ...vendor('claude'), runtime: 'remote' };
  const spec = launchSpecFor(remote, os.tmpdir(), AGENT, '', 'linux', os.tmpdir());

  assert.equal(spec.executable, '');
  assert.notEqual(spec.refusal, '');
});

test('an agent turn gets twenty minutes; a text turn keeps its three', () => {
  assert.equal(budgetsFor('text'), DEFAULT_BUDGETS);
  assert.equal(budgetsFor('agent'), AGENT_BUDGETS);
  assert.equal(DEFAULT_BUDGETS.turnMs, 180_000);
  assert.equal(AGENT_BUDGETS.turnMs, 1_200_000);
  assert.equal(AGENT_BUDGETS.startupMs, DEFAULT_BUDGETS.startupMs);
});

// ---------------------------------------------------------------------------------------------
// The rules the host decides by, the record, the command and the box.
// ---------------------------------------------------------------------------------------------

test('the box is offered for a local model in a folder, and for nothing else', () => {
  assert.equal(agentOffered(false, 'D:/work'), true);
  assert.equal(agentOffered(true, 'D:/work'), false, 'a Team-server model was offered this computer');
  assert.equal(agentOffered(false, ''), false, 'a conversation with no folder was offered agent mode');
});

test('a relaunch keeps agent mode only where it can run, and drops it where it cannot', () => {
  assert.equal(accessOn(false, 'agent', 'D:/work'), 'agent');
  assert.equal(accessOn(true, 'agent', 'D:/work'), 'text', 'a switch to a Team-server model kept agent mode');
  assert.equal(accessOn(false, 'agent', ''), 'text');
  assert.equal(accessOn(false, 'text', 'D:/work'), 'text', 'a relaunch turned agent mode on by itself');
});

test('a refusal says which of the two things is missing, and nothing when neither is', () => {
  assert.match(agentRefusal(true, 'D:/work'), /Team server/);
  assert.match(agentRefusal(false, ''), /workspace folder/);
  assert.equal(agentRefusal(false, 'D:/work'), '');
});

test('the question asked before turning it on names the model, the folder and what it allows', () => {
  const asked = agentQuestion('claude-opus', 'D:/work');

  assert.ok(asked.includes('claude-opus') && asked.includes('D:/work'), asked);
  assert.match(asked, /read, write and run anything on this computer, without asking/);
});

test('the notices say which way it went, and a switch says only when it took agent mode away', () => {
  assert.match(accessNotice('agent', 'm'), /^Agent mode is on/);
  assert.match(accessNotice('text', 'm'), /^Agent mode is off/);
  assert.match(accessDropped('agent', 'text'), /Agent mode is off/);
  assert.equal(accessDropped('agent', 'agent'), '');
  assert.equal(accessDropped('text', 'text'), '');
});

function stored(over: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    version: CONVERSATION_VERSION,
    rev: 1,
    id: 'a1',
    title: 'main',
    passage: 'p',
    modelId: 'm',
    messages: [],
    workspace: 'D:/work',
    createdAt: 1,
    updatedAt: 2,
    ...over,
  };
}

test('a record written before agent mode existed reads back as a text conversation', () => {
  const read = recordFrom(stored());

  assert.notEqual(read, undefined);
  assert.equal(read?.access, undefined);
});

test('a conversation left in agent mode comes back in agent mode', () => {
  assert.equal(recordFrom(stored({ access: 'agent' }))?.access, 'agent');
});

test('an access this build does not know is a record it cannot trust', () => {
  for (const bad of ['text', 'Agent', true, 1, 'full']) {
    assert.equal(recordFrom(stored({ access: bad })), undefined, `read with access ${JSON.stringify(bad)}`);
  }
});

test('the box sends a real boolean or nothing', () => {
  assert.deepEqual(chatCommandOf({ type: 'command', command: 'access', agent: true }), { kind: 'access', agent: true });
  assert.deepEqual(chatCommandOf({ type: 'command', command: 'access', agent: false }), { kind: 'access', agent: false });
  // "false" is a truthy string: read as a tick it would hand a model the computer on a message that
  // said the opposite.
  for (const bad of ['false', 'true', 1, undefined, null]) {
    assert.deepEqual(chatCommandOf({ type: 'command', command: 'access', agent: bad }), { kind: 'ignore' }, String(bad));
  }
});

test('the box is drawn ticked with a warning outline in agent mode, and plain in text mode', () => {
  const on = chatAgentToggleHtml(true, 'agent', false);
  const off = chatAgentToggleHtml(true, 'text', false);

  assert.match(on, /class="agent agentOn"/);
  assert.match(on, /id="agent" checked/);
  assert.match(off, /class="agent"/);
  assert.equal(off.includes('checked'), false);
});

test('a box that is not offered is hidden, so a push can bring it back', () => {
  assert.match(chatAgentToggleHtml(false, 'text', false), /id="agentBox"[^>]* hidden/);
  assert.equal(chatAgentToggleHtml(true, 'text', false).includes(' hidden'), false);
});

test('the box cannot be changed while a turn runs', () => {
  assert.match(chatAgentToggleHtml(true, 'text', true), /<input[^>]* disabled/);
  assert.equal(chatAgentToggleHtml(true, 'text', false).includes('disabled'), false);
});

test('an agent launch tells cmd.exe not to look in the workspace for the programs a shim calls', () => {
  // MEASURED on 2026-09-23: the npm shim `codex.cmd` runs `node` by bare name, and cmd.exe searches the
  // working directory before the PATH — so a cloned repository with `node.cmd` in its root ran THAT
  // file, before the model had decided anything, the moment agent mode put the chat in the workspace.
  // With NoDefaultCurrentDirectoryInExePath=1 the same launch ran the real node. (Our own code review.)
  const workspace = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-agent-ws-'));
  try {
    for (const runtime of CHAT_RUNTIMES) {
      const spec = launchSpecFor(vendor(runtime), os.tmpdir(), AGENT, 'C:/npm/x.cmd', 'win32', workspace);
      assert.equal(spec.env['NoDefaultCurrentDirectoryInExePath'], '1', runtime);
    }
  } finally {
    fs.rmSync(workspace, { recursive: true, force: true });
  }
});

test('a text launch adds nothing to the environment, because it runs in an empty directory', () => {
  assert.deepEqual(launchSpecFor(vendor('codex'), os.tmpdir(), NEW_CONVERSATION, '', 'win32').env, {});
});

test('a workspace that is a FILE is refused, not handed to the CLI as a directory', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-agent-file-'));
  const file = path.join(dir, 'not-a-folder.txt');
  fs.writeFileSync(file, 'x');
  try {
    const spec = launchSpecFor(vendor('claude'), os.tmpdir(), AGENT, '', 'linux', file);

    assert.equal(spec.executable, '');
    assert.ok(spec.refusal.includes(file), spec.refusal);
    assert.equal(isFolder(file), false);
    assert.equal(isFolder(dir), true);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('the launch and the box refuse in the same words, from one place', () => {
  const remote: Vendor = { ...vendor('claude'), runtime: 'remote' };

  assert.equal(launchSpecFor(remote, os.tmpdir(), AGENT, '', 'linux', os.tmpdir()).refusal, agentRefusal(true, os.tmpdir()));
  assert.equal(launchSpecFor(vendor('claude'), os.tmpdir(), AGENT, '', 'linux', '').refusal, agentRefusal(false, ''));
});

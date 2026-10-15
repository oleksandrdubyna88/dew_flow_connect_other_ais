#!/usr/bin/env node
// Cuts a WINDOW of one real caller session into a sanitised fixture directory
// (todo/PLAN_a_round_records_who_wrote_the_code_and_how_much_E1.md, E1.S3). Every string that is not on the shape-checked
// allowlist (src_mcp/tests/fixtures/caller-transcripts/allowlist.json) becomes the sentinel, ids are remapped through one
// map so joins survive, and every path is rewritten under a fixture root. The rules live in scripts/lib/transcript-sanitise.mjs.
//
//   node scripts/cut-transcript-fixtures.mjs --session-file <dir>/<sessionId>.jsonl --out <case-dir> --salt <text>
//        --repo <real-prefix>=<fixture-root> [--repo …] [--other-root <fixture-root>] [--from <utc>] [--to <utc>]
//        [--drop-agent-file <agentId>] [--drop-meta <agentId>] [--clip-agents] [--include-agent <agentId>] [--allowlist <file>] [--out-anywhere]
//
// --clip-agents keeps only the window's lines of every agent file (default: whole files); --include-agent forces an agent and its chain of
// parents into the cut. Either one also keeps, outside the window, the single line that launched each wanted agent, so metas still join.
// Reads the named main file and the agent-<hex>.jsonl|.meta.json files of its own subagents/ folder — nothing else, never
// ~/.claude/sessions, never tool-results/. Writes <out>/projects/<slug>/<sessionId>.jsonl and the subagent files beside it.
// Refuses (exit 65) an --out outside src_mcp/tests/fixtures/caller-transcripts/ unless --out-anywhere (tests and scratch cuts),
// and a non-empty --out. Usage errors exit 2, an unreadable input 74. stdout: one JSON line of COUNTS; stderr: the counts of
// unknown keys and failed shapes (a new Claude Code field shows up here) — never a value from the transcript.
import { readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { Refusal, assertOutputAllowed, cutSession, loadAllowlist, parseRepoMapping } from './lib/transcript-sanitise.mjs';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const fixtureRoot = join(repoRoot, 'src_mcp', 'tests', 'fixtures', 'caller-transcripts');
const VALUE_FLAGS = new Set(['--session-file', '--out', '--salt', '--repo', '--other-root', '--from', '--to', '--drop-agent-file', '--drop-meta', '--include-agent', '--allowlist']);
const camel = (flag) => flag.slice(2).replace(/-(\w)/g, (_, c) => c.toUpperCase());

function assertUtcInstant(name, text) {
  if (text !== undefined && !(/Z$/.test(text) && !Number.isNaN(Date.parse(text)))) throw new Error(`${name} wants a UTC instant like 2026-10-01T10:00:00Z`);
}

function parseArgs(argv) {
  const opts = { repos: [], dropAgentFiles: [], dropMetas: [], includeAgents: [], clipAgents: false, outAnywhere: false };
  for (let i = 0; i < argv.length; i++) {
    const flag = argv[i];
    if (flag === '--out-anywhere') { opts.outAnywhere = true; continue; }
    if (flag === '--clip-agents') { opts.clipAgents = true; continue; }
    if (!VALUE_FLAGS.has(flag) || i + 1 >= argv.length) throw new Error(`unknown or incomplete argument ${flag}`);
    const value = argv[++i];
    if (flag === '--repo') opts.repos.push(parseRepoMapping(value));
    else if (flag === '--drop-agent-file') opts.dropAgentFiles.push(value);
    else if (flag === '--drop-meta') opts.dropMetas.push(value);
    else if (flag === '--include-agent') opts.includeAgents.push(value);
    else opts[camel(flag)] = value;
  }
  for (const required of ['--session-file', '--out', '--salt']) if (!opts[camel(required)]) throw new Error(`${required} is required`);
  assertUtcInstant('--from', opts.from);
  assertUtcInstant('--to', opts.to);
  return opts;
}

function main() {
  let opts;
  try { opts = parseArgs(process.argv.slice(2)); } catch (e) { process.stderr.write(`${e.message}\n`); return 2; }
  try {
    const out = resolve(opts.out);
    if (!opts.outAnywhere) assertOutputAllowed(out, fixtureRoot);
    const allowlist = loadAllowlist(JSON.parse(readFileSync(opts.allowlist ?? join(fixtureRoot, 'allowlist.json'), 'utf8')));
    const { unknownKeys, shapeFailures, lockedKeys, zeroedNumbers, ...counts } = cutSession({
      sessionFile: resolve(opts.sessionFile), outDir: out, allowlist, salt: opts.salt, repos: opts.repos, otherRoot: opts.otherRoot,
      from: opts.from, to: opts.to, dropAgentFiles: opts.dropAgentFiles, dropMetas: opts.dropMetas, clipAgents: opts.clipAgents, includeAgents: opts.includeAgents });
    process.stdout.write(`${JSON.stringify(counts)}\n`);
    process.stderr.write(`${JSON.stringify({ unknownKeys, shapeFailures, lockedKeys, zeroedNumbers })}\n`);
    return 0;
  } catch (e) {
    if (e instanceof Refusal) { process.stderr.write(`refused: ${e.message}\n`); return 65; }
    process.stderr.write(`cannot cut: ${e.code ?? e.name}\n`); // never e.message: an fs error names the operator's real path
    return 74;
  }
}

process.exitCode = main();

#!/usr/bin/env node
// Cuts a WINDOW of one real caller session into a sanitised fixture directory
// (research/PLAN_a_round_records_who_wrote_the_code_and_how_much_E1.md, E1.S3). Every string that is not on the shape-checked
// allowlist (src_mcp/tests/fixtures/caller-transcripts/allowlist.json) becomes the sentinel, ids are remapped through one
// map so joins survive, and every path is rewritten under a fixture root. The rules live in scripts/lib/transcript-sanitise.mjs.
//
//   node scripts/cut-transcript-fixtures.mjs --session-file <dir>/<sessionId>.jsonl --out <case-dir> --salt <32+ random chars>
//        --repo <real-prefix>=<fixture-root> [--repo …] [--other-root <fixture-root>] [--from <utc>] [--to <utc>]
//        [--drop-agent-file <agentId>] [--drop-meta <agentId>] [--clip-agents] [--include-agent <agentId>] [--case-spec <file>]
//        [--max-input-mb <n>] [--allowlist <file>] [--out-anywhere]
//
// Every <fixture-root> must be D:/fixture/repo-<a-z>, D:/fixture/other or D:/fixture/scratch (drive letter optional): anything else is
// refused before any input is read, so no real folder name can be typed into a fixture.
// --clip-agents keeps only the window's lines of every agent file (default: whole files); --include-agent forces an agent and its chain of
// parents into the cut. Either one also keeps, outside the window, the single line that launched each wanted agent, so metas still join.
// --max-input-mb (default 256) is the ceiling on the SUM of the session's files (main + subagent jsonl/meta): the cutter parses them all
// before it applies the window, so a larger session is refused up front. 256 MiB is above the largest session cut so far (229 MB) and
// keeps the parsed objects (several times the text) inside Node's default heap; raise it deliberately, not by habit.
// --case-spec <file> is a REAL case spec ({ rounds: { callerSessionId, gateSessions }, registry?: { pid, sessionId, cwd, version } }); its
// rounds.json and sessions/<pid>.json are written through the same sanitiser (ids, paths, synthetic clock). Without --session-file the
// case has no transcript (a caller that ran without persistence) and --case-spec is required. The spec holds real values: keep it OUTSIDE the repository.
// Reads the named main file and the agent-<hex>.jsonl|.meta.json files of its own subagents/ folder — nothing else, never
// ~/.claude/sessions, never tool-results/. Writes <out>/projects/<slug>/<sessionId>.jsonl and the subagent files beside it.
//
// Exit codes: 0 cut · 2 usage (unknown or incomplete argument, a salt under 32 characters, a malformed value) · 65 refused (an --out outside
// src_mcp/tests/fixtures/caller-transcripts/ unless --out-anywhere, a non-empty --out, a root off the policy, an input over the size limit,
// an input that is not valid JSON) · 74 an input could not be read or the output not written (the sentence says which kind, never a path).
// stdout: one JSON line of COUNTS; stderr: the counts of unknown keys and failed shapes (a new Claude Code field shows up here) — never a
// value from the transcript. Expected failures are returned values inside the script too; only the unexpected throws, and it is explained
// by lib/transcript-sanitise.mjs#explainFailure without its message (an fs error message names the operator's real path).
import { readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { DEFAULT_MAX_INPUT_BYTES, checkFixtureRoot, checkOutputAllowed, cutCaseWithoutTranscript, cutSession, explainFailure, loadAllowlist, parseRepoMapping } from './lib/transcript-sanitise.mjs';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const fixtureRoot = join(repoRoot, 'src_mcp', 'tests', 'fixtures', 'caller-transcripts');
const MIN_SALT_LENGTH = 32;
const VALUE_FLAGS = new Set(['--session-file', '--out', '--salt', '--repo', '--other-root', '--from', '--to', '--drop-agent-file', '--drop-meta', '--include-agent', '--case-spec', '--allowlist', '--max-input-mb']);
const camel = (flag) => flag.slice(2).replace(/-(\w)/g, (_, c) => c.toUpperCase());

const usage = (message) => ({ kind: 'usage', message });
const refused = (message) => ({ kind: 'refused', message });
const isUtcInstant = (text) => /Z$/.test(text) && !Number.isNaN(Date.parse(text));

/** The options, or `{ kind: 'usage' }`: a wrong command line is an expected outcome, not an exception. */
function parseArgs(argv) {
  const opts = { repos: [], dropAgentFiles: [], dropMetas: [], includeAgents: [], clipAgents: false, outAnywhere: false, maxInputBytes: DEFAULT_MAX_INPUT_BYTES };
  for (let i = 0; i < argv.length; i++) {
    const flag = argv[i];
    if (flag === '--out-anywhere') { opts.outAnywhere = true; continue; }
    if (flag === '--clip-agents') { opts.clipAgents = true; continue; }
    if (!VALUE_FLAGS.has(flag) || i + 1 >= argv.length) return usage(`unknown or incomplete argument ${flag}`);
    const value = argv[++i];
    if (flag === '--repo') {
      const mapping = parseRepoMapping(value);
      if (!mapping.ok) return usage(mapping.refusal);
      opts.repos.push(mapping.value);
    } else if (flag === '--max-input-mb') {
      if (!/^[1-9][0-9]{0,5}$/.test(value)) return usage('--max-input-mb wants a whole number of MiB, 1 or more');
      opts.maxInputBytes = Number(value) * 1024 * 1024;
    } else if (flag === '--drop-agent-file') opts.dropAgentFiles.push(value);
    else if (flag === '--drop-meta') opts.dropMetas.push(value);
    else if (flag === '--include-agent') opts.includeAgents.push(value);
    else opts[camel(flag)] = value;
  }
  return validate(opts);
}

function validate(opts) {
  for (const required of ['--out', '--salt']) if (!opts[camel(required)]) return usage(`${required} is required`);
  if (opts.salt.length < MIN_SALT_LENGTH) return usage(`--salt must be at least ${MIN_SALT_LENGTH} characters (random hex, kept OUTSIDE the repository): a short or guessable salt lets anyone holding an original id confirm it in a fixture`);
  if (!opts.sessionFile && !opts.caseSpec) return usage('--session-file or --case-spec is required');
  for (const [name, text] of [['--from', opts.from], ['--to', opts.to]]) if (text !== undefined && !isUtcInstant(text)) return usage(`${name} wants a UTC instant like 2026-10-01T10:00:00Z`);
  return { kind: 'options', opts };
}

/** A JSON file as a value: unreadable throws (unexpected, explained by the caller), not-JSON is an expected refusal naming only WHAT. */
function readJson(path, what) {
  const text = readFileSync(path, 'utf8');
  try { return { kind: 'json', value: JSON.parse(text) }; } catch { return refused(`${what} is not valid JSON`); }
}

/** Everything that can be refused, in the order that keeps real input unread as long as possible. */
function load(opts) {
  const rootsOk = [...opts.repos.map((r) => r.to), ...(opts.otherRoot === undefined ? [] : [opts.otherRoot])].map((root) => checkFixtureRoot(root)).find((r) => !r.ok);
  if (rootsOk) return refused(rootsOk.refusal);
  const out = resolve(opts.out);
  const allowed = opts.outAnywhere ? { ok: true } : checkOutputAllowed(out, fixtureRoot);
  if (!allowed.ok) return refused(allowed.refusal);
  const allowlistJson = readJson(opts.allowlist ?? join(fixtureRoot, 'allowlist.json'), 'the allowlist');
  if (allowlistJson.kind !== 'json') return allowlistJson;
  const specJson = opts.caseSpec ? readJson(opts.caseSpec, 'the case spec') : { kind: 'json', value: undefined };
  if (specJson.kind !== 'json') return specJson;
  return { kind: 'ready', out, allowlist: loadAllowlist(allowlistJson.value), caseSpec: specJson.value };
}

function cut(opts, ready) {
  const common = { outDir: ready.out, allowlist: ready.allowlist, salt: opts.salt, repos: opts.repos, otherRoot: opts.otherRoot, caseSpec: ready.caseSpec };
  return opts.sessionFile
    ? cutSession({ ...common, sessionFile: resolve(opts.sessionFile), from: opts.from, to: opts.to, dropAgentFiles: opts.dropAgentFiles,
      dropMetas: opts.dropMetas, clipAgents: opts.clipAgents, includeAgents: opts.includeAgents, maxInputBytes: opts.maxInputBytes })
    : cutCaseWithoutTranscript(common);
}

function report(done) {
  const { unknownKeys, shapeFailures, lockedKeys, zeroedNumbers, ...counts } = done.value;
  process.stdout.write(`${JSON.stringify(counts)}\n`);
  process.stderr.write(`${JSON.stringify({ unknownKeys, shapeFailures, lockedKeys, zeroedNumbers })}\n`);
}

function main() {
  const parsed = parseArgs(process.argv.slice(2));
  if (parsed.kind === 'usage') { process.stderr.write(`${parsed.message}\n`); return 2; }
  try {
    const ready = load(parsed.opts);
    if (ready.kind === 'refused') { process.stderr.write(`refused: ${ready.message}\n`); return 65; }
    const done = cut(parsed.opts, ready);
    if (!done.ok) { process.stderr.write(`refused: ${done.refusal}\n`); return 65; }
    report(done);
    return 0;
  } catch (e) {
    const failure = explainFailure(e);
    process.stderr.write(`cannot cut: ${failure.sentence}\n`); // never e.message: an fs error names the operator's real path
    return failure.exit;
  }
}

process.exitCode = main();

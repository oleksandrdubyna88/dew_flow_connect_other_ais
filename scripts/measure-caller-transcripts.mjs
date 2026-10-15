#!/usr/bin/env node
// Measures the caller's Claude Code transcripts against a store's code rounds, as COUNTS ONLY
// (research/RESULTS_caller_transcripts_on_both_sides.md, E1.S2 of PLAN_a_round_records_who_wrote_the_code_and_how_much_E1.md).
//
//   node scripts/measure-caller-transcripts.mjs --rounds <rounds.json> --projects <dir> [--largest <n>] [--runs <n>] [--day <yyyy-mm-dd>]
//
// `rounds.json` is what `scripts/export-code-rounds.py` writes: one object per code round (`caller`, `caller_vendor`,
// `started_utc`, …). `--projects` is a Claude Code `projects` folder (`~/.claude/projects`). `--largest` (default 1) is how
// many of the biggest SESSIONS (a main file plus its `subagents/` folder) are timed; `--runs` (default 7) is the number of
// timed passes of each of a full parse and a day-gated parse; `--day` is the UTC day the gate keeps (default: the day holding
// most of that session's lines). Prints ONE JSON line: counts, booleans, durations, version strings, model ids and key names
// — never a value that could carry text. Reads only; writes nothing, under `--projects` or anywhere; never opens
// `~/.claude/sessions`. The time is NODE's, on this machine: not the C# reader's (measurement.md rule 3).
//
// Exit codes: 0 ok · 65 bad arguments or unreadable input (never 64: that is the family's "no such mode"). Run it on a machine
// you are not also loading, or say what else ran: the first pass of each kind is only as cold as the OS file cache is.
import { readFileSync } from 'node:fs';
import { isDay, measure } from './lib/caller-transcripts.mjs';

const FLAGS = new Set(['--rounds', '--projects', '--largest', '--runs', '--day']);
const usage = 'usage: measure-caller-transcripts.mjs --rounds <rounds.json> --projects <dir> [--largest <n>] [--runs <n>] [--day <yyyy-mm-dd>]';

/** `{ options }` or `{ refusal }`; an unknown flag, a missing value or a non-positive count is refused, naming what was legal. */
function parseArguments(argv) {
  const options = {};
  for (let at = 0; at < argv.length; at += 2) {
    if (!FLAGS.has(argv[at]) || at + 1 >= argv.length) return { refusal: `unknown or valueless argument; legal: ${[...FLAGS].join(' ')}` };
    options[argv[at].slice(2)] = argv[at + 1];
  }
  if (!options.rounds || !options.projects) return { refusal: '--rounds and --projects are required' };
  const counts = { largest: options.largest ?? '1', runs: options.runs ?? '7' };
  for (const [name, text] of Object.entries(counts)) {
    if (!/^\d+$/u.test(text) || Number(text) < (name === 'runs' ? 1 : 0)) return { refusal: `--${name} must be a whole number >= ${name === 'runs' ? 1 : 0}` };
  }
  if (options.day !== undefined && !isDay(options.day)) return { refusal: '--day must be a UTC day, yyyy-mm-dd' };
  return { options: { rounds: options.rounds, projects: options.projects, largest: Number(counts.largest), runs: Number(counts.runs), day: options.day ?? '' } };
}

function readRounds(path) {
  try {
    const rounds = JSON.parse(readFileSync(path, 'utf8'));
    return Array.isArray(rounds) ? { rounds } : { refusal: 'the rounds file is not a JSON array' };
  } catch {
    // The parser's own message quotes the text it choked on; this file holds session ids and paths, so say nothing of it.
    return { refusal: 'the rounds file cannot be read as JSON' };
  }
}

function main(argv) {
  const parsed = parseArguments(argv);
  if (parsed.refusal) return refuse(parsed.refusal);
  const read = readRounds(parsed.options.rounds);
  if (read.refusal) return refuse(read.refusal);
  process.stdout.write(`${JSON.stringify(measure({ ...parsed.options, rounds: read.rounds, projectsDir: parsed.options.projects }))}\n`);
  return 0;
}

function refuse(sentence) {
  console.error(`${sentence}\n${usage}`);
  return 65;
}

process.exitCode = main(process.argv.slice(2));

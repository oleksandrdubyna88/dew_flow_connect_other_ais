// lib/cli-args.mjs - the argument parser the Vibe manifest script, the Vibe confinement probe and the native host probe share.
//
// Why it exists (E2.S0 follow-up): `vibe-home-manifest.mjs --unstage` with NO value read `flag('--unstage', '')` as '', skipped the unstage branch and fell
// through to measure() - which STARTED Vibe and spent a paid turn. The manifest script and the Vibe probe each had a lookup that treated "a value-taking flag with no value" as
// "the flag was not given" (the probe's worst case: `--cells` with no value fell back to EVERY cell, a full paid run); the host probe's own parser already refused a trailing
// value-less flag and any unknown flag, and only took another `--flag` as a value (`--series --kimi`). Here that is a usage error, decided by a PURE function before anything is launched, so a test can prove it without starting a script.
//
// The rules (a script that gets any other argv prints the error and exits 65, the usage code, before it reads a file or starts a process):
//   - a flag in `values` takes exactly ONE value: it must be present, non-empty and not itself a `--flag`; with `safe`, it must also match that pattern;
//   - a flag in `booleans` takes none;
//   - an unknown `--flag`, a stray word (no flag claims it) and a repeated flag are errors - so `--help` can never start a measurement.
//
// Arity audit (every flag of the three scripts; "1" = one value, "0" = boolean):
//   vibe-home-manifest.mjs       --vibe 1, --scratch 1, --from 1, --unstage 1, --write 0, --stage 0
//   probe-vibe-confinement.mjs   --vibe 1, --scratch 1, --repeats 1, --cells 1, --cli 1, --arms 1, --reparse 1
//   probe-native-linux-host.mjs  --series 1, --scratch 1, --kimi 1, --coai-mcp 1, --vibe-python 1   (every value also held to the safe pattern)

/** The flags of each script, as data - the audit above, and what the tests walk. */
export const FLAG_SPECS = {
  manifest: { values: ['--vibe', '--scratch', '--from', '--unstage'], booleans: ['--write', '--stage'] },
  vibeProbe: { values: ['--vibe', '--scratch', '--repeats', '--cells', '--cli', '--arms', '--reparse'], booleans: [] },
  hostProbe: { values: ['--series', '--scratch', '--kimi', '--coai-mcp', '--vibe-python'], booleans: [], safe: /^[A-Za-z0-9_.,=/:@-]+$/u },
};

const refuse = (error) => ({ ok: false, values: {}, flags: [], error });

/** The value `argv[at + 1]` is for `flag`, or the reason it is none. */
function valueAfter(argv, at, flag, safe) {
  const value = argv[at + 1];
  if (value === undefined || value === '' || value.startsWith('--')) return { error: `${flag} needs a value (got ${value === undefined ? 'nothing' : JSON.stringify(value)})` };
  return safe && !safe.test(value) ? { error: `${flag}: the value ${JSON.stringify(value)} holds characters outside the safe set` } : { value };
}

/** One token: a boolean, a value-taking flag (consuming the next token) or an error. Returns the next index too. */
function take(argv, at, spec, seen) {
  const token = argv[at];
  if (seen.has(token)) return { error: `${token} was given twice` };
  if (spec.booleans.includes(token)) return { next: at + 1, flag: token };
  if (!spec.values.includes(token)) return { error: token.startsWith('--') ? `unknown flag ${token}` : `unexpected argument ${JSON.stringify(token)}` };
  const read = valueAfter(argv, at, token, spec.safe);
  return read.error ? read : { next: at + 2, name: token, value: read.value };
}

/**
 * Parses `argv` (without node and the script) against `spec` ({ values, booleans, safe? }):
 * `{ ok: true, values: { '--flag': 'v' }, flags: ['--write'], error: '' }` or `{ ok: false, values: {}, flags: [], error }` naming the flag.
 */
export function parseFlags(argv, spec) {
  const seen = new Set();
  const values = {};
  const flags = [];
  for (let at = 0; at < argv.length;) {
    const step = take(argv, at, { booleans: [], ...spec }, seen);
    if (step.error) return refuse(step.error);
    seen.add(argv[at]);
    if (step.name) values[step.name] = step.value;
    if (step.flag) flags.push(step.flag);
    at = step.next;
  }
  return { ok: true, values, flags, error: '' };
}

/**
 * What `vibe-home-manifest.mjs` is being asked to do: `usage` (with the error; the script exits 65 and starts nothing), `unstage` (moves a staged entry, no launch),
 * `from` (rebuilds from a saved run, no launch) or `measure` (starts Vibe). A missing value is `usage`, never `measure`.
 */
export function manifestPlan(argv) {
  const parsed = parseFlags(argv, FLAG_SPECS.manifest);
  if (!parsed.ok) return { mode: 'usage', ...parsed };
  const mode = '--unstage' in parsed.values ? 'unstage' : '--from' in parsed.values ? 'from' : 'measure';
  return { mode, ...parsed };
}

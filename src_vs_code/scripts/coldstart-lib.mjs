/**
 * The pure parts of the cold-start measurement (v2 extension.md § 2 "Measurement"; epic E2, story S2): what a sample is,
 * how M1/M2/M3 are read off its marks, how three samples become a verdict, and the shape of the JSON and the table row.
 * Nothing here starts an editor or touches a file, so `src/test/measureLib.test.mjs` can run every rule of it.
 *
 * <p><b>The three numbers.</b></p>
 * <ul>
 *   <li><b>M1</b> — module evaluation to `activate` return, in ms: `activateReturn.at - eval.at`, both marks made by the shell
 *       (`shell/measure.ts`). Limit 50 ms. Nothing is loaded lazily inside `activate`, so this is the entry's own time.</li>
 *   <li><b>M2</b> — the driver's clock, started just before it runs `workbench.view.extension.coai`, to the sidebar's webview
 *       `ready` acknowledgement reaching the host: `ready.at - t0`, using the FIRST `ready` made at or after `t0`. Limit 2000 ms,
 *       and the MAXIMUM of the samples counts.</li>
 *   <li><b>M3</b> — the same clock to the first state handed to the webview (`firstData`). Recorded, never a limit.</li>
 * </ul>
 *
 * <p><b>What makes a sample not count.</b> The measure is a COLD start, so a sample whose extension was already active when
 * the clock started is refused (restored view state activated it before the clock), and so is a sample with no `ready` made
 * after the clock started — a missing acknowledgement is a failure, never a zero. `recordOnly` (the v1 baseline) turns the
 * limits off and lets the extension have been active, because v1 activates itself at start-up (`onStartupFinished`).</p>
 *
 * <p>Marks are wall-clock milliseconds since 1970 (`performance.timeOrigin + performance.now()`), taken in the extension host;
 * the driver reads its own clock the same way in the same host, so the subtraction is exact to a fraction of a millisecond.</p>
 */

export const SCHEMA = 1;

/** The limits of the design: M2 (command to ready ack) at most 2000 ms in every sample, M1 (own time of the entry) at most 50 ms. */
export const LIMITS = Object.freeze({ m2MaxMs: 2000, m1MaxMs: 50 });

/** How many cold launches make one measurement. */
export const SAMPLES = 3;

const round = (value) => (value === null ? null : Math.round(value * 10) / 10);
const first = (marks, name, notBefore = Number.NEGATIVE_INFINITY) => marks.find((one) => one.name === name && one.at >= notBefore);

/**
 * One sample's numbers and problems, from the raw record a driver wrote:
 * `{ schema, sample, t0, startedActive, marks: [{ name, at }], host: { remoteName, platform, ... } }`.
 */
export function deriveSample(raw, { recordOnly = false } = {}) {
  const problems = [];
  const marks = Array.isArray(raw?.marks) ? raw.marks : [];
  // A driver that could not finish says why in `error` (the Remote-WSL driver does); that sentence is the first problem, not a guess.
  if (typeof raw?.error === 'string') {
    problems.push(`the driver failed: ${raw.error}`);
  }
  if (raw?.schema !== SCHEMA) {
    problems.push(`the record has schema ${String(raw?.schema)}, not ${SCHEMA}`);
  }
  if (!Number.isFinite(raw?.t0)) {
    problems.push('the record has no driver clock (t0)');
  }
  const t0 = Number.isFinite(raw?.t0) ? raw.t0 : Number.NaN;
  if (raw?.startedActive !== false && !recordOnly) {
    problems.push(raw?.startedActive === true
      ? 'the extension was already active when the clock started, so this is not a cold start'
      : 'the record does not say whether the extension was active when the clock started');
  }

  const evaluated = first(marks, 'eval');
  const returned = evaluated === undefined ? undefined : first(marks, 'activateReturn', evaluated.at);
  const m1 = evaluated !== undefined && returned !== undefined ? returned.at - evaluated.at : null;
  if (m1 === null && !recordOnly) {
    problems.push('the marks have no eval -> activateReturn pair, so M1 cannot be read');
  }

  const ready = first(marks, 'ready', t0);
  const m2 = ready === undefined ? null : ready.at - t0;
  if (ready === undefined) {
    problems.push(marks.some((one) => one.name === 'ready')
      ? 'the only ready acknowledgement is older than the driver clock: no fresh ack'
      : 'no ready acknowledgement from the sidebar: the webview never said it had painted');
  }
  const data = first(marks, 'firstData', t0);
  const m3 = data === undefined ? null : data.at - t0;

  return {
    sample: raw?.sample ?? null,
    startedActive: raw?.startedActive ?? null,
    location: raw?.host?.remoteName ?? 'local',
    platform: raw?.host?.platform ?? null,
    vscodeVersion: raw?.host?.vscodeVersion ?? null,
    m1: round(m1),
    m2: round(m2),
    m3: round(m3),
    problems,
  };
}

/** min / median / max of the numbers that exist, or `null` when none does. */
export function stats(values) {
  const sorted = values.filter((one) => one !== null).sort((a, b) => a - b);
  if (sorted.length === 0) {
    return null;
  }
  const middle = sorted.length >> 1;
  const median = sorted.length % 2 === 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;

  return { min: round(sorted[0]), median: round(median), max: round(sorted.at(-1)) };
}

/**
 * Raw records to the verdict. `expected` is how many launches the measurement needs (a record missing for a failed launch
 * is a missing sample, which is a failure, not a smaller measurement).
 */
export function summarize(raws, { expected = SAMPLES, recordOnly = false, limits = LIMITS } = {}) {
  const samples = raws.map((raw) => deriveSample(raw, { recordOnly }));
  const problems = samples.flatMap((one, at) => one.problems.map((problem) => `sample ${one.sample ?? at + 1}: ${problem}`));
  if (samples.length !== expected) {
    problems.push(`${samples.length} sample(s) came back, ${expected} were needed`);
  }
  const m1 = stats(samples.map((one) => one.m1));
  const m2 = stats(samples.map((one) => one.m2));
  const m3 = stats(samples.map((one) => one.m3));
  const locations = [...new Set(samples.map((one) => one.location))];
  if (locations.length > 1) {
    problems.push(`the samples ran in different places: ${locations.join(', ')}`);
  }
  if (!recordOnly) {
    if (m2 !== null && m2.max > limits.m2MaxMs) {
      problems.push(`M2 max ${m2.max} ms is over the ${limits.m2MaxMs} ms limit`);
    }
    if (m1 !== null && m1.max > limits.m1MaxMs) {
      problems.push(`M1 max ${m1.max} ms is over the ${limits.m1MaxMs} ms limit`);
    }
  }

  return {
    schema: SCHEMA,
    kind: 'coai-cold-start',
    recordOnly,
    limits: recordOnly ? null : { ...limits },
    // The extension host's place: 'local', or the remote's name ('wsl', 'ssh-remote', ...).
    location: locations.length === 1 ? locations[0] : null,
    platform: samples[0]?.platform ?? null,
    vscodeVersion: samples[0]?.vscodeVersion ?? null,
    samples: samples.map(({ problems: _problems, ...rest }) => rest),
    m1,
    m2,
    m3,
    pass: problems.length === 0,
    problems,
  };
}

const cell = (value) => (value === null || value === undefined ? 'n/a' : String(value));
const triple = (one) => (one === null ? 'n/a' : `${one.min} / ${one.median} / ${one.max}`);

/** The markdown row `research/RESULTS_v2_start.md` takes: date, commit, label, place, platform, M1 max, M2 and M3 min/median/max, verdict. */
export function tableRow(result, { date, commit, label }) {
  const verdict = result.recordOnly ? 'recorded' : result.pass ? 'pass' : 'FAIL';
  const cells = [date, commit, label, cell(result.location), cell(result.platform), cell(result.m1?.max), triple(result.m2), triple(result.m3), verdict];

  return `| ${cells.map((one) => String(one).replaceAll('|', '/')).join(' | ')} |`;
}

/** The header the table in `research/RESULTS_v2_start.md` has, so a row written by a script and the table cannot drift apart unseen. */
export const TABLE_HEADER = '| date | commit | label | host location | platform | M1 max ms | M2 min / median / max ms | M3 min / median / max ms | verdict |';

/** The flags of `run-host.mjs measure` and `coldstart-report.mjs`; an unknown flag throws, so a typo is not a silent default. */
export function parseMeasureArgs(argv) {
  const options = { samples: SAMPLES, json: undefined, row: false, label: undefined, commit: undefined, recordOnly: false, inputs: [] };
  const wants = (flag, at) => {
    const value = argv[at + 1];
    if (value === undefined || value.startsWith('--')) {
      throw new Error(`${flag} needs a value`);
    }

    return value;
  };
  for (let at = 0; at < argv.length; at += 1) {
    const flag = argv[at];
    if (flag === '--samples') {
      options.samples = Number(wants(flag, at++));
      if (!Number.isInteger(options.samples) || options.samples < 1) {
        throw new Error('--samples needs a whole number of at least 1');
      }
    } else if (flag === '--json') {
      options.json = wants(flag, at++);
    } else if (flag === '--label') {
      options.label = wants(flag, at++);
    } else if (flag === '--commit') {
      options.commit = wants(flag, at++);
    } else if (flag === '--row') {
      options.row = true;
    } else if (flag === '--record-only') {
      options.recordOnly = true;
    } else if (flag.startsWith('--')) {
      throw new Error(`unknown flag ${flag}`);
    } else {
      options.inputs.push(flag);
    }
  }

  return options;
}

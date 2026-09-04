/**
 * Does the gate's ORDER change what a model does with a plan — and does the order NOT to split
 * again actually stop it?
 *
 * Three arms over the same real plans, through the product's own shim:
 *   plain    — the plan, and "you are about to implement this; how will you proceed?"
 *   commands — the same, with the preamble and commands the gate would actually return on a FIRST
 *              plan round, verbatim (the fixtures are written by a test that calls the product).
 *   epic     — the same, with what the gate returns to a plan that is already a PIECE of a split.
 *
 * Nothing in the task prompt mentions epics, stories, reviews, commits, Fable or when to ask a
 * question: if the plain arm produces them anyway, the commands bought nothing, and that is the
 * result worth knowing.
 *
 * Two runs, on two different models, because the shim seeds the request from the prompt BYTES —
 * the same prompt is deliberately the same request, so repeating one measures the engine's
 * determinism rather than the instruction's effect. A second model is a second opinion.
 *
 * Strictly sequential. One card, one caller — the operator's rule, and the lease would enforce it
 * anyway.
 *
 * usage: node scratch-campaign.mjs <coai-mcp.exe> <model> <repo> <runLabel> <plan> [plan...]
 */
import { spawn } from 'node:child_process';
import { mkdtempSync, writeFileSync, readFileSync, existsSync, mkdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const [exe, model, repo, runLabel] = process.argv.slice(2, 6);
const plans = process.argv.slice(6);
const fixtures = join(repo, 'artifacts', 'commands');
const outDir = join(repo, 'artifacts', 'campaign');
mkdirSync(outDir, { recursive: true });
const dir = mkdtempSync(join(tmpdir(), 'coai-campaign-'));

const SCHEMA = {
  type: 'object',
  required: ['approach', 'units', 'reviewsEachUnit', 'commitsEachUnit', 'questionPolicy', 'modelPolicy'],
  additionalProperties: false,
  properties: {
    approach: { type: 'string' },
    units: {
      type: 'array',
      items: {
        type: 'object',
        required: ['title', 'kind', 'children'],
        additionalProperties: false,
        properties: {
          title: { type: 'string' },
          kind: { type: 'string' },
          children: { type: 'array', items: { type: 'string' } },
        },
      },
    },
    reviewsEachUnit: { type: 'boolean' },
    commitsEachUnit: { type: 'boolean' },
    questionPolicy: { type: 'string' },
    modelPolicy: { type: 'string' },
  },
};

const schemaPath = join(dir, 'schema.json');
writeFileSync(schemaPath, JSON.stringify(SCHEMA));

const TASK =
  'You are about to implement the plan below in a real repository. Describe HOW you will proceed:\n' +
  '- `approach`: two sentences on your overall approach.\n' +
  '- `units`: the pieces you will do the work in, in order. `kind` is your own word for what a unit is.\n' +
  '  `children` are its sub-pieces if it has any, otherwise an empty array.\n' +
  '- `reviewsEachUnit`: will you put EVERY unit through a code review before starting the next one?\n' +
  '- `commitsEachUnit`: will you commit after every unit?\n' +
  '- `questionPolicy`: when will you ask the person questions?\n' +
  '- `modelPolicy`: which model will you use for which part?\n' +
  'Answer with JSON only.';

function ask(label, prompt) {
  const promptFile = join(dir, `${label}.txt`);
  const outFile = join(dir, `${label}.json`);
  writeFileSync(promptFile, prompt);
  const started = Date.now();
  const child = spawn(
    exe,
    [
      '--ask-local',
      '--endpoint', 'http://127.0.0.1:11434/v1',
      '--model', model,
      '--prompt-file', promptFile,
      '--schema-file', schemaPath,
      '--out', outFile,
      '--timeout-seconds', '600',
      '--reasoning-effort', 'none',
      '--max-tokens', '8192',
    ],
    { stdio: ['ignore', 'pipe', 'pipe'] },
  );
  let stderr = '';
  child.stderr.on('data', (b) => (stderr += b.toString()));

  return new Promise((resolve) => {
    child.on('close', (code) => {
      let answer = null;
      if (existsSync(outFile)) {
        try {
          answer = JSON.parse(readFileSync(outFile, 'utf8'));
        } catch {
          answer = null;
        }
      }
      resolve({
        code,
        seconds: Math.round((Date.now() - started) / 100) / 10,
        answer,
        error: code === 0 ? '' : (stderr.trim().split('\n').at(-1) ?? '').slice(0, 160),
      });
    });
  });
}

/**
 * What can be counted without reading: the SHAPE of the answer, never its prose.
 *
 * The words are the weak half and are kept only for the record — the smoke run showed a model
 * obeying "2-4 stories" perfectly while calling them "Core Abstraction" and "Data Layer". What the
 * order actually asks for is a COUNT and a DEPTH, and those can be checked: 2-4 units, flat for a
 * stories verdict, two levels of 2-4 for an epics one.
 */
function score(run, verdict) {
  const a = run.answer;
  if (a === null) {
    return { ok: false };
  }
  const units = a.units ?? [];
  const kidCounts = units.map((u) => u.children?.length ?? 0);
  const children = kidCounts.reduce((n, k) => n + k, 0);
  const kinds = units.map((u) => `${u.kind ?? ''} ${u.title ?? ''}`).join(' ');
  const text = `${a.approach ?? ''} ${a.questionPolicy ?? ''} ${a.modelPolicy ?? ''}`;
  const inRange = units.length >= 2 && units.length <= 4;
  const twoLevel = children > 0;
  const everyChildInRange = kidCounts.every((k) => k >= 2 && k <= 4);

  return {
    ok: true,
    units: units.length,
    children,
    inRange,
    twoLevel,
    // What the order asked THIS plan for, given its measured verdict.
    shapeAsOrdered: verdict === 'Epics'
      ? inRange && twoLevel && everyChildInRange
      : verdict === 'Stories'
      ? inRange && !twoLevel
      : inRange,
    epicWord: /epic/i.test(kinds),
    storyWord: /\bstor(y|ies)\b/i.test(`${kinds} ${text}`),
    reviewsEachUnit: a.reviewsEachUnit === true,
    commitsEachUnit: a.commitsEachUnit === true,
    batchesQuestions: /(at the end|final summary|batch|all at once|together|once with all|grouped)/i.test(a.questionPolicy ?? ''),
    namesFable: /fable/i.test(text),
    namesOpus: /opus/i.test(text),
    kinds: units.map((u) => u.kind ?? '').join(' | '),
  };
}

const arms = [
  { name: 'plain', orders: () => '' },
  { name: 'commands', orders: (f) => f.commands },
  { name: 'epic', orders: (f) => f.epicCommands },
];

const results = [];
const resultFile = join(outDir, `runs-${runLabel}.json`);

for (const planName of plans) {
  const fixture = JSON.parse(readFileSync(join(fixtures, `${planName}.json`), 'utf8'));
  const planText = readFileSync(join(repo, fixture.folder, `${planName}.md`), 'utf8');

  for (const arm of arms) {
    const list = arm.orders(fixture);
    const head = list.length === 0
      ? ''
      : `${fixture.preamble}\n\n${list.map((c, i) => `${i + 1}. ${c}`).join('\n\n')}\n\n`;
    const run = await ask(`${runLabel}-${planName}-${arm.name}`, `${head}${TASK}\n\n--- PLAN ---\n${planText}`);
    const row = {
      run: runLabel, model, plan: planName, arm: arm.name, verdict: fixture.verdict,
      lines: fixture.lines, ...run, score: score(run),
    };
    delete row.answer;
    row.answerText = run.answer;
    results.push(row);
    // Written after every single call: an hour of measurement must not depend on the last one.
    writeFileSync(resultFile, JSON.stringify(results, null, 1));
    const s = row.score;
    console.log(
      `${planName.padEnd(32)} ${arm.name.padEnd(8)} ${String(run.seconds).padStart(6)}s  ` +
      (s.ok
        ? `units=${String(s.units).padStart(2)} kids=${String(s.children).padStart(2)} ` +
          `${s.twoLevel ? '2lvl' : '    '} ${s.epicWord ? 'epic' : '    '} ${s.storyWord ? 'story' : '     '} ` +
          `${s.reviewsEachUnit ? 'rev' : '   '} ${s.commitsEachUnit ? 'commit' : '      '} ` +
          `${s.batchesQuestions ? 'batchQ' : '      '} ${s.namesFable ? 'fable' : '     '}`
        : `FAILED exit=${run.code} ${run.error}`),
    );
  }
}

console.log(`\nwrote ${results.length} runs to ${resultFile}`);

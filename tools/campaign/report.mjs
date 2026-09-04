/**
 * Turns the campaign's raw runs into the tables the report carries.
 *
 * Nothing here judges prose: every number is a count over the structural score, so the report can be
 * regenerated from `runs-*.json` alone and a reader can recount it.
 */
import { readFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';

const repo = process.argv[2];
const dir = join(repo, 'artifacts', 'campaign');
const files = ['A', 'B'].filter((r) => existsSync(join(dir, `runs-${r}.json`)));
const runs = files.flatMap((r) => JSON.parse(readFileSync(join(dir, `runs-${r}.json`), 'utf8')));

const arms = ['plain', 'commands', 'epic'];
const cols = [
  ['units 2-4', 'inRange'],
  ['shape as ordered', 'shapeAsOrdered'],
  ['nested', 'twoLevel'],
  ['calls a unit an epic', 'epicWord'],
  ['batches questions', 'batchesQuestions'],
  ['names Fable', 'namesFable'],
  ['reviews every unit', 'reviewsEachUnit'],
  ['commits every unit', 'commitsEachUnit'],
];

const pick = (run, arm) => runs.filter((r) => r.arm === arm && (run === '*' || r.run === run) && r.score.ok);
const med = (xs) => (xs.length ? [...xs].sort((a, b) => a - b)[Math.floor(xs.length / 2)] : 0);

function table(run, title) {
  console.log(`\n**${title}**\n`);
  console.log(`| arm | n | median units | median sub-units | ${cols.map((c) => c[0]).join(' | ')} |`);
  console.log(`|---|---|---|---|${cols.map(() => '---').join('|')}|`);
  for (const arm of arms) {
    const xs = pick(run, arm);
    const cells = cols.map(([, k]) => `${xs.filter((x) => x.score[k]).length}/${xs.length}`);
    console.log(`| \`${arm}\` | ${xs.length} | ${med(xs.map((x) => x.score.units))} | `
      + `${med(xs.map((x) => x.score.children))} | ${cells.join(' | ')} |`);
  }
}

table('*', 'Both runs, 11 plans each');
for (const r of files) {
  const model = runs.find((x) => x.run === r)?.model ?? '';
  table(r, `Run ${r} — ${model}`);
}

console.log('\n**Per plan — the shape each arm proposed (units/sub-units), run A then run B**\n');
console.log('| plan | verdict | plain | commands | epic | commands as ordered | epic re-split |');
console.log('|---|---|---|---|---|---|---|');
for (const plan of [...new Set(runs.map((r) => r.plan))]) {
  const cell = (arm) => files
    .map((run) => {
      const x = runs.find((r) => r.plan === plan && r.arm === arm && r.run === run);
      return x?.score?.ok ? `${x.score.units}/${x.score.children}` : '—';
    })
    .join(' · ');
  const flag = (arm, key) => files
    .map((run) => {
      const x = runs.find((r) => r.plan === plan && r.arm === arm && r.run === run);
      return x?.score?.ok ? (x.score[key] ? 'yes' : 'no') : '—';
    })
    .join(' · ');
  const verdict = runs.find((r) => r.plan === plan)?.verdict ?? '';
  console.log(`| \`${plan}\` | ${verdict} | ${cell('plain')} | ${cell('commands')} | ${cell('epic')} | `
    + `${flag('commands', 'shapeAsOrdered')} | ${flag('epic', 'epicWord')} |`);
}

const fails = runs.filter((r) => !r.score.ok);
const secs = runs.map((r) => r.seconds);
console.log(`\nCalls: ${runs.length}. Failed to answer: ${fails.length}`
  + `${fails.length ? ` (${fails.map((f) => `${f.plan}/${f.arm}`).join(', ')})` : ''}. `
  + `Median call ${med(secs)}s, slowest ${Math.max(...secs)}s.`);

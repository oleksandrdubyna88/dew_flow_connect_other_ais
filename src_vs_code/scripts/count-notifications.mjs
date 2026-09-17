// How many places this extension speaks to a person, counted rather than remembered.
//
// WHY A SCRIPT. The notifications plan promises that EVERY message is written down, and a promise
// about completeness may not rest on a number somebody typed. The plan's own first draft proved
// that: it published 109 call sites, a class table that summed to 113, and then "95 events" — which
// is neither 109 − 16 nor 113 − 16 but the sum of the table's first four rows. Three documents
// quoted the wrong number and every test was green, because no test knew what the number was.
//
// So the number is produced here, written to `notification-sites.json`, and checked by
// `src/test/notificationSites.test.ts`. A call site added tomorrow makes that test red in the
// commit that adds it, rather than making a plan quietly wrong for a year.
//
// WHAT IS COUNTED, AND WHAT IS NOT. Only what a scan can actually know: which API was used, and
// whether the call is modal. A message's CLASS — is this a refusal, a failure, an outcome — is a
// judgement, and until the funnel exists there is nowhere in the source that records it. Once
// `notify.ts` ships, every call passes a `class` literal and this script reads the split off the
// code instead of guessing at it. Counting a thing this cannot see would be the same defect in a
// new place.
//
//   node scripts/count-notifications.mjs            — print the table
//   node scripts/count-notifications.mjs --write    — regenerate notification-sites.json

import { readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const EXTENSION = resolve(HERE, '..');
const SOURCE = join(EXTENSION, 'src');
export const INVENTORY = join(EXTENSION, 'notification-sites.json');

/** The three ways this product tells somebody something. */
const APIS = /** @type {const} */ (['showInformationMessage', 'showWarningMessage', 'showErrorMessage']);

/**
 * Every extension a shipped module can have here.
 *
 * <p>`.ts` alone was a hole in the guard, and the code round found it: a `.tsx` file calling
 * `window.showWarningMessage` would have left BOTH numbers unchanged — the population and the
 * ratchet — so the completeness promise would have gone on being made while being false. A scan
 * that decides what counts by file extension has to know every extension the compiler accepts, and
 * `tsconfig.json` accepts these four. (codex, Architecture.)</p>
 */
export const SOURCE_EXTENSIONS = ['.ts', '.tsx', '.mts', '.cts'];

/** Whether this file is one the compiler would ship, and not a declaration file. */
function isShippedSource(entry) {
  return !entry.endsWith('.d.ts') && SOURCE_EXTENSIONS.some((end) => entry.endsWith(end));
}

/**
 * Whole-file text, not lines: at least one call here is split across a line break
 * (`extension.ts`, the stand-down warning), and a line-by-line scan misses it — which is one of the
 * four the first hand count was out by.
 */
export function everySourceFile(dir) {
  const found = [];
  for (const entry of readdirSync(dir)) {
    const path = join(dir, entry);
    if (statSync(path).isDirectory()) {
      // The tests are not product sites, and two of them assert ON these names. A scan that
      // reported the tests enforcing the discipline is a scan nobody keeps.
      if (entry !== 'test') {
        found.push(...everySourceFile(path));
      }
    } else if (isShippedSource(entry)) {
      found.push(path);
    }
  }

  return found.sort();
}

/** The funnel itself, which is allowed — indeed required — to call the API directly. */
const FUNNEL = 'notify.ts';

/**
 * The file with its comments taken out, so the scan counts CODE.
 *
 * <p>This is not tidiness, it is correctness, and it was found the way these things are found: the
 * fix for another finding added the sentence *"carried over from an `await
 * vscode.window.showErrorMessage`"* to a comment, and the population jumped by one. The scan had
 * always counted prose — every API name and every `notify(` written in a docstring — and this file
 * is unusually full of prose ABOUT those names, because that is what the whole feature is about.</p>
 *
 * <p>It runs both ways, which is why it matters more than a miscount: a comment can inflate
 * `direct` and turn the guard red over nothing, and it can inflate `routed` and the population,
 * which is the number the completeness promise is made over.</p>
 *
 * <p>A state machine rather than a regex, because the cheap version — strip from `//` to the end of
 * the line — eats the rest of any line holding a `https://` URL, and a real call sitting after one
 * would vanish from the count. The four states are all that TypeScript needs here: ordinary code, a
 * quoted string (single, double or template), a line comment, a block comment.</p>
 */
function withoutComments(text) {
  let out = '';
  let state = 'code';
  let quote = '';
  for (let i = 0; i < text.length; i += 1) {
    const here = text[i];
    const next = text[i + 1];
    if (state === 'code') {
      if (here === '/' && next === '/') {
        state = 'line';
        i += 1;
      } else if (here === '/' && next === '*') {
        state = 'block';
        i += 1;
      } else if (here === "'" || here === '"' || here === '`') {
        state = 'string';
        quote = here;
        out += here;
      } else {
        out += here;
      }
    } else if (state === 'string') {
      out += here;
      if (here === '\\') {
        out += next ?? '';
        i += 1;
      } else if (here === quote) {
        state = 'code';
      }
    } else if (state === 'line') {
      if (here === '\n') {
        state = 'code';
        out += here;
      }
    } else if (here === '*' && next === '/') {
      state = 'code';
      i += 1;
    } else if (here === '\n') {
      // Kept, so nothing downstream sees two statements joined into one line.
      out += here;
    }
  }

  return out;
}

/**
 * What ONE file contributes.
 *
 * <p>Split out of `count` because that function had grown past the size the shared coding-style
 * rule allows, and because the per-file judgements — is this the funnel, is this the pure half —
 * are the part somebody will come back to read. (codex, Conventions.)</p>
 */
function readOneFile(file) {
  const text = withoutComments(readFileSync(file, 'utf8'));
  const isFunnel = file.endsWith(FUNNEL);
  const byApi = Object.fromEntries(APIS.map((api) => [api, 0]));
  let inTheFunnel = 0;
  for (const api of APIS) {
    const hits = text.match(new RegExp(`\\.${api}\\b`, 'gu'))?.length ?? 0;
    if (isFunnel) {
      inTheFunnel += hits;
    } else {
      byApi[api] += hits;
    }
  }

  return {
    byApi,
    inTheFunnel,
    // The funnel's own `modal: true` is how it PASSES the flag on, not a question it asks. Counting
    // it moved the event total from 93 to 92 the moment the funnel landed, which is the shape of
    // error this whole script exists to stop.
    modal: isFunnel ? 0 : (text.match(/modal:\s*true/gu)?.length ?? 0),
    // Call sites that have been routed. The definitions themselves are not calls, so the funnel and
    // the pure half are left out of this count as well.
    //
    // EVERY DOOR HAS TO BE NAMED HERE. `notifyOnce` arrived in S4 and this pattern did not know it,
    // so routing a call site through it took the POPULATION from 111 down to 110 - the one number
    // that must never fall, falling because the work was going well. The test held, and the lesson
    // is that a door missing from this list reads as a message that stopped existing.
    // `notifyResolved` is deliberately NOT matched: it clears a counter and says nothing to anybody.
    routed: isFunnel || file.endsWith('notice.ts')
      ? 0
      : (text.match(/\bnotify(?:AndAsk|Then|Once)?\(/gu)?.length ?? 0),
  };
}

/**
 * What the source says, today.
 *
 * <p>The directory is a parameter so a test can point it at a fixture and assert what the scan
 * SEES, rather than only that a constant lists the right extensions. `testing.md` asks for that
 * companion beside every structural prohibition, and a list nothing reads would pass without
 * it.</p>
 */
export function count(dir = SOURCE) {
  const byApi = Object.fromEntries(APIS.map((api) => [api, 0]));
  const perFile = {};
  let modal = 0;
  let inTheFunnel = 0;
  let routed = 0;

  for (const file of everySourceFile(dir)) {
    const here = readOneFile(file);
    let direct = 0;
    for (const api of APIS) {
      byApi[api] += here.byApi[api];
      direct += here.byApi[api];
    }
    modal += here.modal;
    inTheFunnel += here.inTheFunnel;
    routed += here.routed;
    if (direct > 0) {
      perFile[relative(EXTENSION, file).replaceAll('\\', '/')] = direct;
    }
  }

  // `direct` is what is left to route; `sites` is the POPULATION, which is what the plan and the
  // Definition of Done promise completeness over and which must not shrink as work proceeds.
  // Counting only the direct calls as `sites` made `events` mix two populations the moment the
  // first modal was routed: a routed confirmation keeps its `modal: true`, so it left the numerator
  // and stayed in the subtrahend. Caught by reading the output, not by a test — which is why the
  // consistency assertions below exist now.
  const direct = APIS.reduce((total, api) => total + byApi[api], 0);

  return {
    $comment: [
      'Generated by scripts/count-notifications.mjs. Do not edit by hand.',
      'The plan, the todo/README row and the Definition of Done quote THIS file, so that a call',
      'site added tomorrow turns a test red instead of making three documents quietly wrong.',
    ],
    /** Every place this extension speaks to a person, routed or not. This number does not fall. */
    sites: direct + routed,
    /** Still calling the API directly. THIS is the ratchet, and it only ever falls. */
    direct,
    modal,
    events: direct + routed - modal,
    /**
     * Calls to the API from INSIDE the funnel. Must never be zero: a scan that matches nothing
     * passes for ever, and this is its positive companion — the thing `testing.md` asks a
     * structural test to have beside its prohibition.
     */
    inTheFunnel,
    /** Call sites already routed through `notify`/`notifyAndAsk`. This number only goes up. */
    routed,
    byApi,
    perFile,
  };
}

/** Stable text, so a regeneration that changed nothing is an empty diff. */
export function asText(counted) {
  return `${JSON.stringify(counted, undefined, 2)}\n`;
}

function main() {
  const counted = count();
  if (process.argv.includes('--write')) {
    writeFileSync(INVENTORY, asText(counted), 'utf8');
    console.log(`count-notifications: wrote ${relative(EXTENSION, INVENTORY)}`);
  }
  console.log(
    `${counted.sites} call sites, ${counted.routed} routed and ${counted.direct} still direct `
    + `(${counted.byApi.showWarningMessage} warning, ${counted.byApi.showInformationMessage} information, `
    + `${counted.byApi.showErrorMessage} error); ${counted.modal} modal, so ${counted.events} events.`,
  );
}

// Run as a script, importable as a module — the test imports `count()` rather than parsing what
// this prints, so there is one implementation and no second place for the rule to live.
if (process.argv[1]?.endsWith('count-notifications.mjs') === true) {
  main();
}

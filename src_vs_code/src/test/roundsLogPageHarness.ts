import { DbTotals } from '../roundsDb';
import { LogRow, roundsLogHtml } from '../roundsLog';
import { PageClock } from './panelPageHarness';

/**
 * The rounds log page, RUN: its own script over a stub DOM built from the markup it really rendered.
 *
 * <p>Extracted from `roundsLogPaging.test.ts` (2026-10-03) when the busy mark needed the same runner with a clock it
 * controls (todo/PLAN_busy_marks_on_every_webview.md, E1) — a seventh private copy of a rounds-log runner was the
 * alternative, and six already exist. Unchanged except for the optional clock and the busy bar.</p>
 */

/** The busy bar's id (busyMark.ts `BUSY_BAR`). */
const BUSY_BAR_ID = 'busy-bar';

/** The busy bar as the page drew it — hidden or not, per its markup — with attributes that are really kept. */
function busyBar(stub: Stub, html: string): Stub {
  const tag = /<div\b[^>]*\bid="busy-bar"[^>]*>/u.exec(html)?.[0] ?? '';
  let attributes: Readonly<Record<string, string>> = {};

  return {
    ...stub,
    hidden: /\shidden(?=[\s>]|$)/u.test(tag),
    getAttribute: (name) => attributes[name] ?? null,
    setAttribute: (name = '', value = '') => { attributes = { ...attributes, [name]: value }; },
    removeAttribute: (name = '') => { attributes = Object.fromEntries(Object.entries(attributes).filter(([key]) => key !== name)); },
  };
}

export const TOTALS: DbTotals = {
  rounds: 250, findings: 3484, accepted: 900, rejected: 2000, gating: 700,
  tokensIn: 1, tokensOut: 2, costUsd: 3,
};

/** One element of the stub DOM: enough of one that the page script cannot tell the difference. */
export interface Stub {
  innerHTML: string;
  textContent: string;
  hidden: boolean;
  disabled: boolean;
  value: string;
  className: string;
  checked: boolean;
  indeterminate: boolean;
  readonly heard: Record<string, (event?: unknown) => void>;
  addEventListener(kind: string, listener: (event?: unknown) => void): void;
  /** Answered for controls served by SELECTOR, which have no id to be found by. */
  getAttribute(asked: string): string | null;
  setAttribute(name?: string, value?: string): void;
  removeAttribute(name?: string): void;
  querySelectorAll(selector?: string): Stub[];
}

export interface Page {
  readonly at: (id: string) => Stub;
  /** Which selectors the page asked for — how "found by marker" is observed rather than read. */
  readonly asked: readonly string[];
  readonly click: (target: unknown) => void;
  readonly deliver: (message: unknown) => void;
  readonly posted: unknown[];
}

/** Runs the page script of a freshly built page and hands back the levers a person has. */
export function open(rows: readonly LogRow[], totals: DbTotals = TOTALS, extra = '', clock?: PageClock): Page {
  // `extra` splices a section the page never rendered into the markup the script runs over. It is
  // the only way to prove the handler DRIVES what the marker finds rather than merely asking for
  // it: every real section is also reachable by a literal id, so a build that kept the three
  // getElementById lines and added a dead query would satisfy every other assertion here.
  const built = roundsLogHtml(rows, [], 'n0nce', 'usage', 'spots', totals);
  const html = extra === '' ? built : built.replace('<script', extra + '<script');
  const tag = html.slice(html.indexOf('<script'), html.lastIndexOf('</script>'));
  const body = tag.slice(tag.indexOf('>') + 1);

  const elements = new Map<string, Stub>();
  const at = (id: string): Stub => {
    const known = elements.get(id);
    if (known !== undefined) {
      return known;
    }
    const heard: Record<string, (event?: unknown) => void> = {};
    const fresh: Stub = {
      innerHTML: '', textContent: '', hidden: false, disabled: false, value: '', className: '',
      checked: false, indeterminate: false, heard,
      addEventListener(kind, listener) { heard[kind] = listener; },
      getAttribute: (): string | null => null,
      setAttribute: () => undefined,
      removeAttribute: () => undefined,
      querySelectorAll: () => [],
    };
    elements.set(id, id === BUSY_BAR_ID ? busyBar(fresh, html) : fresh);

    return elements.get(id) as Stub;
  };

  // CONTROLS THAT HAVE NO ID, served from the markup the page really rendered — and the selector is
  // served BY NAME, with anything unknown a throw rather than an empty list. Answering [] for every
  // selector is how a test passes against a page that found nothing: the loop runs zero times and
  // "the filter did not travel" is true because no filter was ever wired. That is precisely what had
  // happened here — `[data-filter]` had never been served, so the facet handlers had never run in
  // any test on this page.
  const byAttribute = (attribute: string, tag = '[a-z]+'): Stub[] => {
    // A SPACE before the attribute, and the escape is DOUBLED because this is a template literal:
    // a single `\s` in one is just the letter s, and a single `\b` is the BACKSPACE character. Both
    // spellings were written here first, and both made the pattern match nothing at all — which
    // looks exactly like a page that renders no filters.
    const pattern = new RegExp(`<${tag}[^>]*\\s${attribute}="([^"]*)"[^>]*>`, 'g');

    return [...html.matchAll(pattern)].map((found) => {
      const key = `${attribute}:${found[1]}`;
      const stub = at(key);
      stub.getAttribute = (asked: string) => (asked === attribute ? found[1] ?? null : null);

      return stub;
    });
  };
  const askedFor: string[] = [];
  const serve = (selector: string): Stub[] => {
    askedFor.push(selector);
    if (selector === '[data-filter]') { return byAttribute('data-filter'); }
    if (selector === '[data-tab]') { return byAttribute('data-tab'); }
    // Keyed by the element's OWN id, not by the attribute value, so that the section the table
    // lives in is the SAME stub `getElementById('tab-rounds')` answers. It answers to two tabs and
    // is toggled by a line of its own, so whether the derived loop runs before or after that line
    // decides whether the table survives the conversations tab — and two separate stubs would make
    // that ordering unobservable, which is the shape of test that passes against a broken page.
    if (selector === 'section[data-section]') {
      return [...html.matchAll(/<section id="(tab-[^"]+)"[^>]*\sdata-section="([^"]*)"[^>]*>/gu)]
        .map((found) => {
          const stub = at(found[1] as string);
          stub.getAttribute = (asked: string) => (asked === 'data-section' ? found[2] ?? null : null);

          return stub;
        });
    }
    if (selector === 'th[data-sort]') { return byAttribute('data-sort', 'th'); }
    // A tab's PERIOD row (2026-09-25): the group, with its own buttons read out of its own markup —
    // two rows carry the same data-period values, so they are keyed by the group they sit in.
    const periods = /^\[data-periods="([\w-]+)"\]$/.exec(selector);
    if (periods !== null) {
      const tab = periods[1] as string;
      const start = html.indexOf(`data-periods="${tab}"`);
      if (start < 0) { return []; }
      const group = at(`data-periods:${tab}`);
      // Every button of the group, with the attributes it really carries — a spots button says its
      // period in data-id, a page-filtered one in data-period.
      const buttons = [...html.slice(start, html.indexOf('</div>', start)).matchAll(/<button[^>]*>/g)]
        .map((found) => {
          const attributes = Object.fromEntries([...found[0].matchAll(/\s([\w-]+)="([^"]*)"/g)].map((a) => [a[1], a[2]]));
          const button = at(`period:${tab}:${attributes['data-period'] ?? attributes['data-id']}`);
          button.getAttribute = (asked: string) => attributes[asked] ?? null;

          return button;
        });
      group.querySelectorAll = (asked?: string) => (asked === 'button' ? buttons : []);

      return [group];
    }
    // The text controls, found in the markup like every other control here; pressing them is the page
    // census's job (everyPageHasBothTextControls), so these stubs are only ever bound.
    const control = /^button\[data-(zoom|tone)\]$/.exec(selector);
    if (control !== null) { return byAttribute(`data-${control[1]}`, 'button'); }
    const one = /^\[data-filter="([\w-]+)"\]$/.exec(selector);
    if (one !== null) { return byAttribute('data-filter').filter((s) => s.getAttribute('data-filter') === one[1]); }

    throw new Error(
      `the page asked for ${JSON.stringify(selector)}, which this harness does not model — answering [] `
      + 'would let a test pass while the page found nothing. Teach the harness that selector.',
    );
  };

  const clicks: ((event: unknown) => void)[] = [];
  const messages: ((event: unknown) => void)[] = [];
  const posted: unknown[] = [];
  const document_ = {
    getElementById: at,
    querySelectorAll: (selector: string) => serve(selector),
    querySelector: (selector: string) => serve(selector)[0] ?? null,
    addEventListener(kind: string, listener: (event: unknown) => void) {
      if (kind === 'click') {
        clicks.push(listener);
      }
    },
  };
  const window_ = {
    addEventListener(kind: string, listener: (event: unknown) => void) {
      if (kind === 'message') {
        messages.push(listener);
      }
    },
  };

   
  // The page's timers run on the test's clock when one is given (the busy mark's delay, todo/PLAN_busy_marks_on_every_webview.md),
  // and on the real ones otherwise, which is what every test written before it expects.
  new Function('document', 'window', 'acquireVsCodeApi', 'setTimeout', 'clearTimeout', body)(
    document_, window_, () => ({ postMessage: (m: unknown) => posted.push(m) }),
    clock?.setTimeout ?? setTimeout, clock?.clearTimeout ?? clearTimeout);

  // The page opens on TODAY, and these rows are dated whenever the fixture says. Clearing the range
  // is what a person does when they want the whole log, and it makes these tests independent of the
  // clock they run on.
  at('alldates').heard['click']?.();

  return {
    at,
    asked: askedFor,
    posted,
    click: (target) => clicks.forEach((one) => one({ target })),
    deliver: (message) => messages.forEach((one) => one({ data: message })),
  };
}


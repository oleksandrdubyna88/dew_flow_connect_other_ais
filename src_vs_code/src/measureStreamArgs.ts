/**
 * What `scripts/measure-stream.mjs` was asked to measure.
 *
 * <p>It lives here rather than in the script for one reason: it decides how many real, signed-in
 * vendor turns get spent, and the script itself cannot be unit-tested because importing it runs a
 * measurement. The first version had a defect worth exactly that separation — with no `--lines` the
 * index of that flag is `-1`, and the filter that drops its VALUE dropped index 0 instead, which is
 * the vendor. `measure:stream -- agy` therefore parsed as `all` and spent turns on all three
 * accounts rather than the one asked for. (CodeRabbit, on the pull request.)</p>
 */

/** A parsed command line: what to measure, or why it cannot be read. */
export type MeasureOptions =
  | { readonly kind: 'run'; readonly vendor: string; readonly lines: number }
  | { readonly kind: 'help' }
  | { readonly kind: 'error'; readonly message: string };

/** How long an answer to ask for. Bounded because it is a prompt sent to a paid account. */
const MIN_LINES = 1;
const MAX_LINES = 2000;
const DEFAULT_LINES = 40;

/**
 * Read the arguments, independently of their order.
 *
 * @param args the arguments AFTER the node executable and the script path
 * @param known the vendor names this harness can measure, so an unknown one is refused by name
 */
export function measureOptionsFrom(args: readonly string[], known: readonly string[]): MeasureOptions {
  if (args.includes('--help') || args.includes('-h')) {
    return { kind: 'help' };
  }

  const at = args.indexOf('--lines');
  let lines = DEFAULT_LINES;
  if (at >= 0) {
    const given = args[at + 1] ?? '';
    const asNumber = Number(given);
    if (!/^[0-9]{1,4}$/.test(given) || asNumber < MIN_LINES || asNumber > MAX_LINES) {
      return {
        kind: 'error',
        message: `--lines needs a whole number from ${MIN_LINES} to ${MAX_LINES}, not "${given}"`,
      };
    }
    lines = asNumber;
  }

  // Only when `--lines` is actually present does `at + 1` name its value. Without it, `at` is `-1`
  // and `at + 1` is 0 — the vendor.
  const valueAt = at >= 0 ? at + 1 : -1;
  const positional = args.filter((arg, index) => !arg.startsWith('-') && index !== valueAt);
  const vendor = positional[0] ?? 'all';
  if (vendor !== 'all' && !known.includes(vendor)) {
    return { kind: 'error', message: `unknown vendor: ${vendor}` };
  }

  return { kind: 'run', vendor, lines };
}

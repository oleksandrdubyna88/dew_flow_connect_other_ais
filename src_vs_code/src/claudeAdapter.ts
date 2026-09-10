import { ReportedUsage, spent } from './chatUsage';
import { ChatAdapter, NOTHING, parsed, text, count, inside, money } from './chatAdapter';

/**
 * `claude` — a persistent pipe too, in Anthropic's own stream-json.
 *
 * <p>Measured on 2026-09-08: two turns in 4.9 seconds total, both carrying one `session_id`, with
 * the second answering against the first. That makes it the FASTEST of the three by a wide margin —
 * `agy` takes longer to answer one turn than `claude` takes for two — which is worth knowing when
 * somebody wonders why a chat feels slow.</p>
 *
 * <p><b>`--verbose` is not decoration.</b> Without it the CLI refuses outright:
 * `--output-format=stream-json requires --verbose`. It is in the argv for that reason and a test
 * pins it, because a flag that looks removable is a flag somebody removes.</p>
 *
 * <p><b>`init` arrives once per TURN here, not once per process</b>, which would be a trap if the
 * session re-armed its startup budget on it. It does not: the budget is armed only inside
 * `ensureStarted`, which returns early once ready is latched, so a second `init` sets a flag that is
 * already true and wakes a waiter that no longer exists. Measured in phase 0 of the plan, in the
 * code rather than in a run.</p>
 */
export const CLAUDE_ARGS: readonly string[] = [
  '-p',
  '--verbose',
  '--input-format',
  'stream-json',
  '--output-format',
  'stream-json',
];

export const claudeAdapter: ChatAdapter = {
  shape: 'persistent',
  announces: false,
  argv: () => CLAUDE_ARGS,
  // The block shape, not a bare string: the content of a user message is a list of typed blocks,
  // and a string where a list is expected is refused by the CLI rather than misread.
  encode: (turn) => JSON.stringify({
    type: 'user',
    message: { role: 'user', content: [{ type: 'text', text: turn }] },
  }),
  classify: (line) => {
    const event = parsed(line);
    if (event === undefined) {
      return NOTHING;
    }
    if (event['type'] === 'system' && event['subtype'] === 'init') {
      return { kind: 'ready' };
    }
    if (event['type'] !== 'result') {
      return NOTHING;
    }
    if (event['subtype'] === 'success') {
      return { kind: 'answer', text: text(event['result']).trim(), ...spent(usageOf(event)) };
    }

    // Every other `result` subtype is a way of not answering — an error, a hit limit, a refusal.
    // The subtype IS the reason when the payload carries no sentence of its own.
    const said = text(event['result']);

    return {
      kind: 'failure',
      failure: said.length > 0 ? said : `the model answered with ${text(event['subtype']) || 'no subtype'}`,
    };
  },
};

/**
 * What a `result` event said the turn cost.
 *
 * <p>Measured against the real CLI rather than read out of a manual. A `result.success` carries a
 * `usage` object and, at the TOP level, `total_cost_usd` — so `claude` is the one vendor of the
 * three that bills a real number, and a chat turn on it is a bill rather than an estimate.</p>
 *
 * <p><b>The input count is the sum of three fields, and that is the plan's "not comparable" made
 * concrete.</b> A measured turn reported `input_tokens: 2` beside `cache_read_input_tokens: 27930`
 * and `cache_creation_input_tokens: 9294`. Recording only `input_tokens` would say a turn that cost
 * eleven cents used two tokens. What is billed is everything the model was handed, so that is what
 * is counted — and it is why this vendor's numbers cannot be compared with `agy`'s, which omits
 * cache entirely.</p>
 */
function usageOf(event: Record<string, unknown>): ReportedUsage | undefined {
  const usage = inside(event, 'usage');
  if (usage === undefined) {
    return undefined;
  }

  return {
    tokensIn:
      count(usage['input_tokens'])
      + count(usage['cache_creation_input_tokens'])
      + count(usage['cache_read_input_tokens']),
    tokensOut: count(usage['output_tokens']),
    costUsd: money(event['total_cost_usd']),
  };
}

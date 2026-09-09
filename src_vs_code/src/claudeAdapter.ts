import { ChatAdapter, NOTHING, parsed, text } from './chatAdapter';

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
      return { kind: 'answer', text: text(event['result']).trim() };
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

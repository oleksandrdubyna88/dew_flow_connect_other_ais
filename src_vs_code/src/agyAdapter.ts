import { ReportedUsage, spent } from './chatUsage';
import { ChatAdapter, NOTHING, parsed, text, count, inside } from './chatAdapter';

/**
 * `agy` — a persistent pipe, and the first protocol this feature learned.
 *
 * <p>The schema was MEASURED rather than read out of a manual: the binary's own error strings gave
 * it up after it refused `type:` with `stream input message is missing the "event" field`. Three
 * turns down one pipe were answered by one process with the context preserved — turn 3 resolved
 * "now simpler" against turn 2 without the passage being repeated.</p>
 *
 * <p>The four flags each answer a measured failure: stdin because `-p` does not read it (the model
 * answered "you did not attach the fragment"), NDJSON out because that is what makes a turn
 * distinguishable from a log line, `--mode plan` because explaining a paragraph should write
 * nothing, and `--disable-slash-commands` because the passage is another AI's text and can begin
 * with a slash.</p>
 */
export const AGY_ARGS: readonly string[] = [
  '--mode',
  'plan',
  '--disable-slash-commands',
  '--input-format',
  'stream-json',
  '--output-format',
  'stream-json',
];

export const agyAdapter: ChatAdapter = {
  shape: 'persistent',
  // Per-turn: each `result` carries the tokens of the turn it ends, not the pipe's running total.
  cumulative: false,
  announces: true,
  argv: () => AGY_ARGS,
  encode: (turn) => JSON.stringify({ event: 'user', message: { role: 'user', content: turn } }),
  classify: (line) => {
    const event = parsed(line);
    if (event === undefined) {
      return NOTHING;
    }
    if (event['event'] === 'init') {
      return { kind: 'ready' };
    }
    if (event['event'] !== 'result') {
      return NOTHING;
    }

    const result = (event['result'] ?? {}) as Record<string, unknown>;
    const status = text(result['status']);
    if (status === 'SUCCESS') {
      return { kind: 'answer', text: text(result['response']).trim(), ...spent(usageOf(result)) };
    }

    // An ERROR result is an error, not an empty answer: the CLI reports a refused input this way —
    // it is how the schema was discovered — and a page showing nothing would look like a model with
    // nothing to say.
    const failure = text(result['error']);

    return { kind: 'failure', failure: failure.length > 0 ? failure : `the model answered with ${status || 'no status'}` };
  },
};

/**
 * What a `result` event said the turn cost.
 *
 * <p>Measured: `result.usage` carries `input_tokens`, `output_tokens`, `thinking_tokens`,
 * `cache_read_tokens` and `total_tokens`, and NO money at all — so every turn on this vendor is
 * an estimate, priced from a rate somebody typed or from a published list, and carries the tilde.</p>
 *
 * <p>Cache reads are NOT added to the input count here, and that is the asymmetry with `claude`
 * rather than an oversight: this vendor reports `total_tokens` as `input + output` with the cache
 * read counted separately and outside it, so folding it in would invent a number the vendor did not
 * bill. It is exactly why the plan says these two vendors' figures are not comparable.</p>
 */
function usageOf(result: Record<string, unknown>): ReportedUsage | undefined {
  const usage = inside(result, 'usage');
  if (usage === undefined) {
    return undefined;
  }

  return {
    tokensIn: count(usage['input_tokens']),
    tokensOut: count(usage['output_tokens']),
    costUsd: null,
  };
}

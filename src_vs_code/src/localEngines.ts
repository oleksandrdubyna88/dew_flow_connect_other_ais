import { runningUnderWsl, windowsSideEngine } from './wslNetwork';

/**
 * What model engine is running on this machine, and which models it can review with.
 *
 * <p>Written for this repository rather than lifted from anywhere: `dew_flow_rag_qln` solved the
 * same discovery problem and what is taken from it is what it LEARNED, not its code. Three lessons,
 * each of which cost that project something:</p>
 *
 * <ul>
 *   <li><b>The probe URL and the OpenAI base are different URLs.</b> Ollama serves its own API at the
 *       root and its OpenAI-compatible surface under `/v1`. A configuration holding the probe URL
 *       fails at its first completion with a 404 that reads like a model problem.</li>
 *   <li><b>Ask what is INSTALLED, not what is loaded.</b> Ollama's `api/ps` lists what is resident in
 *       VRAM and is routinely empty — nothing loads until something asks — so a picker built on it
 *       shows an empty list on a machine full of models. `api/tags` is the right question.</li>
 *   <li><b>Unreachable is an ANSWER.</b> "No engine answered on 11434" is useful; an empty dropdown
 *       with no reason is indistinguishable from "you have no models".</li>
 * </ul>
 *
 * <p>Everything here is pure except {@link probeEngine}, so the shapes are tests rather than
 * something checked by having Ollama running.</p>
 */

/** One model an engine will answer for. `id` is what a request must carry. */
export interface LocalModel {
  readonly id: string;
  /** `14.8B · Q6_K · 12.1 GB` when the engine says, empty when it does not. */
  readonly detail: string;
}

export type LocalEngineKind = 'ollama' | 'vllm' | 'custom' | 'none';

/**
 * What one endpoint offers, read live.
 *
 * <p>Two URLs, deliberately both: `probeUrl` is where it was asked and is diagnostic, `apiBaseUrl`
 * is what a review request must actually POST to.</p>
 */
export interface LocalEngine {
  readonly kind: LocalEngineKind;
  readonly probeUrl: string;
  readonly apiBaseUrl: string;
  readonly reachable: boolean;
  /** Its version when it answered, the reason when it did not. */
  readonly status: string;
  readonly models: readonly LocalModel[];
  /**
   * What answers on the WINDOWS side of this machine, when nothing answered here.
   *
   * <p>Only ever set in WSL, and only after every candidate refused. It is a DIAGNOSIS, never an
   * endpoint: no review is sent through it, because a panel-side discovery cannot change the
   * address the server dials — that is the whole reason the network-probing version of this was
   * refused by the gate. What it changes is the sentence, from "you have no engine" to "your engine
   * is one hop away, and here is the hop".</p>
   */
  readonly elsewhere?: string;
  /**
   * Whether the probe ran inside a WSL distro.
   *
   * <p>Carried on the engine because the advice depends on it and "the extension host is Linux" is
   * not the same question: a native Linux box told to edit `%USERPROFILE%\.wslconfig` and restart a
   * subsystem it does not have has been handed instructions for a machine it is not. The note used
   * `platform === 'linux'` and did exactly that — found by Gemini 3.7 Flash in the code round, in
   * the half of the change whose server-side twin had already been fixed for the same reason.</p>
   */
  readonly wsl?: boolean;
}

/** Ollama's own default, and the only port it is ever on unless somebody moved it. */
export const OLLAMA_PROBE = 'http://127.0.0.1:11434';

/** vLLM's default. It has no registered port; 8000 is what `vllm serve` binds. */
export const VLLM_PROBE = 'http://127.0.0.1:8000';

/**
 * Where to look, in order.
 *
 * <p>Two, not five. A probe of every port something might conceivably serve on is a port scan of
 * the developer's own machine on every repaint, and the two engines that matter publish their
 * defaults. Anything else is typed in.</p>
 */
export const PROBE_CANDIDATES: readonly string[] = [OLLAMA_PROBE, VLLM_PROBE];

/**
 * The OpenAI-compatible base for a probe URL — the URL a completion is POSTed to.
 *
 * <p>Idempotent: a base somebody typed with `/v1` already on it is left alone, because the field
 * they typed it into asks for exactly that and correcting it twice would produce `/v1/v1`.</p>
 */
export function openAiBaseOf(probeUrl: string): string {
  const trimmed = probeUrl.replace(/\/+$/, '');

  return trimmed.endsWith('/v1') ? trimmed : `${trimmed}/v1`;
}

/**
 * The portable list: `GET /v1/models`, which Ollama and vLLM both answer.
 *
 * <p>This is the source of TRUTH about what may be asked for. It says nothing about size or
 * quantisation, which is what {@link parseOllamaTags} is for.</p>
 */
export function parseOpenAiModels(body: unknown): LocalModel[] {
  const rows = (body as { data?: unknown })?.data;

  return Array.isArray(rows)
    ? rows
        .map((row) => (row as { id?: unknown })?.id)
        .filter((id): id is string => typeof id === 'string' && id.length > 0)
        .map((id) => ({ id, detail: '' }))
    : [];
}

/**
 * Ollama's own list: `GET /api/tags`, which knows the things a person chooses BY.
 *
 * <p>Ollama-only, and that is why it is separate rather than the primary source: a vLLM answers
 * `/v1/models` and not this, and a picker that needed this would show nothing for it.</p>
 */
export function parseOllamaTags(body: unknown): LocalModel[] {
  const rows = (body as { models?: unknown })?.models;
  if (!Array.isArray(rows)) {
    return [];
  }

  return rows
    .map((row) => row as { name?: unknown; size?: unknown; details?: { parameter_size?: unknown; quantization_level?: unknown } })
    .filter((row) => typeof row.name === 'string' && (row.name as string).length > 0)
    .map((row) => ({
      id: row.name as string,
      detail: [
        text(row.details?.parameter_size),
        text(row.details?.quantization_level),
        gigabytes(row.size),
      ].filter((part) => part.length > 0).join(' · '),
    }));
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

function gigabytes(value: unknown): string {
  return typeof value === 'number' && value > 0 ? `${(value / 1e9).toFixed(1)} GB` : '';
}

/**
 * Both lists as one: every model the endpoint will answer for, described as well as it can be.
 *
 * <p>The portable list decides WHICH models exist — a model only the native list mentions is a model
 * a completion request would be refused for. The native list only adds description. Nothing is
 * dropped for want of a description.</p>
 */
export function mergeModels(portable: readonly LocalModel[], native: readonly LocalModel[]): LocalModel[] {
  const detailById = new Map(native.map((m) => [m.id, m.detail]));

  return portable.map((m) => ({ id: m.id, detail: detailById.get(m.id) ?? m.detail }));
}

/**
 * Whether an endpoint is on THIS machine.
 *
 * <p>Parsed, never matched as a string: `http://127.0.0.1.evil.test/v1` starts with a loopback
 * address and is a hostname somebody else controls. A URL that does not parse is not loopback —
 * "I could not tell" must never resolve to "it is safe".</p>
 *
 * <p>The whole 127.0.0.0/8 block, not just 127.0.0.1: `127.5.5.5` is equally this machine, and a
 * check that missed it would nag about an endpoint that never leaves the host.</p>
 */
export function isLoopback(url: string): boolean {
  let host: string;
  try {
    host = new URL(url).hostname.toLowerCase();
  } catch {
    return false;
  }

  return host === 'localhost'
    || host === '::1'
    || host === '[::1]'
    || /^127\.\d{1,3}\.\d{1,3}\.\d{1,3}$/.test(host);
}

/**
 * What to say about an endpoint that is not on this machine, or nothing when it is.
 *
 * <p>It names the HOST and what would be sent. "An external endpoint is configured" is a sentence
 * somebody skims; "your diffs and file contents will be sent to api.example.com" is one they read.
 * Empty means "whatever the probe found", which is loopback by construction.</p>
 */
export function remoteWarning(endpoint: string): string {
  if (endpoint.length === 0 || isLoopback(endpoint)) {
    return '';
  }
  let host: string;
  try {
    host = new URL(endpoint).host;
  } catch {
    host = endpoint;
  }

  return `This endpoint is not on this machine. Every review sends the prompt — your plan, your `
    + `diffs and the file contents around them — to ${host}. Use it only if you control that server.`;
}

/** Nothing answered anywhere, as an engine value rather than an absence. */
export function noEngine(status: string): LocalEngine {
  return { kind: 'none', probeUrl: OLLAMA_PROBE, apiBaseUrl: '', reachable: false, status, models: [] };
}

/**
 * The line the panel shows under a local reviewer's model picker.
 *
 * <p>When nothing answered it says where it looked and what to do instead — and on Linux it names
 * the WSL case first, because that is the likely one: Windows Ollama binds `127.0.0.1` only, so
 * from a VS Code attached to WSL the gateway times out and the local loopback refuses. An empty
 * list there does not mean "you have no models".</p>
 */
export function engineNote(engine: LocalEngine): string {
  if (engine.reachable) {
    return `${engine.kind} ${engine.status} · ${engine.models.length} models on this machine`;
  }
  const where = `No local engine answered on ${portsOf(PROBE_CANDIDATES)} (${engine.status}).`;
  const seen = engine.elsewhere ?? '';
  if (seen.length > 0) {
    // The state the whole WSL change exists for. Measured 2026-09-03: fifteen models one hop away,
    // ten rounds refused in zero seconds, and a panel saying what it says to a machine with no
    // engine at all.
    return `${where} One IS answering on the Windows side of this machine (${seen}) — a WSL distro's`
      + ' own 127.0.0.1 is not the Windows host\'s. ⇄ switches WSL to mirrored networking, which makes'
      + ' this very address the right one; or start the engine with OLLAMA_HOST=0.0.0.0 and paste the'
      + ' Windows host address below.';
  }
  const wsl =
    (engine.wsl ?? false)
      ? ' If your engine runs on the Windows side, TWO things are in the way and fixing one is not'
        + ' enough: it is bound to 127.0.0.1, so start it with OLLAMA_HOST=0.0.0.0 — and this side\'s'
        + ' 127.0.0.1 is WSL\'s own loopback, not the Windows host, so the endpoint below must point'
        + ' at the Windows host address (the default gateway in `ip route`). Mirrored networking'
        + ' (`[wsl2] networkingMode=mirrored`) removes both at once.'
      : '';

  return `${where}${wsl} Start one, or paste an endpoint below.`;
}

/** `127.0.0.1:11434` out of a probe URL — enough to tell two candidates apart, short enough to read. */
function hostOf(candidate: string): string {
  return candidate.replace(/^[a-z]+:\/\//, '').replace(/\/.*$/, '');
}

function portsOf(candidates: readonly string[]): string {
  return candidates
    .map((c) => /:(\d+)/.exec(c)?.[1] ?? c)
    .join(' and ');
}

/**
 * Ask one endpoint what it is and what it has.
 *
 * <p>Never throws: an endpoint that is not there is the ordinary state of a machine, and the reason
 * is the answer. The native list is asked for SECOND and only to enrich — a vLLM refuses it, and
 * that refusal must not lose the models `/v1` already reported.</p>
 */
export async function probeEngine(probeUrl: string, timeoutMs = 4000): Promise<LocalEngine> {
  const base = openAiBaseOf(probeUrl);
  const root = base.replace(/\/v1$/, '');
  const portable = await getJson(`${base}/models`, timeoutMs);
  if (portable.body === undefined) {
    // The REASON, not just "no answer": refused means nothing is listening, timed out means
    // something is and will not answer — a firewall, a wedged engine, or a host that accepts the
    // connection and never replies. Those want different actions from a person.
    return { ...noEngine(portable.why), probeUrl, apiBaseUrl: '' };
  }

  const models = parseOpenAiModels(portable.body);
  const tags = (await getJson(`${root}/api/tags`, timeoutMs)).body;
  const version = (await getJson(`${root}/api/version`, timeoutMs)).body;
  const isOllama = tags !== undefined;

  return {
    kind: isOllama ? 'ollama' : 'vllm',
    probeUrl: root,
    apiBaseUrl: base,
    reachable: true,
    status: text((version as { version?: unknown })?.version) || 'reachable',
    models: isOllama ? mergeModels(models, parseOllamaTags(tags)) : models,
  };
}

/**
 * The first candidate that answers, or a `none` engine carrying why not — and, in WSL, what is
 * answering on the other side of the machine.
 *
 * <p>The Windows question is asked LAST and only when everything here refused, because it costs a
 * process launch and answers nothing a working engine leaves open. It is injected so the tests can
 * drive both orders without a machine.</p>
 */
export async function discoverEngine(
  candidates: readonly string[] = PROBE_CANDIDATES,
  timeoutMs = 4000,
  elsewhere: () => Promise<string> = windowsSideEngine,
  underWsl: () => Promise<boolean> = runningUnderWsl,
): Promise<LocalEngine> {
  // Each candidate's reason is KEPT. It used to be computed, carried through `probeEngine`, and
  // then thrown away by a hard-coded 'connection refused' on this line — so a firewall swallowing
  // the connection, an engine wedged mid-answer and a port with nothing on it all reached a person
  // as the same sentence, and three different actions had one prompt. Found by GPT-5.6-Luna,
  // 2026-09-02.
  const reasons: string[] = [];
  for (const candidate of candidates) {
    const engine = await probeEngine(candidate, timeoutMs);
    if (engine.reachable) {
      return engine;
    }
    reasons.push(`${hostOf(candidate)}: ${engine.status}`);
  }
  // The kind of machine decides both the question and the advice: only a WSL distro has a Windows
  // side to ask about, and only a WSL distro can act on the answer.
  const wsl = await underWsl();
  const seen = wsl ? await elsewhere() : '';
  const none = { ...noEngine(reasons.length > 0 ? reasons.join('; ') : 'nowhere to look'), wsl };

  return seen.length > 0 ? { ...none, elsewhere: seen } : none;
}

/**
 * One GET, with the reason when it did not work.
 *
 * <p>The timeout is enforced here rather than trusted to the platform: `fetch` has no default one,
 * so a host that accepts the connection and never answers would leave the panel's probe pending for
 * as long as the socket stayed open. Four seconds is long enough for a loaded engine to answer a
 * model list and short enough that a repaint is not held up by it.</p>
 */
async function getJson(url: string, timeoutMs: number): Promise<{ body?: unknown; why: string }> {
  const abort = new AbortController();
  let timedOut = false;
  const timer = setTimeout(() => {
    timedOut = true;
    abort.abort();
  }, timeoutMs);
  try {
    const response = await fetch(url, { signal: abort.signal, headers: { accept: 'application/json' } });
    if (!response.ok) {
      return { why: `answered ${response.status}` };
    }

    return { body: await response.json(), why: '' };
  } catch (error) {
    return {
      why: timedOut
        ? `no answer within ${Math.round(timeoutMs / 1000)}s`
        : (error as Error)?.message ?? 'connection refused',
    };
  } finally {
    clearTimeout(timer);
  }
}

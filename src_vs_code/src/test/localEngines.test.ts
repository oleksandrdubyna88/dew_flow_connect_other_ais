import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  discoverEngine,
  engineNote,
  LocalEngine,
  mergeModels,
  OLLAMA_PROBE,
  openAiBaseOf,
  parseOllamaTags,
  parseOpenAiModels,
  probeEngine,
  PROBE_CANDIDATES,
  VLLM_PROBE,
} from '../localEngines';

/**
 * What this machine has running locally, and which models it can review with.
 *
 * <p>Every payload below is captured from the real endpoint on 2026-09-02, not invented: Ollama on
 * `127.0.0.1:11434` with thirteen models. The two shapes are different on purpose — `/v1/models` is
 * the portable one that vLLM speaks too, and `api/tags` is the Ollama-only one that knows the
 * parameter size, the quantisation and the disk size.</p>
 */

const V1_MODELS = {
  object: 'list',
  data: [
    { id: 'qwen3-embedding:0.6b', object: 'model', created: 1786454982, owned_by: 'library' },
    { id: 'Qwen3.5-35B-A3B-Q5_vk64:latest', object: 'model', created: 1786454983, owned_by: 'library' },
    { id: 'qwen2.5-coder-14b-uncensored_64kv:latest', object: 'model', created: 1786454984, owned_by: 'library' },
  ],
};

const TAGS = {
  models: [
    {
      name: 'qwen2.5-coder-14b-uncensored_64kv:latest',
      size: 12_100_000_000,
      details: { parameter_size: '14.8B', quantization_level: 'Q6_K' },
    },
    {
      name: 'Qwen3.5-35B-A3B-Q5_vk64:latest',
      size: 24_800_000_000,
      details: { parameter_size: '34.7B', quantization_level: 'Q5_K_M' },
    },
  ],
};

test('the portable list gives the ids a request must carry', () => {
  const models = parseOpenAiModels(V1_MODELS);

  assert.deepEqual(models.map((m) => m.id), [
    'qwen3-embedding:0.6b',
    'Qwen3.5-35B-A3B-Q5_vk64:latest',
    'qwen2.5-coder-14b-uncensored_64kv:latest',
  ]);
  assert.equal(models[0]?.detail, '', 'the /v1 list says nothing about size or quantisation');
});

test('Ollama’s own list adds what the dropdown needs to choose with', () => {
  const models = parseOllamaTags(TAGS);
  const first = models.find((m) => m.id.startsWith('qwen2.5-coder-14b'));

  assert.equal(first?.detail, '14.8B · Q6_K · 12.1 GB');
});

test('the merge keeps every model and prefers the richer description', () => {
  // The portable list is the source of TRUTH about what can be asked for; the native list is the
  // source of DETAIL. A model in one and not the other must not disappear.
  const merged = mergeModels(parseOpenAiModels(V1_MODELS), parseOllamaTags(TAGS));

  assert.equal(merged.length, 3, 'the embedding model is only in the /v1 list and must survive');
  assert.equal(merged.find((m) => m.id.startsWith('Qwen3.5-35B'))?.detail, '34.7B · Q5_K_M · 24.8 GB');
  assert.equal(merged.find((m) => m.id === 'qwen3-embedding:0.6b')?.detail, '');
});

test('the probe url and the OpenAI base are different urls', () => {
  // The trap this function exists for, recorded in dew_flow_rag_qln: Ollama serves its own API at
  // the root and its OpenAI-compatible surface under /v1. An entry holding the probe url fails at
  // its first completion with a 404 that reads like a model problem.
  assert.equal(openAiBaseOf('http://127.0.0.1:11434'), 'http://127.0.0.1:11434/v1');
  assert.equal(openAiBaseOf('http://127.0.0.1:11434/'), 'http://127.0.0.1:11434/v1');
  assert.equal(openAiBaseOf('http://box:8000/v1'), 'http://box:8000/v1', 'already a base, left alone');
});

test('the candidates are the two engines’ own defaults', () => {
  assert.deepEqual(PROBE_CANDIDATES, [OLLAMA_PROBE, VLLM_PROBE]);
  assert.match(OLLAMA_PROBE, /:11434$/);
  assert.match(VLLM_PROBE, /:8000$/);
});

test('an engine that answered says what it is and how many models', () => {
  const engine: LocalEngine = {
    kind: 'ollama',
    probeUrl: OLLAMA_PROBE,
    apiBaseUrl: openAiBaseOf(OLLAMA_PROBE),
    reachable: true,
    status: 'v0.15.2',
    models: mergeModels(parseOpenAiModels(V1_MODELS), parseOllamaTags(TAGS)),
  };

  assert.match(engineNote(engine, 'win32'), /ollama v0\.15\.2/);
  assert.match(engineNote(engine, 'win32'), /3 models/);
});

test('nothing listening is a reason, never an empty dropdown', () => {
  const nothing: LocalEngine = {
    kind: 'none', probeUrl: OLLAMA_PROBE, apiBaseUrl: '', reachable: false,
    status: 'connection refused', models: [],
  };

  const note = engineNote(nothing, 'win32');
  assert.match(note, /No local engine answered/);
  assert.match(note, /11434/, 'the note must say where it looked');
  assert.match(note, /paste an endpoint/i, 'and what to do instead');
});

test('on Linux the reason names the WSL case, because that is the likely one', () => {
  // Measured: Windows Ollama binds 127.0.0.1 only, so from a VS Code attached to WSL the gateway
  // times out and the local loopback refuses. An empty list there is not "you have no models".
  const nothing: LocalEngine = {
    kind: 'none', probeUrl: OLLAMA_PROBE, apiBaseUrl: '', reachable: false,
    status: 'connection refused', models: [],
  };

  const note = engineNote(nothing, 'linux');
  assert.match(note, /OLLAMA_HOST=0\.0\.0\.0/);
  assert.match(note, /WSL/);
});

test('an engine seen on the Windows side is named, and the cure that reaches it is offered', () => {
  // The state this whole change exists for: fifteen models, one hop away, and a panel that said
  // exactly what it says to a machine with no engine at all. Measured 2026-09-03.
  const nothing: LocalEngine = {
    kind: 'none', probeUrl: OLLAMA_PROBE, apiBaseUrl: '', reachable: false,
    status: 'connection refused', models: [], elsewhere: 'ollama 0.33.2',
  };

  const note = engineNote(nothing, 'linux');
  assert.match(note, /Windows side/i, 'the sighting is the news');
  assert.match(note, /ollama 0\.33\.2/, 'and naming it is what proves it is not a guess');
  assert.match(note, /mirrored/, 'the cure that needs no firewall and no address that drifts');
});

test('with nothing seen anywhere the note does not invent a Windows engine', () => {
  const nothing: LocalEngine = {
    kind: 'none', probeUrl: OLLAMA_PROBE, apiBaseUrl: '', reachable: false,
    status: 'connection refused', models: [],
  };

  // The Linux note names the Windows case as a POSSIBILITY either way; what it must never do is
  // claim something was actually seen there, which is a different sentence and a different action.
  assert.doesNotMatch(engineNote(nothing, 'linux'), /answering on the Windows side/i);
});

test('the Windows side is asked only after every candidate has refused', async () => {
  let asked = 0;
  const probe = async (): Promise<string> => {
    asked += 1;

    return 'ollama 0.33.2';
  };

  const found = await discoverEngine(['http://127.0.0.1:1'], 500, probe);

  assert.equal(asked, 1, 'nothing answered, so the other side is worth asking');
  assert.equal(found.elsewhere, 'ollama 0.33.2');
  assert.match(engineNote(found, 'linux'), /ollama 0\.33\.2/);
});

test('an engine that answered here is not followed by a question about over there', async () => {
  let asked = 0;
  const probe = async (): Promise<string> => {
    asked += 1;

    return 'ollama 0.33.2';
  };

  const found = await discoverEngine([OLLAMA_PROBE], 4000, probe);
  if (!found.reachable) {
    return; // nothing running on this machine right now; the refused case above covers the shape
  }
  assert.equal(asked, 0, 'a working engine makes the interop question pure cost');
});

test('junk from an endpoint produces no models rather than a crash', () => {
  for (const junk of [null, undefined, 42, 'text', {}, { data: 'no' }, { data: [1, 2] }]) {
    assert.deepEqual(parseOpenAiModels(junk), []);
  }
  for (const junk of [null, undefined, 42, { models: 'no' }]) {
    assert.deepEqual(parseOllamaTags(junk), []);
  }
});

test('a probe that found nothing says WHY, and the reasons differ', async () => {
  // Accepted from the gate's second round. "No answer" was already better than an empty dropdown,
  // but refused and timed out want different actions from a person: nothing listening versus
  // something listening and not answering — a firewall, a wedged engine, a host that accepts TCP
  // and never replies.
  const refused = await probeEngine('http://127.0.0.1:1', 500);

  assert.equal(refused.reachable, false);
  assert.notEqual(refused.status, '', 'the reason must not be empty');
  assert.doesNotMatch(refused.status, /^no answer$/, 'and must not be the old placeholder');
});

test('a live engine is described by its own version', async () => {
  // The one test here that needs the real thing, and it is worth it: every parse above is fed a
  // captured payload, so nothing else would notice if the probe stopped asking for the version.
  const engine = await probeEngine('http://127.0.0.1:11434', 4000);

  if (!engine.reachable) {
    return; // nothing running on this machine right now; the refused case above covers the shape
  }
  assert.equal(engine.kind, 'ollama');
  assert.match(engine.status, /^\d+\.\d+/, 'the engine version, not a placeholder');
  assert.ok(engine.models.length > 0);
  assert.equal(engine.apiBaseUrl, 'http://127.0.0.1:11434/v1');
});

import { Runtime, RUNTIMES } from './models';

/** Every CLI shape this build can drive. Kept beside the parser that has to recognise them. */

/**
 * The reviewers, as an editable LIST rather than a fixed three.
 *
 * <p>The panel can add and remove them because the panel is not the limit: a new vendor with an
 * OpenAI-compatible endpoint is a base URL and a key, and the Codex CLI already knows how to be
 * pointed at one. Hard-coding three would have made every future vendor a release.</p>
 */
export interface Vendor {
  readonly id: string;
  readonly runtime: Runtime;
  readonly model: string;
  readonly enabled: boolean;
  /**
   * Which STAGES this vendor reviews. Both by default, and absent means both.
   *
   * <p>Measured over fourteen judged runs (`research/RESULTS_vendor_overlap_2026-09-06.md`): `local`
   * was 19 % useful on a plan and 3 % on code, while writing more findings than codex and gemini
   * together. So the useful setting is not "local on or off" — it is on for the plan and off for the
   * code, and until these two flags existed that could not be expressed anywhere.</p>
   *
   * <p>`enabled` stays the master switch: off means off everywhere, and these two are then
   * irrelevant rather than contradictory.</p>
   */
  readonly plan: boolean;
  readonly code: boolean;
  /**
   * Whether this vendor reviews DOCUMENTS — and absent means nobody has said, unlike its neighbours.
   *
   * <p><b>Optional where `plan` and `code` are not, and that is the design.</b> Those two fold an
   * absent value to `true` on the way in, because "yes" is what a configuration written before they
   * existed meant by saying nothing. This one cannot, because the honest reading of silence depends
   * on where the reviewer runs: for a vendor on this machine it is the plan tick, and for a Team
   * server it is NO — a company document must not cross the network because somebody once ticked a
   * box about plans. {@link reviewsDocuments} is that rule, and `coai-mcp` holds the same one.</p>
   *
   * <p>Touching either stage box pins this to its current effective value, so the absent state is a
   * migration reading rather than somewhere a person sits unawares. See {@link pinnedDocument}.</p>
   */
  readonly document?: boolean | undefined;
  /** OpenAI-compatible endpoint, for a vendor riding the Codex runtime. Empty = the CLI's own. */
  readonly baseUrl: string;
  /**
   * For a `remote` row: the vendor id the TEAM SERVER knows it by.
   *
   * <p><b>Not this row's id, and that is the point.</b> A row is named `<server>-<vendor>` so that two
   * Team servers each offering `codex` do not collide on one id — the id names the row, its usage
   * history and its vault key. The server knows only `codex`, so sending the row id would be refused
   * by every server and reported as a vendor it "does not offer", which reads exactly like a typo.
   * <p>Optional, and absent means the row's own id — which is what every other runtime uses and
   * what a hand-written row does. Required would have meant editing every existing fixture to
   * add a field none of them has a value for.</p>
   */
  readonly remoteVendor?: string | undefined;
  /**
   * For a `remote` row: which Team server entry it belongs to.
   *
   * <p>The row also stores that server's address, but an address is CORRECTABLE — somebody fixes a
   * typo in the hostname and every row that matched on the old string is orphaned: its model list
   * stops updating and removing the server no longer finds it. The server id is generated once and
   * never rewritten, which is the whole reason it exists. Absent on rows written before this field,
   * which fall back to matching by address. Caught on the code round.</p>
   */
  readonly teamServerId?: string | undefined;
  /**
   * Where this vendor's CLI is. Empty = look it up on PATH.
   *
   * <p>PATH is not always able to answer, and WSL is the case that proves it: `codex` resolves
   * there to the Windows npm shim on the interop PATH, which runs Linux node against a Windows
   * install and dies on a missing native dependency. The native Linux one sits in
   * `~/.npm-global/bin` and until this field existed nothing could point at it — so a WSL round
   * failed every time, whatever anybody configured.</p>
   */
  readonly executablePath: string;
  /**
   * What this vendor bills, per million tokens. Zero means "not set".
   *
   * <p>From the PERSON, never from a table we ship. A shipped price list is wrong for anyone on a
   * flat subscription, wrong the first time a vendor changes a price, and wrong silently in both
   * cases. Only Claude reports its own cost; codex and antigravity report tokens and nothing else,
   * so every row in the spending section read a dash — true, and useless against the question a
   * person actually has.</p>
   */
  readonly pricePerMillionIn: number;
  readonly pricePerMillionOut: number;
}

/** The model an Antigravity row starts on: flash at high effort, the CLI's own active model. */
export const ANTIGRAVITY_DEFAULT_MODEL = 'gemini-3.7-flash-high';

/**
 * What a fresh install reviews with: the two vendors whose CLIs authenticate themselves.
 *
 * <p><b>Antigravity, not Gemini, since 2026-09-01.</b> Google retired Code Assist for individual
 * accounts and its CLI now refuses before it reaches a model. The adapter for the replacement had
 * shipped the day before and nothing used it: no preset offered it, every default still named
 * gemini, and a saved list therefore went on pointing at a closed door. Supporting a vendor and
 * DEFAULTING to it are different changes, and only the first one had been made.</p>
 */
export const DEFAULT_VENDORS: readonly Vendor[] = [
  { id: 'codex', runtime: 'codex', model: '', enabled: true, plan: true, code: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
  { id: 'antigravity', runtime: 'antigravity', model: ANTIGRAVITY_DEFAULT_MODEL, enabled: true, plan: true, code: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
];

/**
 * Offered by "Add a reviewer…" — presets, not a closed set; the last is a blank to fill in.
 *
 * <p><b>Every default vendor is listed here too</b>, which is not redundancy: remove gemini and
 * the list it came from was the only place it existed, so it could never be added back. A default
 * that cannot be restored is a one-way door, and the operator walked through it.</p>
 */
/**
 * A model served on this machine, or on a box you can reach.
 *
 * <p>Exported on its own because it is the one preset whose MODEL cannot be defaulted here: what is
 * installed is a fact about the machine the panel is running on, discovered at repaint. An empty
 * model means "the first one the engine reports", decided there rather than guessed here.</p>
 */
export const LOCAL_PRESET: Vendor & { label: string; hint: string } = {
  label: 'Local model (Ollama / vLLM)',
  hint: 'A model on this machine, through its OpenAI-compatible endpoint — no CLI, no key, no bill.',
  id: 'local',
  runtime: 'local',
  model: '',
  enabled: true, plan: true, code: true,
  baseUrl: '',
  executablePath: '',
  pricePerMillionIn: 0,
  pricePerMillionOut: 0,
};

export const VENDOR_PRESETS: readonly (Vendor & { label: string; hint: string })[] = [
  {
    label: 'Codex (OpenAI)',
    hint: 'The Codex CLI, signed in as itself — the panel’s default first reviewer.',
    id: 'codex',
    runtime: 'codex',
    model: '',
    enabled: true, plan: true, code: true,
    baseUrl: '',
    executablePath: '',
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
  },
  {
    label: 'Antigravity (Google)',
    hint: 'Google’s replacement for Code Assist — one subscription reaching Gemini, Claude and GPT-OSS.',
    id: 'antigravity',
    runtime: 'antigravity',
    model: ANTIGRAVITY_DEFAULT_MODEL,
    enabled: true, plan: true, code: true,
    baseUrl: '',
    executablePath: '',
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
  },
  {
    label: 'Gemini (Google) — retired',
    hint: 'RETIRED by Google for individual accounts: it refuses before reaching a model. Kept only for a Workspace account that still has Code Assist.',
    id: 'gemini',
    runtime: 'gemini',
    model: '',
    enabled: true, plan: true, code: true,
    baseUrl: '',
    executablePath: '',
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
  },
  {
    label: 'Claude (a second one)',
    hint: 'A separate claude -p process: it sees the plan and the diff, never the conversation that produced them.',
    id: 'claude',
    runtime: 'claude',
    model: 'haiku',
    enabled: true, plan: true, code: true,
    baseUrl: '',
    executablePath: '',
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
  },
  {
    label: 'DeepSeek',
    hint: 'Rides the Codex CLI against api.deepseek.com — needs a key in the vault entry.',
    id: 'deepseek',
    runtime: 'codex',
    model: 'deepseek-chat',
    enabled: true, plan: true, code: true,
    baseUrl: 'https://api.deepseek.com/v1',
    executablePath: '',
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
  },
  {
    label: 'OpenRouter',
    hint: 'One key, many models, through the Codex CLI.',
    id: 'openrouter',
    runtime: 'codex',
    model: '',
    enabled: true, plan: true, code: true,
    baseUrl: 'https://openrouter.ai/api/v1',
    executablePath: '',
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
  },
  {
    label: 'Another OpenAI-compatible endpoint',
    hint: 'Give it a name and a base URL; the key goes in the vault entry under that name.',
    id: '',
    runtime: 'codex',
    model: '',
    enabled: true, plan: true, code: true,
    baseUrl: '',
    executablePath: '',
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
  },
  LOCAL_PRESET,
];

/** Read whatever is stored, keeping only entries that could actually be run. */
export function vendorsFrom(value: unknown): Vendor[] {
  if (!Array.isArray(value)) {
    return [...DEFAULT_VENDORS];
  }
  const vendors = value
    .filter((v): v is Record<string, unknown> => typeof v === 'object' && v !== null)
    .map((v) => ({
      id: typeof v['id'] === 'string' ? v['id'].trim().toLowerCase() : '',
      // An unknown runtime becomes `codex` because that is the one that takes a base URL, so a
      // name written by a NEWER extension still leaves a row that launches something.
      //
      // The list is imported rather than repeated, and that is not tidiness. It WAS repeated,
      // `local` was added to the type and not to the copy here, and every saved local reviewer
      // came back as a codex one — silently, under its own name. The comment that used to sit
      // here said the two must be kept in step; they are now one declaration instead.
      runtime: (RUNTIMES as readonly string[]).includes(v['runtime'] as string)
        ? (v['runtime'] as Runtime)
        : ('codex' as const),
      model: typeof v['model'] === 'string' ? v['model'].trim() : '',
      enabled: v['enabled'] !== false,
      // Absent is BOTH, which is what makes an existing configuration keep the gate it had after an
      // update. Only an explicit `false` narrows a vendor to one stage.
      plan: v['plan'] !== false,
      code: v['code'] !== false,
      // Written only when it IS said — `coai.vendors` is JSON a person reads, and the difference
      // between "absent" and "false" is load-bearing here in a way it is not for the two above.
      ...(typeof v['document'] === 'boolean' ? { document: v['document'] } : {}),
      baseUrl: typeof v['baseUrl'] === 'string' ? v['baseUrl'].trim() : '',
      // Only written when there IS one, so a codex row is byte-identical to what it always was —
      // `coai.vendors` is JSON a person reads and edits, and a `"remoteVendor": ""` on every row
      // is noise that means nothing.
      ...(typeof v['remoteVendor'] === 'string' && v['remoteVendor'].trim().length > 0
        // NOT lower-cased: this is the SERVER's own spelling of its vendor id, and a server whose
        // catalog says `DeepSeek` matches `DeepSeek`. Lower-casing it here (and again in the C#
        // parser) would have had every such vendor refused as one the server "does not offer".
        // Caught on the code round.
        ? { remoteVendor: v['remoteVendor'].trim() }
        : {}),
      ...(typeof v['teamServerId'] === 'string' && v['teamServerId'].trim().length > 0
        ? { teamServerId: v['teamServerId'].trim().toLowerCase() }
        : {}),
      executablePath: typeof v['executablePath'] === 'string' ? v['executablePath'].trim() : '',
      pricePerMillionIn: rate(v['pricePerMillionIn']),
      pricePerMillionOut: rate(v['pricePerMillionOut']),
    }))
    .filter((v) => v.id.length > 0)
    .map(migrateRetired);

  // A stored list that names nothing runnable is not a configuration, it is an accident.
  return vendors.length > 0 ? dedupe(vendors) : [...DEFAULT_VENDORS];
}

/**
 * A reviewer saved before the retirement, moved to the CLI Google pointed at.
 *
 * <p>The RUNTIME moves and the id stays: the id names the row, its usage history and its vault
 * key, so renaming it would orphan all three. The model goes with the runtime, because a model id
 * from the old CLI is not one the new CLI lists — <c>gemini-flash-latest</c> is not an `agy`
 * model, and leaving it would trade a dead CLI for a refused one.</p>
 *
 * <p>A vendor with its own base URL is never touched: that is not Google's CLI at all.</p>
 */
function migrateRetired(vendor: Vendor): Vendor {
  return vendor.runtime === 'gemini' && vendor.baseUrl.length === 0
    ? { ...vendor, runtime: 'antigravity', model: ANTIGRAVITY_DEFAULT_MODEL }
    : vendor;
}

/** One id, one vendor: two rows with the same name would fight over the same key and env. */
function dedupe(vendors: Vendor[]): Vendor[] {
  const seen = new Set<string>();
  return vendors.filter((v) => (seen.has(v.id) ? false : (seen.add(v.id), true)));
}

/** A rate is a non-negative number or it is unset; a negative price would credit the person. */
function rate(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0 ? value : 0;
}

/** A name a person typed → something usable as an id, an env-var suffix and a vault key. */
export function normaliseId(name: string): string {
  return name
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
}

/**
 * The environment the server reads: the vendor list as JSON, because a comma-separated string
 * cannot carry a runtime and a base URL, and inventing a second encoding for them would be a
 * format nobody could read in a config file.
 */
export function vendorsEnv(vendors: readonly Vendor[]): string {
  return JSON.stringify(
    vendors
      .filter((v) => v.enabled)
      .map((v) => ({
        id: v.id,
        runtime: v.runtime,
        model: v.model,
        baseUrl: v.baseUrl,
        executablePath: v.executablePath,
        // The SERVER's own name for this vendor, which is not the row's id and must not be
        // confused with it: a row is `<server>-<vendor>` so two Team servers offering `codex` do
        // not collide, while `--vendor` has to carry what the server actually knows. This was
        // missing for the whole life of the `remote` runtime — the server received the row id,
        // refused it as a vendor it "does not offer", and the reviewer was dropped from every
        // round while the panel went on reporting it as configured.
        //
        // `teamServerId` deliberately stays behind: the server has no field for it and no
        // question it answers. It exists so the PANEL can follow a row to its server entry after
        // somebody fixes a typo in a hostname.
        //
        // Guarded by `typeof`, not by `!== undefined`, and trimmed before it is weighed. This is
        // JSON a person edits: a hand-written `"remoteVendor": null` passes an undefined check and
        // then has `.length` read off it, which throws inside the settings sync — and a sync that
        // throws leaves the server on the file's previous contents with nothing saying the write
        // never happened. A name made only of spaces is absent for the same reason it is absent
        // from `vendorsFrom`. Trimming is not the normalisation this field forbids: that one is
        // about CASE, and neither side lower-cases, because a server whose catalog says `DeepSeek`
        // matches `DeepSeek`. Both raised on this change's code round.
        ...(typeof v.remoteVendor === 'string' && v.remoteVendor.trim().length > 0
          ? { remoteVendor: v.remoteVendor.trim() }
          : {}),
        // Written only when NARROWED, like every other value in the env block: a vendor that
        // reviews both stages says nothing, and the server reads an absent flag as both. So the
        // block a person opens still carries only what differs from the defaults.
        ...(v.plan ? {} : { plan: false }),
        ...(v.code ? {} : { code: false }),
        // And this one is written whenever it was SAID, in either direction — the one field here
        // whose absence is a value rather than a default. `coai-mcp` reads an absent `document` as
        // "the plan tick for a local vendor, no for a Team server", which is the whole consent rule;
        // emitting `true` only when narrowed would have thrown away the half that says yes.
        ...(v.document === undefined ? {} : { document: v.document }),
      })),
  );
}

/**
 * Whether this vendor reviews documents, once an absent switch has been read.
 *
 * <p><b>Absent answers differently depending on where the reviewer runs.</b> For a vendor this
 * machine launches itself, absent is the plan tick — the reading the document round shipped with,
 * nothing leaves the laptop, and there is no permission to ask for. For a Team server, absent is NO:
 * the tick would otherwise grant permission for a company document to cross the network,
 * retroactively, on every configuration written before documents existed. A tick that means "this
 * vendor is good at prose" cannot also mean "this file may go to the shared box".</p>
 *
 * <p>The same rule lives in `ProviderSettings.Serves`, which is the one that actually decides a
 * round; this is what the panel draws so the box agrees with what will happen.</p>
 */
export function reviewsDocuments(vendor: Vendor): boolean {
  return vendor.document ?? (vendor.runtime !== 'remote' && vendor.plan);
}

/**
 * What a vendor was told about documents, as a word rather than an absence.
 *
 * <p>`coai-mcp` names these three states `DocumentReviews.Unspecified | Yes | No` — a routing rule
 * reading a null is what the C# doctrine forbids, and a decision about whether a file leaves the
 * machine deserves a name for its most interesting case. Here the stored field stays
 * `boolean | undefined` because that IS the wire format: `coai.vendors` is JSON a person edits, the
 * settings block carries it verbatim, and inventing a third spelling for this side would be a second
 * schema to keep level. This function is the name, for reading and for tests.</p>
 */
export type DocumentSetting = 'unspecified' | 'yes' | 'no';

export function documentSetting(vendor: Vendor): DocumentSetting {
  return vendor.document === undefined ? 'unspecified' : vendor.document ? 'yes' : 'no';
}

/**
 * The `document` value to write ALONGSIDE a change to another stage box.
 *
 * <p>Empty unless this vendor's document switch is still absent and the box being changed is the
 * `plan` one. In that case the absent value is pinned to what it means right now — so somebody who
 * unticks *plan* on a local vendor does not silently lose their document rounds as a side effect,
 * and somebody who ticks it does not silently gain them.</p>
 *
 * <p>Which is the whole point of the migration reading ending at the first touch: the panel is where
 * a person decides, so the moment they decide anything here, the thing that was inferred becomes
 * something they said.</p>
 */
export function pinnedDocument(vendor: Vendor, key: string): { document?: boolean } {
  return key === 'plan' && vendor.document === undefined
    ? { document: reviewsDocuments(vendor) }
    : {};
}

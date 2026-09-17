/**
 * The Consultant section's WRITE path: what an edit in that section stores.
 *
 * <p>Its own module since 2026-09-15, and the reason is a number: `settingsShape.ts` had grown to
 * 1054 lines against the 800 the shared coding-style rule allows, and this was the largest thing in
 * it that was about one feature rather than about settings in general. `consultSettings.ts` is the
 * READ half of the same feature — the resolution rule, the defaults, the skew and vault-key notes —
 * so the write half sitting beside it is where a person looking for either would look.</p>
 *
 * <p>Nothing here changed in the move. The rule these functions serve is stated once, in
 * `resolveConsultant`, and everything below writes what that rule would read back.</p>
 */

import { Vendor, VENDOR_PRESETS, normaliseId } from './vendors';
import { namesACredential } from './credentialWords';
import {
  CALLER_KINDS,
  ConsultantChoice,
  ConsultantDefinition,
  ResolvedConsultant,
  consultableVendors,
  consultantChoiceFrom,
  DEFAULT_CONSULT,
  isChoiceField,
  resolveConsultant,
} from './consultSettings';

/** The fields a control in the section edits in place. The vendor takes its own path: it re-resolves. */
type ConsultantField = 'model' | 'baseUrl' | 'executablePath';

/** Which field each control's setting key changes — one map, so an unknown key writes nothing. */
const CONSULTANT_FIELDS: Readonly<Record<string, ConsultantField>> = {
  consultModel: 'model',
  consultBaseUrl: 'baseUrl',
  consultExecutablePath: 'executablePath',
};

/**
 * One caller's consultant, merged into whatever the stored map already holds — as a DEFINITION.
 *
 * <p>Merged rather than replaced, exactly as a role record is: writing what Claude Code asks must
 * not drop the other three callers, and the stored object is what every other row reads on the next
 * repaint. It merges into the RAW `coai.consultants` object, so a caller kind this build has no name
 * for survives the write — a newer panel may know more of them, which is why the server's own
 * `Merge` keeps them too.</p>
 *
 * <p><b>What changed on 2026-09-14 (story A2 of `PLAN_the_consultant_has_its_own_vendors`): the
 * write stops storing a REFERENCE.</b> It used to put back `{vendor, model}`, where the vendor was a
 * reviewer row's id and everything else — the runtime, the endpoint, the CLI path, and the model when
 * none was named — was borrowed from that row on every read. Story A1 made the READ resolve that,
 * which fixed a consultant dying with a reviewer it never chose; but the file itself still held the
 * reference, so the consultant went on following the row. Here the first edit in the section writes
 * what will actually run: {@link resolveConsultant} against the rows this side can see, stored whole.
 * After it, the two settings are genuinely independent — editing the reviewer row moves the reviewer
 * and leaves the consultant where the person put it.</p>
 *
 * <p>A vendor change still CLEARS the model before resolving — a model named for one vendor is not a
 * model the next one offers — and what lands is then the model the new vendor will really use: its
 * row's, where a row lends one, and otherwise none. An id that is blank or only spaces is REFUSED:
 * it keys no vault entry and names no runtime, so the map comes back untouched rather than holding a
 * row nothing can run. An entry the rule cannot place stays a bare reference and gains no invented
 * runtime — only its model is editable, because guessing the rest would send a working tree to a
 * vendor nobody chose.</p>
 */
export function consultantRecordUpdate(
  current: Readonly<Record<string, unknown>>,
  caller: string,
  key: string,
  value: unknown,
  vendors: readonly Vendor[],
): Record<string, unknown> {
  // The caller arrives in a webview message, and the only kinds this build emits are its own four.
  // An id it does not know is refused rather than indexed with: `__proto__` through `current[caller]`
  // reads Object.prototype and would write a key nobody asked for. (gemini, A2's code round.)
  if (!CALLER_KINDS.some((one) => one.id === caller)) {
    return { ...current };
  }

  return key === 'consultVendor'
    ? vendorChosen(current, caller, String(value).trim(), vendors)
    : fieldEdited(current, caller, CONSULTANT_FIELDS[key], String(value).trim(), vendors);
}

/**
 * An endpoint of the consultant's own: the name and the URL a person typed, as THAT caller's definition.
 *
 * <p>Story C6, and the last entry the picker was missing. What the catalogue's blank preset IS is the
 * `codex` runtime at an endpoint, so that is what is stored — with the minted id as the vendor, which
 * is what keys the vault entry and the usage ledger. Nothing is appended to the reviewer rows: the
 * ruling is three independent sets of settings, and a consultant that added itself to somebody's
 * reviewers would be the coupling this plan removed, re-entering by the only door left open. Two
 * callers may hold the same id at the same URL, and that is one vault key used twice — the point of
 * having a name. (gemini, C6's plan round, on where the definition lands.)</p>
 *
 * <p>The id is normalised the way *Add a reviewer* normalises it, because the two flows must mint the
 * SAME id from the same words or one credential ends up under two keys. A name that normalises to
 * nothing writes nothing: there is no vault entry to key and nothing to show, so refusing is the only
 * honest outcome. So does a caller kind this build does not have — the same guard, and the same
 * reason, as `consultantRecordUpdate`'s.</p>
 */
export function consultantEndpointWrite(
  current: Readonly<Record<string, unknown>>,
  caller: string,
  name: string,
  baseUrl: string,
): Record<string, unknown> {
  const id = normaliseId(name);
  // The runtime comes from the catalogue entry the person picked, never from here. Deciding it at
  // the write meant the picker could show that preset's label while the stored definition named
  // something else — one edit to `VENDOR_PRESETS` away from an endpoint launched through the wrong
  // CLI. (codex, C6's code round.)
  const { custom } = consultableVendors();
  if (id.length === 0 || custom === undefined || !CALLER_KINDS.some((one) => one.id === caller)) {
    return { ...current };
  }

  return merged(
    current,
    caller,
    { kind: 'definition', vendor: id, runtime: custom.runtime, model: '', baseUrl: baseUrl.trim(), executablePath: '' },
    undefined,
  );
}

/**
 * What two boxes MEAN once they are closed — and what a dismissal of either means, which is nothing.
 *
 * <p>Its own function because it is the only part of the flow that can be tested: the boxes
 * themselves are `vscode.window.showInputBox`, and a host is not something this suite has. A
 * dismissed box is `undefined` and a name that normalises to nothing keys no vault entry, so both
 * answer "no endpoint" and the caller writes nothing at all. The second box is the one worth naming:
 * a person who typed a name and then changed their mind has given no more consent than one who
 * closed the first, and an endpoint minted from a name alone would have no URL to reach.
 * (local, C6's plan round.)</p>
 */
export function endpointAnswer(
  name: string | undefined,
  baseUrl: string | undefined,
): { readonly id: string; readonly baseUrl: string } | undefined {
  const id = normaliseId(name ?? '');
  const where = (baseUrl ?? '').trim();

  return name === undefined || baseUrl === undefined || id.length === 0 || where.length === 0
    ? undefined
    : { id, baseUrl: where };
}

/**
 * Whether a name is already spoken for at a DIFFERENT endpoint — said while it is being typed.
 *
 * <p>One id is one vault key and one credential, so two endpoints under one name would send a key to
 * whichever of them answered. The check spans the reviewer ROWS and the other callers' consultants,
 * because both key the vault the same way; the same URL under the same name is not a conflict at all,
 * it is the same service named once. Returns the sentence to show, or empty for "go ahead" — a
 * validator's shape, so `showInputBox` can refuse while the box is open rather than after it closes.
 * (gemini, C6's plan round.)</p>
 */
export function endpointConflict(
  name: string,
  baseUrl: string,
  vendors: readonly Vendor[],
  consultants: Readonly<Record<string, unknown>>,
  caller: string,
): string {
  const id = normaliseId(name);
  const wanted = baseUrl.trim();
  const clash = holders(vendors, consultants, caller)
    .find((one) => one.id.toLowerCase() === id.toLowerCase() && one.baseUrl !== wanted);

  if (clash === undefined) {
    return '';
  }

  // Both halves named, because a holder can have no endpoint of its own — `claude` is a catalogue
  // preset and a vault key with no URL — and the sentence then has neither a place to name nor an
  // endpoint to offer. Inline this was a ternary inside a template literal inside a ternary, which
  // SonarCloud flagged three times over and was right to.
  const where = clash.baseUrl.length > 0 ? ` at ${clash.baseUrl}` : '';
  const orUseIt = clash.baseUrl.length > 0 ? ' or use that endpoint' : '';

  return `'${id}' is already ${clash.what}${where}`
    + ` — one name is one key in the vault, so pick another name${orUseIt}`;
}

/**
 * Why a typed endpoint is not one — or `undefined` when it is fine to go on.
 *
 * <p>`startsWith('http')` was the whole check, and it accepts `http-not-a-url`: the box closes, the
 * setting is stored, and the person learns it is wrong the next time they are stuck and a
 * consultation fails. Parsing is the only way to know, and `URL` is the parser both halves of this
 * product already trust. (codex, C6's code round.)</p>
 *
 * <p><b>A credential in the URL is refused rather than stored.</b> `https://user:token@host/v1` and
 * `?api_key=…` both work against many gateways, and both end up in `settings.json` — which for a
 * WORKSPACE setting is a file people commit. This product keeps keys in one CredsForDevs entry
 * precisely so they are never in argv, a log line or a settings file, and an endpoint box is not the
 * place to make an exception. The vault entry under this name is where the key goes. (codex, C6's
 * code round; I took the refusal and not the redaction — the URL a conflict names is one already in
 * this person's own settings, shown back to that same person.)</p>
 */
export function badEndpoint(typed: string): string | undefined {
  const text = typed.trim();
  let parsed: URL;
  try {
    parsed = new URL(text);
  } catch {
    return 'A base URL is needed — something like https://api.example.com/v1';
  }

  if (!['http:', 'https:'].includes(parsed.protocol) || parsed.hostname.length === 0) {
    return 'The endpoint has to be an http or https address';
  }

  return parsed.username.length > 0 || parsed.password.length > 0 || carriesAKey(parsed)
    ? 'Leave the key out of the URL — it goes in the vault entry under this name, which is what keeps it out of settings.json'
    : undefined;
}

/**
 * Whether a URL carries a credential in its query or its fragment.
 *
 * <p>The fragment is read for the same reason the query is. A `#` never reaches the server, so it is
 * not a leak to the vendor — but this string is about to be written into `settings.json`, and a
 * workspace settings file is a file people commit, which is the whole point of refusing it.</p>
 */
function carriesAKey(parsed: URL): boolean {
  const named = [
    ...parsed.searchParams.keys(),
    ...new URLSearchParams(parsed.hash.replace(/^#/, '')).keys(),
  ];

  // `credentialWords.ts`, not a list here. This one WAS the list, private to this module, until the
  // notifications ledger needed the same judgement — and the first attempt at sharing it added a
  // second copy beside this one instead of moving it, which the code round caught. A marker added to
  // one list and not the other would make settings validation and ledger redaction disagree about
  // the same string: refused in one path, written to a file in the other.
  return named.some(namesACredential);
}

/**
 * Everything that already means something by a name — and the caller doing the editing is not one.
 *
 * <p>Three holders, because all three key the vault the same way. The reviewer ROWS. The CATALOGUE,
 * which is the one no reviewer row need exist for: with no `deepseek` row configured a person could
 * name their own endpoint `deepseek`, and the next caller to pick DeepSeek out of the list would
 * share a vault key with a different service (codex, C6's code round). And the OTHER callers'
 * consultants — but not this caller's own, which is the record about to be replaced: counting it
 * made a person's own endpoint unchangeable for good, and told them a name they had chosen belonged
 * to somebody else (gemini, C6's code round, from two roles).</p>
 *
 * <p>A holder with no endpoint of its own still holds the NAME — `claude` is a catalogue preset and
 * a vault key — so it clashes, and the sentence then stops short of telling anyone to use an
 * endpoint there is none of.</p>
 */
function holders(
  vendors: readonly Vendor[],
  consultants: Readonly<Record<string, unknown>>,
  caller: string,
): readonly { readonly id: string; readonly baseUrl: string; readonly what: string }[] {
  return [
    ...vendors.map((one) => ({ id: one.id, baseUrl: one.baseUrl, what: 'a reviewer' })),
    ...VENDOR_PRESETS.filter((one) => one.id.length > 0)
      .map((one) => ({ id: one.id, baseUrl: one.baseUrl, what: `what this build calls ${one.label}` })),
    ...Object.entries(consultants)
      .filter(([whose]) => whose !== caller)
      .map(([, one]) => consultantChoiceFrom(one))
      .map((one) => ({ id: one.vendor, baseUrl: one.baseUrl, what: "another caller's consultant" })),
  ];
}

/**
 * The row a write starts FROM — and an absent one means the caller's shipped pair, not a blank.
 *
 * <p>The reader's own rule, mirrored: `callers` reads a row with no vendor as `DEFAULT_CONSULT.stored`
 * for that kind, because a caller nobody has configured is one running the shipped default. Starting
 * from a blank instead resolved to UNAVAILABLE and stored `{vendor: '', model: …}` — which the very
 * next read threw away, taking the person's edit with it. A side that holds a row for one caller and
 * nothing for another is ordinary: the panel writes the caller somebody edited, so a workspace
 * overlay holds exactly the rows touched there. (gemini, A2's code round, twice.)</p>
 */
function startingChoice(current: Readonly<Record<string, unknown>>, caller: string): ConsultantChoice {
  const stored = consultantChoiceFrom(current[caller]);

  return stored.vendor.length > 0 ? stored : DEFAULT_CONSULT.stored[caller]!;
}

/**
 * A vendor arriving from the picker — a CHANGE clears the model, the same choice again changes nothing.
 *
 * <p>Clearing is for a change: a model named for one vendor is not a model the next one offers. Sent
 * the vendor already chosen — a re-selection, or a message delivered twice — the old code built a
 * fresh bare reference and re-resolved it, handing the reviewer row's model and endpoint back over
 * whatever the person had set. The consultant is not a reference to that row any more, so nothing may
 * be re-lent to it. (codex, A2's code round.)</p>
 *
 * <p><b>That guard is for a DEFINITION, and narrowing it to one is this story's code round.</b> A
 * stored entry can be a bare REFERENCE whose id the catalogue also offers: `deepseek` names no
 * runtime and needs no reviewer row, so without one it resolves to UNAVAILABLE while the picker
 * offers it two lines below as a working choice. Keeping the reference because the id matched meant
 * a person clicked the vendor their own row was already showing them and nothing happened — same
 * reference in, same unplaceable row back. There is nothing of theirs to protect in a reference: it
 * holds no model they chose, no endpoint and no CLI path. So a re-selection MATERIALISES it, which
 * is what picking a catalogue entry has meant since C5. (codex, this story's code round.)</p>
 */
function vendorChosen(
  current: Readonly<Record<string, unknown>>,
  caller: string,
  id: string,
  vendors: readonly Vendor[],
): Record<string, unknown> {
  const starting = startingChoice(current, caller);
  if (id.length === 0) {
    return { ...current };
  }

  return id === starting.vendor && starting.runtime !== ''
    ? merged(current, caller, resolveConsultant(starting, vendors), current[caller])
    : merged(current, caller, chosen(id, vendors), undefined);
}

/**
 * What a newly picked vendor MEANS — the catalogue first, since story C5.
 *
 * <p>The picker offers the catalogue now, so what a person chose is a CATALOGUE ENTRY, and the entry
 * says what it is: DeepSeek is the codex runtime at `api.deepseek.com`, OpenRouter is the codex
 * runtime at theirs. Resolving the bare id instead — which is all this did before — sent both of them
 * to the UNAVAILABLE state, because no reviewer row of that name exists and neither id is a runtime:
 * the section offered an entry that could not be stored.</p>
 *
 * <p>It also settles a question the ruling had already answered. For an id that IS a runtime name,
 * this stores the plain runtime rather than the model of a reviewer row with the same name. That is
 * the point of the whole plan — three independent sets of settings — and it is the last place the
 * consultant was still reaching into somebody's reviewer. A person who wants that model chooses it
 * in the box beside the vendor, where they can see it.</p>
 */
function chosen(id: string, vendors: readonly Vendor[]): ResolvedConsultant {
  const preset = consultableVendors().offered.find((one) => one.id === id);

  return preset === undefined
    ? resolveConsultant(bareReference(id), vendors)
    : {
      kind: 'definition',
      vendor: preset.id,
      runtime: preset.runtime,
      model: preset.model,
      baseUrl: preset.baseUrl,
      executablePath: preset.executablePath,
    };
}

function fieldEdited(
  current: Readonly<Record<string, unknown>>,
  caller: string,
  field: ConsultantField | undefined,
  value: string,
  vendors: readonly Vendor[],
): Record<string, unknown> {
  return field === undefined
    ? { ...current }
    : merged(
      current,
      caller,
      edited(resolveConsultant(startingChoice(current, caller), vendors), field, value),
      current[caller],
    );
}

/** A vendor id with nothing else claimed — what a person picking from the list has actually said. */
function bareReference(vendor: string): ConsultantChoice {
  return { vendor, runtime: '', model: '', baseUrl: '', executablePath: '' };
}

/**
 * The caller's row, replaced by what the rule answered — with anything this build cannot name kept.
 *
 * <p>`keep` is the row as stored, and it is passed for an edit to one FIELD and withheld for a vendor
 * CHANGE. The outer map already survives a caller kind this build has no name for; a field inside a
 * row deserves the same, because a newer panel may hold one and an edit beside it must not delete it.
 * A vendor change is a fresh start, so anything the old vendor had goes with it — otherwise DeepSeek's
 * endpoint would still be sitting under a Claude consultant. (codex, A2's code round.)</p>
 */
function merged(
  current: Readonly<Record<string, unknown>>,
  caller: string,
  one: ResolvedConsultant,
  keep: unknown,
): Record<string, unknown> {
  return { ...current, [caller]: { ...unnamedFields(keep), ...storedShape(one) } };
}

function unnamedFields(row: unknown): Record<string, unknown> {
  const stored = typeof row === 'object' && row !== null && !Array.isArray(row) ? row as Record<string, unknown> : {};

  return Object.fromEntries(Object.entries(stored).filter(([key]) => !isChoiceField(key)));
}

/**
 * What a resolved consultant looks like in `settings.json`.
 *
 * <p>An endpoint and a CLI path are written only when they hold something: absent and empty mean the
 * same thing to every reader of this setting — the CLI's own endpoint, and whatever is on PATH — and
 * a file a person edits by hand is worth keeping readable. The runtime is always written, because it
 * is the one field that tells a definition from the legacy reference this used to store.</p>
 */
function storedShape(one: ResolvedConsultant): Record<string, unknown> {
  return one.kind === 'unavailable'
    ? { vendor: one.vendor, model: one.model }
    : {
      vendor: one.vendor,
      runtime: one.runtime,
      model: one.model,
      ...(one.baseUrl.length > 0 ? { baseUrl: one.baseUrl } : {}),
      ...(one.executablePath.length > 0 ? { executablePath: one.executablePath } : {}),
    };
}

/** One field replaced. An unplaceable entry admits only its model — nothing else has a meaning yet. */
function edited(one: ResolvedConsultant, field: ConsultantField, value: string): ResolvedConsultant {
  if (one.kind === 'definition') {
    return editedDefinition(one, field, value);
  }

  return field === 'model' ? { ...one, model: value } : one;
}

function editedDefinition(one: ConsultantDefinition, field: ConsultantField, value: string): ResolvedConsultant {
  return {
    kind: 'definition',
    vendor: one.vendor,
    runtime: one.runtime,
    model: field === 'model' ? value : one.model,
    baseUrl: field === 'baseUrl' ? value : one.baseUrl,
    executablePath: field === 'executablePath' ? value : one.executablePath,
  };
}

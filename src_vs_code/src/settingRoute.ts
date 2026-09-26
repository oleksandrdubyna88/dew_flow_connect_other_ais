import { isModelSlot, type ModelSlot } from './commandModels';

/*
 * How one changed control of the panel is routed to where it is kept (`settingRoute`). Moved out of `settingsShape.ts`
 * when the dropdown's `control` joined it and that file passed 800 lines; `settingsShape` re-exports
 * all of it, so no importer changed.
 */

/**
 * Where one changed control is kept: a plain setting, one vendor's property, or one role's entry in
 * a role-keyed record.
 *
 * <p>Three kinds because there ARE three, and the panel used to have two slots for them. `rounds`
 * and `thresholds` are records keyed by role, and their inputs travelled in the vendor slot — so the
 * provider looked for a vendor called `Architecture`, found none, and wrote nothing. The number
 * reverted on the next repaint and the prompt pickers never changed count.</p>
 */
/**
 * <p>A FOURTH kind arrived with the consultant, and for the same reason the third did: its controls
 * are keyed by CALLER — which agent is stuck — and travelling in the vendor slot would have the
 * provider hunt for a vendor called `claude` when the row means "what Claude Code asks", and
 * sometimes find one.</p>
 */
export type SettingWrite = (
  | { readonly kind: 'plain'; readonly key: string; readonly value: unknown }
  | { readonly kind: 'vendor'; readonly key: string; readonly value: unknown; readonly vendor: string }
  | { readonly kind: 'role'; readonly key: string; readonly value: unknown; readonly role: string }
  | { readonly kind: 'caller'; readonly key: string; readonly value: unknown; readonly caller: string }
  | { readonly kind: 'commandModel'; readonly key: ModelSlot; readonly value: unknown; readonly commandModel: string }
) & { readonly control?: ControlKind };

/** The kind of control a write came from, where the host needs it: only a dropdown says so. */
export type ControlKind = 'select';

/** What the webview said it changed. A message with no key changes nothing. */
export interface SettingMessage {
  readonly key: string | undefined;
  readonly value: unknown;
  readonly vendor?: string | undefined;
  readonly role?: string | undefined;
  readonly caller?: string | undefined;
  /** The caller KIND whose split-order model a box names (issue #117) — a key of `coai.commandModels`. */
  readonly commandModel?: string | undefined;
  /** `select` when a dropdown changed — absent for every other control. */
  readonly control?: ControlKind | undefined;
}

/**
 * The write the page ASKED for, rebuilt from the raw webview message — every routing field it may
 * carry, and nothing else.
 *
 * <p>Its own function because this step is where a routing field can be lost with every test green:
 * the page sent it and `settingWrite` would have routed it, and the one line between them rebuilt the
 * message from a hand-written list of four fields. (Issue #117's code review.)</p>
 */
export function settingMessageFrom(m: {
  readonly key?: string | undefined;
  readonly value?: unknown;
  readonly vendor?: string | undefined;
  readonly role?: string | undefined;
  readonly caller?: string | undefined;
  readonly commandModel?: string | undefined;
  readonly control?: unknown;
}): SettingMessage {
  return {
    key: m.key, value: m.value, vendor: m.vendor, role: m.role, caller: m.caller, commandModel: m.commandModel,
    // Only the literal crosses — the page is the only sender, and a value it never sends is not believed.
    ...(m.control === 'select' ? { control: 'select' as const } : {}),
  };
}

/**
 * Route one changed control. Pure: the `vscode` call it leads to is the provider's business, and
 * this is the part that was wrong.
 */
export function settingWrite(message: SettingMessage): SettingWrite | undefined {
  const write = routed(message);

  // Carried on EVERY kind, because a dropdown sits in a vendor row, a consultant row and a split-order
  // slot as well as among the plain settings — and a refusal is snapped back by the control, not the kind.
  return write === undefined || message.control !== 'select' ? write : { ...write, control: 'select' };
}

function routed(message: SettingMessage): SettingWrite | undefined {
  const { key, value } = message;
  if (!named(key)) {
    return undefined;
  }
  if (named(message.commandModel)) {
    // The key names WHICH slot changed, and only the two slots exist: anything else is a page and a
    // host that disagree, and writing it would store a field no reader looks at.
    return isModelSlot(key) ? { kind: 'commandModel', key, value, commandModel: message.commandModel } : undefined;
  }

  return keyedBy(message, key, value);
}

/** The row a control belongs to — a caller, a role or a vendor, in that order — or none, a plain setting. */
function keyedBy(message: SettingMessage, key: string, value: unknown): SettingWrite {
  if (named(message.caller)) {
    return { kind: 'caller', key, value, caller: message.caller };
  }
  if (named(message.role)) {
    return { kind: 'role', key, value, role: message.role };
  }

  return named(message.vendor) ? { kind: 'vendor', key, value, vendor: message.vendor } : { kind: 'plain', key, value };
}

/** A routing field that is really set: present and not empty. */
function named(field: string | undefined): field is string {
  return field !== undefined && field.length > 0;
}

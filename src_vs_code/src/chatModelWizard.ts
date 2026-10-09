import * as vscode from 'vscode';
import type { ChatCatalog } from './chatModels';
import { ChatVendorChoice, chatRunSpec } from './chatPresets';
import { CHAT_RUNTIMES } from './cliChatLaunch';
import { allowedModelsFor, modelsFor } from './models';
import { askPerson } from './personWait';
import { teamServersFrom } from './teamServers';
import { VENDOR_PRESETS } from './vendors';

/**
 * *Add a model* on the chat presets page: the four questions a chat model is made of, asked one at a time. Moved from
 * `chatPresetsPanel.ts` (research/PLAN_one_model_catalog.md E4.6b) so the presets' editing core (`chatPresetsHost.ts`)
 * can ask them without importing the page that draws them.
 */

/** What the four questions answer — a vendor, one of its models, a name, and the text the composer opens with. */
export interface AskedModel {
  readonly vendor: ChatVendorChoice;
  readonly model: string;
  readonly name: string;
  readonly startingPrompt: string;
}

/**
 * The four questions, one at a time.
 *
 * <p>The shape of *Add a model* — `showQuickPick` with a label and the sentence under it — because
 * that is what the operator asked for by name, and because a row id alone ("remsoftdev-codex") does
 * not say what it reaches. The list is the configured ROWS rather than the vendor kinds that dialog
 * offers: a vendor cannot say WHICH row answers, and two `codex` rows with different keys or prices
 * are two different backends, which is the decision three reviewers settled on the provider plan.</p>
 *
 * <p>Each step can be escaped, and escaping writes nothing — a half-made preset is the defect this
 * whole area has just been cleared of. The starting prompt is the one optional step: empty is a
 * preset that only changes the model.</p>
 *
 * @param catalog what the panel last discovered — nothing is fetched here; a dialog must not wait on three probes
 */
export async function askForAModel(catalog: ChatCatalog): Promise<AskedModel | undefined> {
  const chosen = await askWhichVendor(catalog);
  const model = chosen === undefined ? undefined : await askWhichModel(catalog, chosen.vendor, chosen.label);

  return chosen === undefined || model === undefined ? undefined : named(chosen, model);
}

/** Steps 3 and 4: the name, then the optional starting text. */
async function named(chosen: { readonly label: string; readonly vendor: ChatVendorChoice }, model: { readonly id: string; readonly label: string }): Promise<AskedModel | undefined> {
  const name = await askPerson(() => vscode.window.showInputBox({
    title: 'Add a model — step 3 of 4',
    prompt: 'A name for this preset — it is what the button above the composer says',
    value: model.label.length > 0 ? model.label : chosen.label,
  }));
  if (name === undefined) {
    return undefined;
  }
  // The LAST step is optional, and escaping it means "none" rather than "throw the other three
  // away". Escape is how a person skips an optional field in every other VS Code dialog, and
  // discarding a finished preset for using it is the wizard punishing the ordinary gesture.
  const startingPrompt = await askPerson(() => vscode.window.showInputBox({
    title: 'Add a model — step 4 of 4, optional',
    prompt: 'What the composer opens with when this model is chosen. Leave it empty for none.',
    placeHolder: 'You are a business analyst…',
  }));

  return { vendor: chosen.vendor, model: model.id, name, startingPrompt: startingPrompt ?? '' };
}

/**
 * Step 1 — the VENDORS, which is the list *Add a model* offers.
 *
 * <p>Asked for five times before it was built, and the reason is the one that made the chat
 * independent in the first place: the person is choosing what will ANSWER, not borrowing somebody's
 * reviewer. One source — `VENDOR_PRESETS` and the *Team servers* section — so a vendor added to
 * the product appears here without anybody remembering to, and the sentence under each is the
 * vendor's own.</p>
 *
 * <p>Only vendors the chat can speak to are offered; the rest would be an entry that cannot answer.
 * A Team server contributes one entry per vendor IT hosts, because the server is the endpoint and
 * the vendor on it is what runs.</p>
 */
async function askWhichVendor(catalog: ChatCatalog): Promise<{ label: string; vendor: ChatVendorChoice } | undefined> {
  const local = VENDOR_PRESETS
    .filter((preset) => CHAT_RUNTIMES.includes(preset.runtime))
    .map((preset) => ({
      label: preset.label,
      detail: preset.hint,
      vendor: { runtime: preset.runtime, baseUrl: preset.baseUrl } as ChatVendorChoice,
    }));
  const remote = teamServersFrom(vscode.workspace.getConfiguration('coai').get('teamServers')).flatMap((server) =>
    serverVendorsOf(catalog, server.id).map((name) => ({
      label: server.name + ' · ' + name,
      detail: 'on ' + server.url + " — the company's subscription, nothing to install",
      vendor: { runtime: 'remote', teamServerId: server.id, remoteVendor: name } as ChatVendorChoice,
    })));

  return askPerson(() => vscode.window.showQuickPick(
    [...local, ...remote],
    { title: 'Add a model — step 1 of 4', placeHolder: 'Which vendor should answer?' },
  ));
}

/** What a Team server said it hosts, from the catalog the panel last fetched. */
function serverVendorsOf(catalog: ChatCatalog, serverId: string): readonly string[] {
  const cached = catalog.teamServers.find((one) => one.server.id === serverId);

  return (cached?.catalog?.vendors ?? []).map((one) => one.id);
}

/**
 * Step 2 — the models that vendor offers, from the catalog the panel discovered.
 *
 * <p>A vendor whose models are DISCOVERED and whose probe has not answered offers none. Asking
 * anyway would be a dialog with nothing in it, so the answer is an empty model — which everywhere
 * else in this feature means "whatever that vendor is set to".</p>
 */
async function askWhichModel(catalog: ChatCatalog, vendor: ChatVendorChoice, label: string):
Promise<{ id: string; label: string } | undefined> {
  const spec = chatRunSpec({
    id: '',
    name: '',
    // Nothing is saved here — this is the probe that asks a vendor what it offers, and `main` is a
    // fact about a LIST that this row is not in yet.
    main: false,
    runtime: vendor.runtime,
    model: '',
    executablePath: '',
    baseUrl: vendor.baseUrl ?? '',
    teamServerId: vendor.teamServerId,
    remoteVendor: vendor.remoteVendor,
  });
  const offered = modelsFor(
    vendor.runtime,
    catalog.discoveredCodex,
    '',
    catalog.localEngine,
    catalog.discoveredAgy,
    allowedModelsFor(spec, catalog.teamServers).models,
    undefined,
    '',
    // A vendor on somebody else's endpoint offers nothing here rather than the Codex CLI's cache, so the
    // step answers "whatever that vendor is set to" (PLAN_custom_endpoint_model_list).
    { baseUrl: vendor.baseUrl ?? '', keyName: '' },
  );
  if (offered.length === 0) {
    return { id: '', label: '' };
  }

  return askPerson(() => vscode.window.showQuickPick(
    offered.map((one) => ({ label: one.label, id: one.id })),
    { title: 'Add a model — step 2 of 4', placeHolder: 'Which of ' + label + ' models?' },
  ));
}

import type { PanelState } from './panelView';

/**
 * The Models tab of the new Settings page (todo/PLAN_one_model_catalog.md, E3.2): every model this side can use, added
 * once, each card saying what it is used for and how it answers.
 */
export function modelsTabHtml(state: PanelState): string {
  return `<p class="lead">Every model this side can use, added once. Tick what each one is used for; a model can be added
more than once with different settings.</p>
<p class="hint">${state.vendors.length} model${state.vendors.length === 1 ? '' : 's'} in this side's catalog.</p>`;
}

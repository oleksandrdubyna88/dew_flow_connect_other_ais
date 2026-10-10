import assert from 'node:assert/strict';
import { test } from 'node:test';

import { launchesChart } from '../teamChart';
import { DailyUsage } from '../teamServerApi';
import { pageTree, PageNode } from './pageTree';

/**
 * The 30-day launches-per-day chart (story 3.3), read as the markup it IS — parsed, never matched as a substring:
 * bars to scale that add up to each day's launches, an axis that says what it counts, the days as the server wrote them,
 * a legend that names every vendor, and a text alternative a screen reader can use.
 */

const palette = (vendor: string): string => `var(--vc-${vendor})`;

/** Thirty dense days, as the server sends them: every vendor on every day, a zero day included. */
function daily(): DailyUsage {
  const days = Array.from({ length: 30 }, (_, i) => {
    const day = `2026-09-${String(i + 11).padStart(2, '0')}`.replace('2026-09-31', '2026-10-01');

    return { day, vendors: [{ vendor: 'codex', runs: i % 5 }, { vendor: 'gemini', runs: i === 29 ? 7 : 1 }] };
  });

  return { fromUtc: '2026-09-11T00:00:00Z', toUtc: '2026-10-11T00:00:00Z', days: days.map((d, i) => (i < 20 ? d : { ...d, day: `2026-10-${String(i - 19).padStart(2, '0')}` })) };
}

function bars(tree: PageNode): readonly PageNode[] {
  return tree.find((node) => node.tagName === 'RECT' && node.attrs['data-day'] !== undefined);
}

test('the bars add up to each day\'s launches, and are drawn to one scale', () => {
  const data = daily();
  const tree = pageTree(launchesChart(data, palette));
  const drawn = bars(tree);
  assert.ok(drawn.length > 0, 'the chart drew no bars');

  for (const day of data.days) {
    const mine = drawn.filter((bar) => bar.attrs['data-day'] === day.day);
    const runs = mine.reduce((sum, bar) => sum + Number(bar.attrs['data-runs']), 0);
    assert.equal(runs, day.vendors.reduce((sum, v) => sum + v.runs, 0), `${day.day} does not add up`);
  }
  const unit = drawn.map((bar) => Number(bar.attrs['height']) / Number(bar.attrs['data-runs']));
  assert.ok(unit.every((one) => Math.abs(one - unit[0]!) < 1e-6), `bars are not to one scale: ${unit.join(', ')}`);
});

test('a vendor with no launches on a day draws no bar for it — no sliver that reads as one', () => {
  const tree = pageTree(launchesChart(daily(), palette));

  assert.ok(bars(tree).every((bar) => Number(bar.attrs['data-runs']) > 0));
});

/** The labels the chart draws itself: the svg's own <text> nodes — never a bar's <title>, the note or the table. */
function drawnLabels(tree: PageNode): readonly PageNode[] {
  const svg = tree.one((node) => node.tagName === 'SVG', 'svg');

  return svg.find((node) => node.tagName === 'TEXT');
}

test('the axis says it counts launches, and the days are the server\'s strings, never re-dated', () => {
  const data = daily();
  const labels = drawnLabels(pageTree(launchesChart(data, palette))).map((node) => node.text());

  assert.ok(labels.includes('launches'), `the axis does not say what it counts: ${labels.join(' | ')}`);
  assert.ok(labels.includes(data.days[0]!.day) && labels.includes(data.days[29]!.day),
    `the chart's own day labels are not the server's strings: ${labels.join(' | ')}`);
});

test('the axis label starts inside the drawing, where nothing clips it', () => {
  const axis = drawnLabels(pageTree(launchesChart(daily(), palette))).find((node) => node.text() === 'launches');

  assert.ok(axis !== undefined, 'no axis label');
  assert.equal(axis.attrs['text-anchor'], 'start', 'an end-anchored label at the left edge runs off the svg and is cut');
  assert.ok(Number(axis.attrs['x']) >= 0);
});

test('the numbers fold carries a fold key, so a push can keep it open', () => {
  const tree = pageTree(launchesChart(daily(), palette, 'acme'));
  const fold = tree.one((node) => node.tagName === 'DETAILS' && node.className.includes('chart-data'), 'the numbers fold');

  assert.equal(fold.attrs['data-fold'], 'acme|chart-data');
});

test('a legend names every vendor in words, not by colour alone', () => {
  const tree = pageTree(launchesChart(daily(), palette));
  const legend = tree.one((node) => node.className.split(' ').includes('legend'), 'legend');

  assert.match(legend.text(), /codex/);
  assert.match(legend.text(), /gemini/);
});

test('the chart has a text alternative: a summary on the image, and the numbers as a table', () => {
  const tree = pageTree(launchesChart(daily(), palette));
  const svg = tree.one((node) => node.tagName === 'SVG', 'svg');

  assert.equal(svg.attrs['role'], 'img');
  assert.match(svg.attrs['aria-label'] ?? '', /launches per day/i);
  const rows = tree.find((node) => node.tagName === 'TR');
  assert.equal(rows.length, 31, 'a header and one row per day');
});

test('the chart says its 30 calendar days are not the Month window', () => {
  assert.match(pageTree(launchesChart(daily(), palette)).text(), /not the Month window/);
});

test('colours are the theme\'s, through the vendor palette — no colour is written into the chart', () => {
  const html = launchesChart(daily(), palette);

  assert.doesNotMatch(html, /#[0-9a-fA-F]{3,8}\b/, 'a hex colour in the chart ignores the theme');
  assert.match(html, /var\(--vc-codex\)/);
});

test('a hostile vendor name or day string from the server is text', () => {
  const data: DailyUsage = { fromUtc: '', toUtc: '', days: [{ day: '<b>x</b>', vendors: [{ vendor: '<img src="y">', runs: 1 }] }] };
  const tree = pageTree(launchesChart(data, palette));

  assert.equal(tree.find((node) => node.tagName === 'IMG' || node.tagName === 'B').length, 0);
});

test('thirty days with no launches say so, rather than drawing an empty frame that reads as broken', () => {
  const quiet: DailyUsage = { ...daily(), days: daily().days.map((d) => ({ ...d, vendors: d.vendors.map((v) => ({ ...v, runs: 0 })) })) };

  assert.match(pageTree(launchesChart(quiet, palette)).text(), /No launches in these 30 days/);
});

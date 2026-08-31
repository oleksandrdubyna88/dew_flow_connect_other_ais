#!/usr/bin/env node
/**
 * Renders media/icon.png (128×128) and media/panel.svg from one idea.
 *
 * The Marketplace listing icon must be a raster image — package.json's `icon` field rejects an
 * SVG — while the Activity Bar container wants the SVG, monochrome, so VS Code can tint it.
 * Rather than carry a binary nobody can regenerate, this draws it: no dependencies, just
 * node:zlib for the PNG stream. (The technique is CredsForDevs' generate-icon.mjs; the glyph
 * is not.)
 *
 * The glyph: one filled centre — the AI writing the code — ringed by three outlined circles, the
 * independent reviewers, each joined to it by a spoke. Three because that is the panel: one
 * architecture, one security, one performance. Outlined rather than filled because they are
 * looking IN at the work, not producing it.
 *
 * Usage: node scripts/generate-icon.mjs
 */
import { deflateSync } from 'node:zlib';
import { writeFileSync, mkdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const SIZE = 128;
const SS = 4; // supersample; a 4×4 box filter gives 17 levels of anti-aliasing
const HI = SIZE * SS;
const UNITS = 24; // the SVG viewBox this mirrors
const SCALE = HI / UNITS;

const BACKGROUND = [0x2d, 0x50, 0x8a, 0xff]; // a calm review blue, dark enough for white on it
const GLYPH = [0xff, 0xff, 0xff, 0xff];

const CENTRE = { cx: 12, cy: 12, r: 3.1 };
const RING = 7.4; // how far the reviewers sit from the centre
const REVIEWER_R = 2.5;
const REVIEWER_STROKE = 1.15;
const SPOKE = 0.95;

/** Three reviewers, evenly spaced, the first at the top. */
const reviewers = [-90, 30, 150].map((deg) => {
  const rad = (deg * Math.PI) / 180;
  return { cx: 12 + RING * Math.cos(rad), cy: 12 + RING * Math.sin(rad) };
});

const distance = (px, py, x, y) => Math.hypot(px - x, py - y);

/** Distance from a point to a segment — how the spokes are drawn without a path rasteriser. */
function toSegment(px, py, x1, y1, x2, y2) {
  const dx = x2 - x1;
  const dy = y2 - y1;
  const lengthSquared = dx * dx + dy * dy;
  const t = lengthSquared === 0 ? 0 : Math.max(0, Math.min(1, ((px - x1) * dx + (py - y1) * dy) / lengthSquared));
  return Math.hypot(px - (x1 + t * dx), py - (y1 + t * dy));
}

/** Is this supersampled pixel inside the glyph? Units are the viewBox, not device pixels. */
function inGlyph(x, y) {
  if (distance(x, y, CENTRE.cx, CENTRE.cy) <= CENTRE.r) {
    return true;
  }

  for (const r of reviewers) {
    const d = distance(x, y, r.cx, r.cy);
    if (Math.abs(d - REVIEWER_R) <= REVIEWER_STROKE / 2) {
      return true; // the ring itself
    }

    // The spoke, stopping short of both circles so the joins stay clean.
    const from = { x: CENTRE.cx, y: CENTRE.cy };
    if (
      toSegment(x, y, from.x, from.y, r.cx, r.cy) <= SPOKE / 2 &&
      distance(x, y, CENTRE.cx, CENTRE.cy) > CENTRE.r - 0.2 &&
      distance(x, y, r.cx, r.cy) > REVIEWER_R + REVIEWER_STROKE / 2 - 0.2
    ) {
      return true;
    }
  }

  return false;
}

/** The rounded square the icon sits in — VS Code shows it unmasked. */
function inBackground(x, y) {
  const radius = 5.2;
  const inset = 0.6;
  const min = inset;
  const max = UNITS - inset;
  const cx = Math.min(Math.max(x, min + radius), max - radius);
  const cy = Math.min(Math.max(y, min + radius), max - radius);
  return x >= min && x <= max && y >= min && y <= max && distance(x, y, cx, cy) <= radius + 1e-9
    ? true
    : x >= min + radius && x <= max - radius && y >= min && y <= max
      ? true
      : y >= min + radius && y <= max - radius && x >= min && x <= max;
}

const pixels = Buffer.alloc(SIZE * SIZE * 4);
for (let y = 0; y < SIZE; y++) {
  for (let x = 0; x < SIZE; x++) {
    let coverage = 0;
    let glyph = 0;
    for (let sy = 0; sy < SS; sy++) {
      for (let sx = 0; sx < SS; sx++) {
        const ux = ((x * SS + sx + 0.5) / HI) * UNITS;
        const uy = ((y * SS + sy + 0.5) / HI) * UNITS;
        if (inBackground(ux, uy)) {
          coverage++;
          if (inGlyph(ux, uy)) {
            glyph++;
          }
        }
      }
    }

    const samples = SS * SS;
    const offset = (y * SIZE + x) * 4;
    const alpha = coverage / samples;
    for (let c = 0; c < 3; c++) {
      const blended =
        coverage === 0 ? 0 : (GLYPH[c] * glyph + BACKGROUND[c] * (coverage - glyph)) / coverage;
      pixels[offset + c] = Math.round(blended);
    }
    pixels[offset + 3] = Math.round(alpha * 255);
  }
}

// --- PNG container ---
const CRC_TABLE = Array.from({ length: 256 }, (_, n) => {
  let c = n;
  for (let k = 0; k < 8; k++) {
    c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  }
  return c >>> 0;
});

function crc32(buffer) {
  let c = 0xffffffff;
  for (const byte of buffer) {
    c = CRC_TABLE[(c ^ byte) & 0xff] ^ (c >>> 8);
  }
  return (c ^ 0xffffffff) >>> 0;
}

function chunk(type, data) {
  const length = Buffer.alloc(4);
  length.writeUInt32BE(data.length);
  const body = Buffer.concat([Buffer.from(type, 'latin1'), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(body));
  return Buffer.concat([length, body, crc]);
}

const ihdr = Buffer.alloc(13);
ihdr.writeUInt32BE(SIZE, 0);
ihdr.writeUInt32BE(SIZE, 4);
ihdr[8] = 8; // bit depth
ihdr[9] = 6; // colour type: RGBA

const raw = Buffer.alloc(SIZE * (SIZE * 4 + 1));
for (let y = 0; y < SIZE; y++) {
  const rowStart = y * (SIZE * 4 + 1);
  raw[rowStart] = 0; // filter: none
  pixels.copy(raw, rowStart + 1, y * SIZE * 4, (y + 1) * SIZE * 4);
}

const png = Buffer.concat([
  Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
  chunk('IHDR', ihdr),
  chunk('IDAT', deflateSync(raw, { level: 9 })),
  chunk('IEND', Buffer.alloc(0)),
]);

const media = join(dirname(fileURLToPath(import.meta.url)), '..', 'media');
mkdirSync(media, { recursive: true });
writeFileSync(join(media, 'icon.png'), png);

// The Activity Bar wants a monochrome SVG so VS Code can tint it for the active/inactive states.
const spokes = reviewers
  .map((r) => `<line x1="12" y1="12" x2="${r.cx.toFixed(2)}" y2="${r.cy.toFixed(2)}" stroke-width="${SPOKE}"/>`)
  .join('\n    ');
const rings = reviewers
  .map((r) => `<circle cx="${r.cx.toFixed(2)}" cy="${r.cy.toFixed(2)}" r="${REVIEWER_R}" fill="none" stroke-width="${REVIEWER_STROKE}"/>`)
  .join('\n    ');
writeFileSync(
  join(media, 'panel.svg'),
  `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" stroke="currentColor" stroke-linecap="round">
    ${spokes}
    ${rings}
    <circle cx="12" cy="12" r="${CENTRE.r}" stroke="none"/>
</svg>
`,
);

console.log(`wrote media/icon.png (${SIZE}×${SIZE}, ${png.length} bytes) and media/panel.svg`);

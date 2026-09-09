/**
 * A PNG that SAYS a number, drawn from scratch.
 *
 * <p>It exists because of the sharpest finding on this probe's plan round: <b>a CLI exiting 0 does
 * not prove the model received the image.</b> An unknown field in a JSON turn can be ignored
 * silently, an unsupported payload can be dropped, and either way the process answers cheerfully and
 * the probe records a "yes" that commits the project to building a paste feature for a vendor that
 * throws pictures away. So the image carries an unambiguous token, the prompt demands it back
 * verbatim, and only the token appearing in the answer counts as receipt.</p>
 *
 * <p>Drawn rather than checked in: a binary fixture in the repository is a thing nobody can read in a
 * diff, and this is sixty lines of digits. No dependency — a PNG is a header, one deflated block of
 * scanlines and a footer, and `node:zlib` already does the only hard part.</p>
 */
import { deflateSync } from 'node:zlib';

/** Five-by-seven digits, one string per row, `#` for ink. Legible at any sane scale. */
const GLYPHS = {
  0: ['.###.', '#...#', '#..##', '#.#.#', '##..#', '#...#', '.###.'],
  1: ['..#..', '.##..', '..#..', '..#..', '..#..', '..#..', '.###.'],
  2: ['.###.', '#...#', '....#', '...#.', '..#..', '.#...', '#####'],
  3: ['#####', '...#.', '..#..', '...#.', '....#', '#...#', '.###.'],
  4: ['...#.', '..##.', '.#.#.', '#..#.', '#####', '...#.', '...#.'],
  5: ['#####', '#....', '####.', '....#', '....#', '#...#', '.###.'],
  6: ['..##.', '.#...', '#....', '####.', '#...#', '#...#', '.###.'],
  7: ['#####', '....#', '...#.', '..#..', '.#...', '.#...', '.#...'],
  8: ['.###.', '#...#', '#...#', '.###.', '#...#', '#...#', '.###.'],
  9: ['.###.', '#...#', '#...#', '.####', '....#', '...#.', '.##..'],
};

const GLYPH_WIDTH = 5;
const GLYPH_HEIGHT = 7;

/** The CRC every PNG chunk carries. Twelve lines, so that this file needs nothing installed. */
function crc32(bytes) {
  let crc = 0xffffffff;
  for (const byte of bytes) {
    crc ^= byte;
    for (let bit = 0; bit < 8; bit += 1) {
      crc = (crc >>> 1) ^ (0xedb88320 & -(crc & 1));
    }
  }

  return (crc ^ 0xffffffff) >>> 0;
}

function chunk(type, data) {
  const length = Buffer.alloc(4);
  length.writeUInt32BE(data.length, 0);
  const body = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(body), 0);

  return Buffer.concat([length, body, crc]);
}

/**
 * A black-on-white PNG of `token`, scaled up so no model has to squint.
 *
 * @param {string} token digits only — the thing the answer must repeat back
 * @param {number} scale how many pixels per glyph pixel
 * @param {number} margin white border, in scaled pixels
 * @returns {Buffer} a complete PNG
 */
export function tokenPng(token, scale = 16, margin = 24) {
  const digits = [...token];
  if (digits.some((digit) => GLYPHS[digit] === undefined)) {
    throw new Error(`tokenPng draws digits only, and was given "${token}"`);
  }

  const gap = 1;
  const inkWidth = digits.length * GLYPH_WIDTH + (digits.length - 1) * gap;
  const width = inkWidth * scale + margin * 2;
  const height = GLYPH_HEIGHT * scale + margin * 2;

  // One byte per pixel, greyscale, with a filter byte at the start of every row.
  const raw = Buffer.alloc((width + 1) * height, 0xff);
  for (let y = 0; y < height; y += 1) {
    raw[y * (width + 1)] = 0;
  }
  const plot = (x, y) => {
    if (x >= 0 && x < width && y >= 0 && y < height) {
      raw[y * (width + 1) + 1 + x] = 0x00;
    }
  };

  digits.forEach((digit, index) => {
    const rows = GLYPHS[digit];
    const originX = margin + index * (GLYPH_WIDTH + gap) * scale;
    rows.forEach((row, rowIndex) => {
      [...row].forEach((cell, columnIndex) => {
        if (cell !== '#') {
          return;
        }
        for (let dy = 0; dy < scale; dy += 1) {
          for (let dx = 0; dx < scale; dx += 1) {
            plot(originX + columnIndex * scale + dx, margin + rowIndex * scale + dy);
          }
        }
      });
    });
  });

  const header = Buffer.alloc(13);
  header.writeUInt32BE(width, 0);
  header.writeUInt32BE(height, 4);
  header[8] = 8; // bit depth
  header[9] = 0; // colour type: greyscale
  header[10] = 0; // deflate
  header[11] = 0; // adaptive filtering
  header[12] = 0; // no interlace

  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', header),
    chunk('IDAT', deflateSync(raw, { level: 9 })),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

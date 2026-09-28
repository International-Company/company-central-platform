import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import jsQR from 'jsqr';
import qrcode from 'qrcode-generator';
import { describe, expect, it } from 'vitest';
import { qrGeometry } from './qr-code';

/**
 * The setup square, read back.
 *
 * **A QR that is wrong does not look wrong.** It fails by not scanning, in
 * somebody's hand, at the one moment they are trying to protect their account —
 * and the only thing they can report is "it does not work". So this does not
 * check that a path was produced: it turns the path back into pixels and reads
 * the code with a decoder, and asserts the characters that come out are the
 * ones that went in.
 *
 * The encoding itself is not what is under test. `qrcode-generator` has done
 * Reed-Solomon and mask selection for fifteen years. What is under test is the
 * drawing, which is written here, and the wiring, which is the mistake that
 * would matter most: a square encoding the wrong string still scans perfectly
 * and sets up an authenticator that never produces an accepted code.
 */

/** A provisioning URI in the shape the Platform builds, fully escaped ASCII. */
const provisioningUri =
  'otpauth://totp/Company%20Central%20Platform:admin'
  + '?secret=JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP'
  + '&issuer=Company%20Central%20Platform&algorithm=SHA1&digits=6&period=30';

/**
 * The path, back into the grid it came from.
 *
 * Every module is emitted as `M<x> <y>h1v1h-1z`, so reading the coordinates out
 * recovers exactly which cells were drawn — which is the thing worth checking,
 * because a transposed row and column, or a quiet zone left off, produce a path
 * that renders and a square that does not read.
 */
function cellsOf(path: string): Set<string> {
  return new Set(
    [...path.matchAll(/M(\d+) (\d+)h1v1h-1z/g)].map((match) => `${match[1]},${match[2]}`),
  );
}

/**
 * The grid as an image the decoder can read: one module to several pixels,
 * because a decoder given one pixel per module has no slack for the sampling
 * grid it fits.
 */
function rasterise(path: string, extent: number, scale = 8): Uint8ClampedArray {
  const side = extent * scale;
  const pixels = new Uint8ClampedArray(side * side * 4);

  // White everywhere first, quiet zone included. The border is not decoration:
  // a decoder finds the symbol by it.
  pixels.fill(255);

  for (const cell of cellsOf(path)) {
    const [column, row] = cell.split(',').map(Number) as [number, number];

    for (let y = row * scale; y < (row + 1) * scale; y += 1) {
      for (let x = column * scale; x < (column + 1) * scale; x += 1) {
        const at = (y * side + x) * 4;

        pixels[at] = 0;
        pixels[at + 1] = 0;
        pixels[at + 2] = 0;
        pixels[at + 3] = 255;
      }
    }
  }

  return pixels;
}

function decode(value: string): string | null {
  const { path, extent } = qrGeometry(value);
  const scale = 8;

  const read = jsQR(rasterise(path, extent, scale), extent * scale, extent * scale);

  return read?.data ?? null;
}

describe('the setup square', () => {
  it('reads back as exactly what went into it', () => {
    expect(decode(provisioningUri)).toBe(provisioningUri);
  });

  it('reads back whatever it is given, not only that one string', () => {
    // Two more, so the first is not passing by some accident of its length.
    for (const value of [
      'otpauth://totp/x:y?secret=AAAAAAAAAAAAAAAA&issuer=x&algorithm=SHA1&digits=6&period=30',
      'otpauth://totp/'
        + encodeURIComponent('شركة الاختبار')
        + ':'
        + encodeURIComponent('موظف')
        + '?secret=KRSXG5CTMVRXEZLUKRSXG5CTMVRXEZLU&issuer='
        + encodeURIComponent('شركة الاختبار')
        + '&algorithm=SHA1&digits=6&period=30',
    ]) {
      expect(decode(value), value.slice(0, 40)).toBe(value);
    }
  });

  it('leaves the quiet zone clear on every side', () => {
    // Four modules of white border. Without it many readers refuse the symbol
    // outright, and the failure is silent: the camera simply never locks on.
    const { path, extent, modules } = qrGeometry(provisioningUri);
    const cells = cellsOf(path);

    expect(extent).toBe(modules + 8);

    for (const cell of cells) {
      const [column, row] = cell.split(',').map(Number) as [number, number];

      expect(column).toBeGreaterThanOrEqual(4);
      expect(row).toBeGreaterThanOrEqual(4);
      expect(column).toBeLessThan(extent - 4);
      expect(row).toBeLessThan(extent - 4);
    }
  });

  /**
   * Every dark module where the encoder put it, and nowhere else.
   *
   * **Added because breaking the drawing on purpose did not fail anything.**
   * Transposing the rows and the columns produces a mirrored symbol, and jsQR
   * — like most decoders — reads a mirrored symbol quite happily, so all four
   * assertions went on passing against a square drawn wrong. Some readers do
   * not, and "works with most phones" is not a thing to ship into an
   * authentication flow.
   *
   * This compares the drawing against the encoder's own matrix, which is the
   * only check that says the two agree rather than that a decoder coped.
   */
  it('draws every module exactly where the encoder put it', () => {
    const { path } = qrGeometry(provisioningUri);
    const cells = cellsOf(path);

    const code = qrcode(0, 'M');

    code.addData(provisioningUri);
    code.make();

    const modules = code.getModuleCount();
    const wrong: string[] = [];

    for (let row = 0; row < modules; row += 1) {
      for (let column = 0; column < modules; column += 1) {
        // The path is written x then y, and the matrix is read row then
        // column. Getting that the wrong way round is the mistake this exists
        // to catch.
        const drawn = cells.has(`${column + 4},${row + 4}`);

        if (drawn !== code.isDark(row, column)) {
          wrong.push(`row ${row}, column ${column}`);
        }
      }
    }

    expect(wrong.slice(0, 5)).toEqual([]);
    expect(cells.size).toBeGreaterThan(0);
  });

  it('is a square somebody can actually hold a phone up to', () => {
    // A hundred and fifty-nine characters at error level M is a fifty-three
    // module symbol. If a change to the level or the encoding pushed it much
    // past that, the modules at two hundred pixels get too small to read from
    // arm's length, and nothing would say so.
    const { modules } = qrGeometry(provisioningUri);

    expect(modules).toBeLessThanOrEqual(57);
  });
});

describe('what the enrolment dialog puts in it', () => {
  it('is the provisioning URI, not the key beside it', () => {
    // **The mistake that would matter most, and the one nothing else catches.**
    // Both fields are strings on the same object, one word apart at the call
    // site. A square built from the manual key scans perfectly and sets up an
    // authenticator that produces codes the Platform will never accept, and the
    // person has no way to tell which of the two things is wrong.
    const screen = readFileSync(
      join(import.meta.dirname, '..', '..', 'features', 'security', 'security-screen.tsx'),
      'utf8',
    );

    expect(screen).toContain('<QrCode value={enrolment.provisioningUri}');
    expect(screen).not.toContain('<QrCode value={enrolment.manualEntryKey}');
  });
});

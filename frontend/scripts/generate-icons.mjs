/*
 * Draws the installed application's icons, and writes them to public/icons.
 *
 * Run by hand, and the output is committed:
 *
 *     node scripts/generate-icons.mjs
 *
 * **A wordmark, not a picture.** The design forbids icons standing in for
 * words inside the application (ARCHITECTURE.md §9.5), and the reasoning holds
 * on a home screen too: a glyph invented for this Platform would mean nothing
 * to anybody until they had learned it. Three letters in the face the Platform
 * is set in, white on the blue of its own top bar, is a mark somebody
 * recognises the first time.
 *
 * **Chromium draws it** rather than an image library, because Playwright is
 * already a dependency of this project and an icon generator that needs its
 * own native toolchain is an icon generator nobody can run. The page it draws
 * is plain HTML, so what the icon is can be read here rather than decoded from
 * a binary.
 *
 * The face is fetched from Google Fonts *at generation time only* -- nothing
 * here ships, and no visitor's browser ever asks Google for anything (the
 * application self-hosts its fonts through next/font). The script refuses to
 * write anything if the face did not load, because an icon silently drawn in
 * Arial is worse than no icon.
 */

import { mkdir, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from '@playwright/test';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const out = join(root, 'public', 'icons');

/** The deepest blue in the token set: the colour of the application's top bar. */
const blue = '#13304d';

const mark = 'CCP';

/**
 * The icons a browser actually asks for.
 *
 * `maskable` is padded to the safe zone Android's masks cut to: a platform may
 * crop a maskable icon to a circle, and a mark drawn to the edges loses its
 * outer letters when it does.
 */
const icons = [
  { file: 'icon-192.png', size: 192, padding: 0.16 },
  { file: 'icon-512.png', size: 512, padding: 0.16 },
  { file: 'icon-maskable-512.png', size: 512, padding: 0.3 },
  { file: 'apple-touch-icon.png', size: 180, padding: 0.16 },
];

function page(size, padding) {
  // The mark is sized from the box it is allowed to occupy, so every icon is
  // the same drawing at a different scale rather than four separate designs.
  const fontSize = Math.round(size * (1 - padding * 2) * 0.42);

  return `<!doctype html>
<html>
  <head>
    <meta charset="utf-8" />
    <link
      href="https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@600&display=block"
      rel="stylesheet"
    />
    <style>
      html, body { margin: 0; padding: 0; }
      body {
        width: ${size}px;
        height: ${size}px;
        display: flex;
        align-items: center;
        justify-content: center;
        background: ${blue};
      }
      span {
        font-family: "IBM Plex Sans";
        font-weight: 600;
        font-size: ${fontSize}px;
        letter-spacing: ${Math.round(fontSize * 0.02)}px;
        color: #ffffff;
        line-height: 1;
      }
    </style>
  </head>
  <body><span>${mark}</span></body>
</html>`;
}

const browser = await chromium.launch();
const context = await browser.newContext({ deviceScaleFactor: 1 });

await mkdir(out, { recursive: true });

for (const icon of icons) {
  const tab = await context.newPage();

  await tab.setViewportSize({ width: icon.size, height: icon.size });
  await tab.setContent(page(icon.size, icon.padding), { waitUntil: 'networkidle' });
  await tab.evaluate(() => document.fonts.ready);

  const loaded = await tab.evaluate(() => document.fonts.check('600 100px "IBM Plex Sans"'));

  if (!loaded) {
    throw new Error(
      'IBM Plex Sans did not load, so the mark would be drawn in whatever '
      + 'Chromium fell back to. Nothing was written.',
    );
  }

  const png = await tab.screenshot({ type: 'png' });

  await writeFile(join(out, icon.file), png);
  await tab.close();

  console.log(`${icon.file}  ${icon.size}x${icon.size}  ${png.length} bytes`);
}

await context.close();
await browser.close();

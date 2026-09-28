import qrcode from 'qrcode-generator';

/**
 * Data as a square somebody's phone can read.
 *
 * **Not an icon, and not decoration.** The no-icons rule (ARCHITECTURE.md §9.5)
 * is about glyphs standing in for words; this is the payload itself, in the
 * only form a camera can take it. A person holding a phone cannot type a
 * hundred and fifty-nine characters correctly, and the failure when they
 * mistype one is a code that never works with nothing saying why.
 *
 * **The encoding is the library's; the drawing is ours.** Reed-Solomon
 * correction over GF(256), eight mask patterns scored against four penalty
 * rules, and BCH-coded format information are not something to hand-write on
 * the way to a feature — and a QR encoded wrongly fails silently, by simply
 * never scanning. `qrcode-generator` is fifteen years old, has no dependencies
 * of its own, and does that part. What is written here is the rendering, which
 * is where the design decisions are, and where a mistake of ours would live.
 */

/**
 * The square as geometry: one SVG path, and the width of the box it needs.
 *
 * **Drawn as a single path rather than a grid of rectangles.** A hundred and
 * fifty-nine characters is a fifty-three module square, which is two thousand
 * eight hundred cells; as elements those are two thousand eight hundred nodes
 * the browser has to lay out. As a path they are one.
 *
 * Separated from the component so it can be checked on its own.
 */
export function qrGeometry(value: string): { path: string; extent: number; modules: number } {
  const code = qrcode(0, 'M');

  code.addData(value);
  code.make();

  const modules = code.getModuleCount();

  // The quiet zone. Four modules of clear space on every side, and it is part
  // of the specification rather than padding: a scanner finds the symbol by its
  // border, and a QR drawn flush against anything else is one many readers
  // refuse outright.
  const quietZone = 4;

  let path = '';

  for (let row = 0; row < modules; row += 1) {
    for (let column = 0; column < modules; column += 1) {
      if (code.isDark(row, column)) {
        path += `M${column + quietZone} ${row + quietZone}h1v1h-1z`;
      }
    }
  }

  return { path, extent: modules + quietZone * 2, modules };
}

/** The square, rendered. */
export function QrCode({
  value,
  label,
  size = 200,
}: {
  value: string;

  /**
   * What the square is, for anybody who cannot see it. An image with no
   * alternative text is an image that does not exist to a screen reader, and
   * the text beside it — the key in full — is what they will use instead.
   */
  label: string;

  /** Rendered edge length in pixels. The square scales; the modules do not. */
  size?: number;
}) {
  const { path, extent } = qrGeometry(value);

  return (
    <svg
      role="img"
      aria-label={label}
      viewBox={`0 0 ${extent} ${extent}`}
      width={size}
      height={size}
      // Sharp edges. A module blurred across a pixel boundary is a module a
      // camera has to guess at.
      shapeRendering="crispEdges"
      className="rounded-sm border border-border bg-surface"
    >
      {/* White under the whole square, including the quiet zone, so the code
          survives a dark page background and a printed page alike. */}
      <rect width={extent} height={extent} fill="#ffffff" />

      {/* Literal black and white, and deliberately not the design tokens.
          Everything else on this screen should follow the palette; this should
          not. Contrast here is not a preference but the thing a camera needs,
          and a scanner that cannot separate the modules from the ground does
          not report a poor reading — it reports nothing at all. Tying these two
          values to tokens would mean a future palette change could stop
          authenticator apps working, silently, for everyone setting up. */}
      <path d={path} fill="#141a21" />
    </svg>
  );
}

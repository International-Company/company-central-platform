/**
 * A size in bytes, rounded to something a person reads.
 *
 * The Platform sends a 64-bit integer, which the generated types express as
 * `number | string` — JavaScript cannot hold every `long` exactly, so the
 * generator refuses to promise it can. A document size is nowhere near that
 * boundary, and coercing here is the whole of the handling it needs.
 *
 * In its own file rather than beside the screen that first needed it: the
 * detail panel needs it too, and importing it from the screen would make the
 * two components import each other.
 */
export function inKilobytes(sizeInBytes: number | string): string {
  return `${Math.max(1, Math.round(Number(sizeInBytes) / 1024))} KB`;
}

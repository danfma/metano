/** What the probes need from the runtime they run on (Bun or Node). */

declare const Bun: { readonly version: string; gc(force: boolean): void } | undefined;

const isBun = typeof Bun !== "undefined";

export function runtimeName(): string {
  return isBun ? `bun ${Bun!.version}` : `node ${process.version}`;
}

/** A full collection; Node must run with `--expose-gc`. */
export function collectGarbage(): void {
  if (isBun) {
    Bun!.gc(true);
    return;
  }

  const gc = (globalThis as { gc?: () => void }).gc;

  if (!gc) {
    throw new Error("Run Node with --expose-gc.");
  }

  gc();
}

/** The process's peak resident set size so far, in MB (`maxRSS` is in KB on both runtimes). */
export function peakRssMb(): number {
  return Math.round(process.resourceUsage().maxRSS / 1024);
}

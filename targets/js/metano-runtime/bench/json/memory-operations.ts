/**
 * The operations `memory-probe.ts` measures, shared with `run.ts` without
 * importing the probe, which runs its measurement when it loads.
 */
export const MEMORY_OPERATIONS = ["read", "read-text", "read-full", "write"] as const;

export type MemoryOperation = (typeof MEMORY_OPERATIONS)[number];

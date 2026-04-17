/**
 * Port of System.HashCode from .NET (xxHash32-based).
 * Used by Metano-generated record classes for value-based equality.
 *
 * Reference: https://github.com/dotnet/corert/blob/master/src/System.Private.CoreLib/shared/System/HashCode.cs
 * Algorithm: xxHash32 by Yann Collet (BSD 2-Clause)
 */

const Prime1 = 0x9e3779b1;
const Prime2 = 0x85ebca77;
const Prime3 = 0xc2b2ae3d;
const Prime4 = 0x27d4eb2f;
const Prime5 = 0x165667b1;

// Fixed seed (deterministic — no random seed like .NET since we need consistent hashing)
const Seed = 0;

function rotateLeft(value: number, count: number): number {
  return (value << count) | (value >>> (32 - count)) | 0;
}

function round(hash: number, input: number): number {
  hash = (hash + Math.imul(input, Prime2)) | 0;
  hash = rotateLeft(hash, 13);
  hash = Math.imul(hash, Prime1);
  return hash;
}

function queueRound(hash: number, queuedValue: number): number {
  hash = (hash + Math.imul(queuedValue, Prime3)) | 0;
  hash = rotateLeft(hash, 17);
  hash = Math.imul(hash, Prime4);
  return hash;
}

function mixState(v1: number, v2: number, v3: number, v4: number): number {
  const hash = rotateLeft(v1, 1) + rotateLeft(v2, 7) + rotateLeft(v3, 12) + rotateLeft(v4, 18);
  return hash | 0;
}

function mixFinal(hash: number): number {
  hash = (hash ^ (hash >>> 15)) | 0;
  hash = Math.imul(hash, Prime2);
  hash = (hash ^ (hash >>> 13)) | 0;
  hash = Math.imul(hash, Prime3);
  hash = (hash ^ (hash >>> 16)) | 0;
  return hash;
}

function mixEmptyState(): number {
  return (Seed + Prime5) | 0;
}

/**
 * Computes a hash code for a single value.
 */
function hashValue(value: unknown): number {
  if (value === null || value === undefined) return 0;

  if (typeof value === "number") {
    // For integers, use the value directly. For floats, use bit representation.
    if (Number.isInteger(value)) return value | 0;
    // Float: use DataView to get bit pattern
    const buf = new ArrayBuffer(8);
    new DataView(buf).setFloat64(0, value);
    const dv = new DataView(buf);
    return dv.getInt32(0) ^ dv.getInt32(4);
  }

  if (typeof value === "string") {
    let hash = 0;
    for (let i = 0; i < value.length; i++) {
      hash = Math.imul(hash, 31) + value.charCodeAt(i);
      hash = hash | 0;
    }
    return hash;
  }

  if (typeof value === "boolean") return value ? 1 : 0;

  if (typeof value === "bigint") return Number(value & 0xffffffffn) | 0;

  // Objects with hashCode method (other Metano records)
  if (
    typeof value === "object" &&
    value !== null &&
    "hashCode" in value &&
    typeof value.hashCode === "function"
  ) {
    return (value as { hashCode(): number }).hashCode();
  }

  return 0;
}

/**
 * Accumulator for computing hash codes, modeled after System.HashCode.
 *
 * Usage:
 * ```ts
 * const hc = new HashCode();
 * hc.add(this.x);
 * hc.add(this.y);
 * return hc.toHashCode();
 * ```
 */
export class HashCode {
  private _v1: number;
  private _v2: number;
  private _v3: number;
  private _v4: number;
  private _queue1 = 0;
  private _queue2 = 0;
  private _queue3 = 0;
  private _length = 0;

  constructor() {
    this._v1 = (Seed + Prime1 + Prime2) | 0;
    this._v2 = (Seed + Prime2) | 0;
    this._v3 = Seed;
    this._v4 = (Seed - Prime1) | 0;
  }

  add(value: unknown): void {
    const hc = hashValue(value);
    const pos = this._length % 4;
    this._length++;

    switch (pos) {
      case 0:
        this._queue1 = hc;
        break;
      case 1:
        this._queue2 = hc;
        break;
      case 2:
        this._queue3 = hc;
        break;
      case 3:
        this._v1 = round(this._v1, this._queue1);
        this._v2 = round(this._v2, this._queue2);
        this._v3 = round(this._v3, this._queue3);
        this._v4 = round(this._v4, hc);
        break;
    }
  }

  toHashCode(): number {
    const length = this._length;

    let hash: number;
    if (length >= 4) {
      hash = mixState(this._v1, this._v2, this._v3, this._v4);
    } else {
      hash = mixEmptyState();
    }

    hash = (hash + length * 4) | 0;

    const remaining = length % 4;
    if (remaining >= 1) hash = queueRound(hash, this._queue1);
    if (remaining >= 2) hash = queueRound(hash, this._queue2);
    if (remaining >= 3) hash = queueRound(hash, this._queue3);

    hash = mixFinal(hash);
    return hash;
  }

  // ─── Static combine helpers ───────────────────────────────

  static combine(v1: unknown): number {
    const hc = new HashCode();
    hc.add(v1);
    return hc.toHashCode();
  }

  static combine2(v1: unknown, v2: unknown): number {
    const hc = new HashCode();
    hc.add(v1);
    hc.add(v2);
    return hc.toHashCode();
  }

  static combine3(v1: unknown, v2: unknown, v3: unknown): number {
    const hc = new HashCode();
    hc.add(v1);
    hc.add(v2);
    hc.add(v3);
    return hc.toHashCode();
  }

  static combine4(v1: unknown, v2: unknown, v3: unknown, v4: unknown): number {
    const hc = new HashCode();
    hc.add(v1);
    hc.add(v2);
    hc.add(v3);
    hc.add(v4);
    return hc.toHashCode();
  }
}

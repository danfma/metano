import { HashCode, copyRecordWith } from "metano-runtime";

class ShotSchedule$ShotEntry {
  declare readonly name: string;
  declare readonly frames: number;

  constructor(name: string, frames: number) {
    this.name = name;
    this.frames = frames;
  }

  static empty(): ShotSchedule$ShotEntry {
    return new ShotSchedule$ShotEntry("", 0);
  }

  twice(): ShotSchedule$ShotEntry {
    return ShotSchedule$ShotEntry.empty().with({ frames: Math.imul(this.frames, 2) });
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.name === other.name && this.frames === other.frames;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.name);
    hc.add(this.frames);

    return hc.toHashCode();
  }

  with(overrides?: Partial<ShotSchedule$ShotEntry>): ShotSchedule$ShotEntry {
    return copyRecordWith<ShotSchedule$ShotEntry>(this, overrides);
  }

  toString(): string {
    return `ShotEntry { Name = ${this.name ?? ""}, Frames = ${this.frames} }`;
  }
}

const ShotSchedule$Kind = {
  A: 0,
  B: 1,
} as const;

type ShotSchedule$Kind = typeof ShotSchedule$Kind[keyof typeof ShotSchedule$Kind];

class ShotSchedule$Slot {
  declare readonly index: number;

  constructor(index: number) {
    this.index = index;
  }

  static create(): ShotSchedule$Slot {
    return new ShotSchedule$Slot(7);
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.index === other.index;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.index);

    return hc.toHashCode();
  }

  with(overrides?: Partial<ShotSchedule$Slot>): ShotSchedule$Slot {
    return copyRecordWith<ShotSchedule$Slot>(this, overrides);
  }

  toString(): string {
    return `Slot { Index = ${this.index} }`;
  }
}

class ShotSchedule$Box<T> {
  declare readonly value: T;

  constructor(value: T) {
    this.value = value;
  }
}

class ShotSchedule$Deep {
  private constructor() { }

  static make(): ShotSchedule$Deep$Leaf {
    return new ShotSchedule$Deep$Leaf(5);
  }
}

class ShotSchedule$Deep$Leaf {
  declare readonly n: number;

  constructor(n: number) {
    this.n = n;
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.n === other.n;
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.n);

    return hc.toHashCode();
  }

  with(overrides?: Partial<ShotSchedule$Deep$Leaf>): ShotSchedule$Deep$Leaf {
    return copyRecordWith<ShotSchedule$Deep$Leaf>(this, overrides);
  }

  toString(): string {
    return `Leaf { N = ${this.n} }`;
  }
}

export class ShotSchedule {
  private constructor() { }

  /** @internal */
  static get Kind(): typeof ShotSchedule$Kind {
    return ShotSchedule$Kind;
  }

  static get Slot(): typeof ShotSchedule$Slot {
    return ShotSchedule$Slot;
  }

  static get Box(): typeof ShotSchedule$Box {
    return ShotSchedule$Box;
  }

  static total(name: string): number {
    const entry = new ShotSchedule$ShotEntry(name, 3);
    const kind = ShotSchedule$Kind.B as ShotSchedule$Kind;
    const box = new ShotSchedule$Box(entry);
    const boxed = entry;
    const isEntry = boxed instanceof ShotSchedule$ShotEntry ? 1 : 0;

    return (box.value.frames + (kind === ShotSchedule$Kind.B ? 1 : 0) + ShotSchedule$ShotEntry.empty().frames + isEntry + ShotSchedule$Slot.create().index + ShotSchedule$Deep.make().n + new ShotSchedule$Deep$Leaf(1).n + entry.twice().frames) | 0;
  }

  /** @internal */
  static kindValue(kind: ShotSchedule$Kind = ShotSchedule$Kind.B): number {
    return kind;
  }

  static first(): ShotSchedule$Slot {
    return new ShotSchedule$Slot(0);
  }
}

export declare namespace ShotSchedule {
  /** @internal */
  export type Kind = ShotSchedule$Kind;

  export type Slot = ShotSchedule$Slot;

  export type Box<T> = ShotSchedule$Box<T>;
}

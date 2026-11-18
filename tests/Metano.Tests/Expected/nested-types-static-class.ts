import { HashCode, copyRecordWith } from "metano-runtime";

class ShotSchedule$ShotEntry {
  constructor(readonly name: string, readonly frames: number) { }

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
}

const ShotSchedule$Kind = {
  A: 0,
  B: 1,
} as const;

type ShotSchedule$Kind = typeof ShotSchedule$Kind[keyof typeof ShotSchedule$Kind];

class ShotSchedule$Slot {
  constructor(readonly index: number) { }

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
}

class ShotSchedule$Box<T> {
  constructor(readonly value: T) {
    this.value = value;
  }
}

class ShotSchedule$Deep {
  constructor() { }

  static make(): ShotSchedule$Deep$Leaf {
    return new ShotSchedule$Deep$Leaf(5);
  }
}

class ShotSchedule$Deep$Leaf {
  constructor(readonly n: number) { }

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
}

export class ShotSchedule {
  constructor() { }

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
  export type Kind = ShotSchedule$Kind;

  export type Slot = ShotSchedule$Slot;

  export type Box<T> = ShotSchedule$Box<T>;
}

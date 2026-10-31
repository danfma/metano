import { HashCode, copyRecordWith } from "metano-runtime";

export class ShotSchedule {
  constructor() { }

  static total(name: string): number {
    const entry = new ShotSchedule.ShotEntry(name, 3);
    const kind = ShotSchedule.Kind.B;
    const box = new ShotSchedule.Box(entry);
    const boxed = entry;
    const isEntry = boxed instanceof ShotSchedule.ShotEntry ? 1 : 0;

    return box.value.frames + (kind === ShotSchedule.Kind.B ? 1 : 0) + ShotSchedule.ShotEntry.empty().frames + isEntry + ShotSchedule.Slot.create().index + ShotSchedule.Deep.make().n + new ShotSchedule.Deep.Leaf(1).n + entry.twice().frames;
  }

  static kindValue(kind: ShotSchedule.Kind = ShotSchedule.Kind.B): number {
    return kind;
  }

  static first(): ShotSchedule.Slot {
    return new ShotSchedule.Slot(0);
  }
}

export namespace ShotSchedule {
  export class ShotEntry {
    constructor(readonly name: string, readonly frames: number) { }

    static empty(): ShotSchedule.ShotEntry {
      return new ShotSchedule.ShotEntry("", 0);
    }

    twice(): ShotSchedule.ShotEntry {
      return ShotSchedule.ShotEntry.empty().with({ frames: this.frames * 2 });
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

    with(overrides?: Partial<ShotEntry>): ShotEntry {
      return copyRecordWith<ShotEntry>(this, overrides);
    }
  }

  export enum Kind {
    A = 0,
    B = 1,
  }

  export class Slot {
    constructor(readonly index: number) { }

    static create(): ShotSchedule.Slot {
      return new ShotSchedule.Slot(7);
    }

    equals(other: any): boolean {
      return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.index === other.index;
    }

    hashCode(): number {
      const hc = new HashCode();
      hc.add(this.index);

      return hc.toHashCode();
    }

    with(overrides?: Partial<Slot>): Slot {
      return copyRecordWith<Slot>(this, overrides);
    }
  }

  export class Box<T> {
    constructor(readonly value: T) {
      this.value = value;
    }
  }

  export class Deep {
    constructor() { }

    static make(): ShotSchedule.Deep.Leaf {
      return new ShotSchedule.Deep.Leaf(5);
    }
  }

  export namespace Deep {
    export class Leaf {
      constructor(readonly n: number) { }

      equals(other: any): boolean {
        return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this) && this.n === other.n;
      }

      hashCode(): number {
        const hc = new HashCode();
        hc.add(this.n);

        return hc.toHashCode();
      }

      with(overrides?: Partial<Leaf>): Leaf {
        return copyRecordWith<Leaf>(this, overrides);
      }
    }
  }
}

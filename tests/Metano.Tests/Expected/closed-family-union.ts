// fam/command.ts
import { HashCode, copyRecordWith } from "metano-runtime";
import type { Custom } from "./custom.js";
import { Halt } from "./halt.js";
import type { Play } from "./play.js";
import { Reader } from "./reader.js";
import type { Seek } from "./seek.js";

abstract class Command$Base {
  abstract readonly kind: "Play" | "Halt" | "SeekTo" | "SeekBy" | "Custom";

  constructor() { }

  static default(): Command {
    return new Halt();
  }

  self(): Command {
    return this as unknown as Command;
  }

  describe(): string {
    return Reader.name(this as unknown as Command);
  }

  protected rank(): number {
    return 0;
  }

  rankOf(other: Command): number {
    return (other as Command$Base).rank();
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this);
  }

  hashCode(): number {
    const hc = new HashCode();

    return hc.toHashCode();
  }

  with(overrides?: Partial<Command>): Command {
    return copyRecordWith<Command>(this as unknown as Command, overrides);
  }

  protected $printMembers(): string {
    return "";
  }

  toString(): string {
    const members = this.$printMembers();

    return members ? `Command { ${members} }` : "Command { }";
  }
}

export type Command = Play | Halt | Seek | Custom;

export const Command: typeof Command$Base = Command$Base;

// fam/seek.ts
import { HashCode, copyRecordWith } from "metano-runtime";
import { Command } from "./command.js";
import type { SeekBy } from "./seek-by.js";
import type { SeekTo } from "./seek-to.js";

abstract class Seek$Base extends Command {
  abstract override readonly kind: "SeekTo" | "SeekBy";

  constructor() {
    super();
  }

  override equals(other: any): boolean {
    return super.equals(other);
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());

    return hc.toHashCode();
  }

  override with(overrides?: Partial<Seek>): Seek {
    return copyRecordWith<Seek>(this as unknown as Seek, overrides);
  }

  protected override $printMembers(): string {
    return super.$printMembers?.() ?? "";
  }

  override toString(): string {
    const members = this.$printMembers();

    return members ? `Seek { ${members} }` : "Seek { }";
  }
}

export type Seek = SeekTo | SeekBy;

export const Seek: typeof Seek$Base = Seek$Base;

// fam/seek-by.ts
import { HashCode, copyRecordWith } from "metano-runtime";
import { Seek } from "./seek.js";

export class SeekBy extends Seek {
  constructor(readonly delta: number) {
    super();
  }

  override get kind(): "SeekBy" {
    return "SeekBy";
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.delta === other.delta;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.delta);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<SeekBy>): SeekBy {
    return copyRecordWith<SeekBy>(this, overrides);
  }

  protected override $printMembers(): string {
    const base = super.$printMembers?.() ?? "";

    return `${base ? `${base}, ` : ""}Delta = ${this.delta}`;
  }

  override toString(): string {
    return `SeekBy { ${this.$printMembers()} }`;
  }
}

// fam/seek-by-twice.ts
import { HashCode, copyRecordWith } from "metano-runtime";
import { SeekBy } from "./seek-by.js";

export class SeekByTwice extends SeekBy {
  constructor(delta: number) {
    super(delta);
  }

  override equals(other: any): boolean {
    return super.equals(other);
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());

    return hc.toHashCode();
  }

  override with(overrides?: Partial<SeekByTwice>): SeekByTwice {
    return copyRecordWith<SeekByTwice>(this, overrides);
  }

  protected override $printMembers(): string {
    return super.$printMembers?.() ?? "";
  }

  override toString(): string {
    const members = this.$printMembers();

    return members ? `SeekByTwice { ${members} }` : "SeekByTwice { }";
  }
}

// fam/halt.ts
import { HashCode, copyRecordWith } from "metano-runtime";
import { Command } from "./command.js";

export class Halt extends Command {
  constructor() {
    super();
  }

  override get kind(): "Halt" {
    return "Halt";
  }

  override equals(other: any): boolean {
    return super.equals(other);
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());

    return hc.toHashCode();
  }

  override with(overrides?: Partial<Halt>): Halt {
    return copyRecordWith<Halt>(this, overrides);
  }

  protected override $printMembers(): string {
    return super.$printMembers?.() ?? "";
  }

  override toString(): string {
    const members = this.$printMembers();

    return members ? `Stop { ${members} }` : "Stop { }";
  }
}

// fam/custom.ts
import { HashCode, copyRecordWith } from "metano-runtime";
import { Command } from "./command.js";

export abstract class Custom extends Command {
  constructor() {
    super();
  }

  override get kind(): "Custom" {
    return "Custom";
  }

  override equals(other: any): boolean {
    return super.equals(other);
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());

    return hc.toHashCode();
  }

  override with(overrides?: Partial<Custom>): Custom {
    return copyRecordWith<Custom>(this, overrides);
  }

  protected override $printMembers(): string {
    return super.$printMembers?.() ?? "";
  }

  override toString(): string {
    const members = this.$printMembers();

    return members ? `Custom { ${members} }` : "Custom { }";
  }
}

// fam/macro.ts
import { HashCode, copyRecordWith } from "metano-runtime";
import { Custom } from "./custom.js";

export class Macro extends Custom {
  constructor(readonly name: string) {
    super();
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.name === other.name;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.name);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<Macro>): Macro {
    return copyRecordWith<Macro>(this, overrides);
  }

  protected override $printMembers(): string {
    const base = super.$printMembers?.() ?? "";

    return `${base ? `${base}, ` : ""}Name = ${this.name ?? ""}`;
  }

  override toString(): string {
    return `Macro { ${this.$printMembers()} }`;
  }
}

// fam/tree.ts
import { HashCode, copyRecordWith } from "metano-runtime";
import type { IntLeaf } from "./int-leaf.js";
import type { Leaf } from "./leaf.js";
import type { Node } from "./node.js";

abstract class Tree$Base<T> {
  abstract readonly kind: "Leaf" | "Node" | "IntLeaf";

  constructor() { }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this);
  }

  hashCode(): number {
    const hc = new HashCode();

    return hc.toHashCode();
  }

  with(overrides?: Partial<Tree<T>>): Tree<T> {
    return copyRecordWith<Tree<T>>(this as unknown as Tree<T>, overrides);
  }

  protected $printMembers(): string {
    return "";
  }

  toString(): string {
    const members = this.$printMembers();

    return members ? `Tree { ${members} }` : "Tree { }";
  }
}

export type Tree<T> = Leaf<T> | Node<T> | ([T] extends [number] ? IntLeaf : never);

export const Tree: typeof Tree$Base = Tree$Base;

// fam/int-leaf.ts
import { HashCode, copyRecordWith } from "metano-runtime";
import { Tree } from "./tree.js";

export class IntLeaf extends Tree<number> {
  constructor(readonly number: number) {
    super();
  }

  override get kind(): "IntLeaf" {
    return "IntLeaf";
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.number === other.number;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.number);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<IntLeaf>): IntLeaf {
    return copyRecordWith<IntLeaf>(this, overrides);
  }

  protected override $printMembers(): string {
    const base = super.$printMembers?.() ?? "";

    return `${base ? `${base}, ` : ""}Number = ${this.number}`;
  }

  override toString(): string {
    return `IntLeaf { ${this.$printMembers()} }`;
  }
}

// fam/take.ts
import { HashCode, copyRecordWith } from "metano-runtime";

abstract class Take$Base {
  abstract readonly kind: "Fresh" | "Reused";

  constructor() { }

  static get Fresh(): typeof Take$Fresh {
    return Take$Fresh;
  }

  static get Reused(): typeof Take$Reused {
    return Take$Reused;
  }

  equals(other: any): boolean {
    return other != null && Object.getPrototypeOf(other) === Object.getPrototypeOf(this);
  }

  hashCode(): number {
    const hc = new HashCode();

    return hc.toHashCode();
  }

  with(overrides?: Partial<Take>): Take {
    return copyRecordWith<Take>(this as unknown as Take, overrides);
  }

  protected $printMembers(): string {
    return "";
  }

  toString(): string {
    const members = this.$printMembers();

    return members ? `Take { ${members} }` : "Take { }";
  }
}

export declare namespace Take {
  export type Fresh = Take$Fresh;

  export type Reused = Take$Reused;
}

export type Take = Take$Fresh | Take$Reused;

export const Take: typeof Take$Base = Take$Base;

class Take$Fresh extends Take {
  constructor() {
    super();
  }

  override get kind(): "Fresh" {
    return "Fresh";
  }

  override equals(other: any): boolean {
    return super.equals(other);
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());

    return hc.toHashCode();
  }

  override with(overrides?: Partial<Take$Fresh>): Take$Fresh {
    return copyRecordWith<Take$Fresh>(this, overrides);
  }

  protected override $printMembers(): string {
    return super.$printMembers?.() ?? "";
  }

  override toString(): string {
    const members = this.$printMembers();

    return members ? `Fresh { ${members} }` : "Fresh { }";
  }
}

class Take$Reused extends Take {
  constructor(readonly from: number) {
    super();
  }

  override get kind(): "Reused" {
    return "Reused";
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.from === other.from;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.from);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<Take$Reused>): Take$Reused {
    return copyRecordWith<Take$Reused>(this, overrides);
  }

  protected override $printMembers(): string {
    const base = super.$printMembers?.() ?? "";

    return `${base ? `${base}, ` : ""}From = ${this.from}`;
  }

  override toString(): string {
    return `Reused { ${this.$printMembers()} }`;
  }
}

// fam/reader.ts
import { SwitchExpressionException } from "metano-runtime/system/exceptions/switch-expression-exception";
import type { Command } from "./command.js";
import { Custom } from "./custom.js";
import { Halt } from "./halt.js";
import { Play } from "./play.js";
import { SeekBy } from "./seek-by.js";
import { SeekTo } from "./seek-to.js";
import { Seek } from "./seek.js";

export class Reader {
  constructor() { }

  static name(command: Command): string {
    {
      let $s = command;

      if ($s instanceof Play) {
        const play = $s;

        return play.clip;
      }

      if ($s instanceof Halt) {
        return "stop";
      }

      if ($s instanceof SeekTo) {
        const to = $s;

        return "to " + to.position;
      }

      if ($s instanceof SeekBy) {
        const by = $s;

        return "by " + by.delta;
      }

      if ($s instanceof Custom) {
        return "custom";
      }

      throw new SwitchExpressionException($s);
    }
  }

  static seekOf(value: Object): number {
    let seek: Seek;

    return value instanceof Seek && (seek = value as unknown as Seek, true) ? Reader.name(seek).length : -1;
  }

  static asSeek(value: Object): Seek | null {
    return value instanceof Seek ? value as unknown as Seek : null;
  }

  static statement(value: Object): string {
    switch (true) {
      case value instanceof Seek:
        {
          const seek = value as unknown as Seek;

          return Reader.name(seek);
        }
      default:
        return "";
    }
  }
}

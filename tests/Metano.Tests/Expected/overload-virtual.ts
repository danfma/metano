import { isInt32, isString } from "metano-runtime";

export class Base {
  constructor() { }

  addX(x: number): number {
    return x;
  }

  addS(s: string): number {
    return s.length;
  }

  add(x: number): number;
  add(s: string): number;
  add(...args: unknown[]): number {
    if (args.length === 1 && isInt32(args[0])) {
      return this.addX(args[0] as number);
    }

    if (args.length === 1 && isString(args[0])) {
      return this.addS(args[0] as string);
    }

    throw new Error("No matching overload for add");
  }
}

import { Base } from "./base";

export class Partial extends Base {
  constructor() {
    super();
  }

  override addX(x: number): number {
    return super.addX(x) * 2;
  }

  twice(x: number): number {
    return super.addX(x) + this.add(x);
  }
}

import { Base } from "./base";

export class Full extends Base {
  constructor() {
    super();
  }

  override addX(x: number): number {
    return x + 1;
  }

  override addS(s: string): number {
    return super.addS(s) + 1;
  }
}

import { isInt32 } from "metano-runtime";
import { Base } from "./base";

export class Hider extends Base {
  constructor() {
    super();
  }

  addX$(x: number): number {
    return 100;
  }

  override add(s: string): number;
  override add(x: number): number;
  override add(...args: unknown[]): number {
    if (args.length === 1 && isInt32(args[0])) {
      return this.addX$(args[0] as number);
    }

    return super.add(...args as [never]);
  }
}

import { isInt32, isString } from "metano-runtime";

export class Ledger {
  constructor() { }

  addX(x: number): number {
    return x;
  }

  private addS(s: string): number {
    return s.length + 1000;
  }

  add(x: number): number;
  /** @internal */
  add(s: string): number;
  add(...args: unknown[]): number {
    if (args.length === 1 && isInt32(args[0])) {
      return this.addX(args[0] as number);
    }

    if (args.length === 1 && isString(args[0])) {
      return this.addS(args[0] as string);
    }

    throw new Error("No matching overload for add");
  }

  count(s: string): number {
    return this.add(s);
  }
}

import { isBool } from "metano-runtime";
import { Ledger } from "./ledger";

export class Flagged extends Ledger {
  constructor() {
    super();
  }

  addFlag(flag: boolean): number {
    return flag ? 1 : 0;
  }

  override add(x: number): number;
  /** @internal */
  override add(s: string): number;
  override add(flag: boolean): number;
  override add(...args: unknown[]): number {
    if (args.length === 1 && isBool(args[0])) {
      return this.addFlag(args[0] as boolean);
    }

    return super.add(...args as [never]);
  }
}

import { isInt16 } from "metano-runtime";
import { Ledger } from "./ledger";

export class Shorty extends Ledger {
  constructor() {
    super();
  }

  addS$(s: number): number {
    return 7;
  }

  override add(x: number): number;
  /** @internal */
  override add(s: string): number;
  override add(s: number): number;
  override add(...args: unknown[]): number {
    if (args.length === 1 && isInt16(args[0])) {
      return this.addS$(args[0] as number);
    }

    return super.add(...args as [never]);
  }
}

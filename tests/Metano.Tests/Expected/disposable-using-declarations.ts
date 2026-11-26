import type { IDisposable } from "metano-runtime";
import { Handle } from "./handle.js";
import { Res } from "./res.js";
import type { SRes } from "./s-res.js";

export class Uses {
  private constructor() { }

  static open(): Res {
    return new Res();
  }

  static touch(r: Res | null): void {
  }

  static a(): number {
    using r = new Res();

    return r.count;
  }

  static b(maybe: Res | null): void {
    {
      using r = maybe;
      Uses.touch(r);
    }

    Uses.touch(null);
  }

  static c(): void {
    {
      using $using0 = Uses.open();
      Uses.touch(null);
    }
  }

  static d(): void {
    {
      using a = new Res();
      using b = new Res();
      a.count = (a.count + 1) | 0;
    }
  }

  static e(): void {
    using a = new Res();
    using b = new Handle();
    Uses.touch(a);
  }

  static f(s: SRes, ns: SRes | null): void {
    {
      using $using0 = s;
    }

    {
      using $using1 = ns;
    }
  }

  static g<T extends IDisposable>(t: T): void {
    {
      using $using0 = t;
    }
  }

  static k(): void {
    {
      using a = new Res();
      using b = new Res();
      a.count = (a.count + 1) | 0;
    }
  }
}

import type { IDisposable } from "metano-runtime";
import { Handle } from "./handle.js";
import { Res } from "./res.js";
import type { SRes } from "./s-res.js";

export class Uses {
  constructor() { }

  static open(): Res {
    return new Res();
  }

  static touch(r: Res | null): void {
  }

  static a(): number {
    const r = new Res();
    try {
      return r.count;
    } finally {
      r.dispose();
    }
  }

  static b(maybe: Res | null): void {
    {
      const r = maybe;
      try {
        Uses.touch(r);
      } finally {
        r?.dispose();
      }
    }

    Uses.touch(null);
  }

  static c(): void {
    {
      const $using0 = Uses.open();
      try {
        Uses.touch(null);
      } finally {
        $using0?.dispose();
      }
    }
  }

  static d(): void {
    {
      const a = new Res();
      try {
        const b = new Res();
        try {
          a.count = (a.count + 1) | 0;
        } finally {
          b.dispose();
        }
      } finally {
        a.dispose();
      }
    }
  }

  static e(): void {
    const a = new Res();
    try {
      const b = new Handle();
      try {
        Uses.touch(a);
      } finally {
        b.dispose();
      }
    } finally {
      a.dispose();
    }
  }

  static f(s: SRes, ns: SRes | null): void {
    {
      const $using0 = s;
      try {
      } finally {
        $using0.dispose();
      }
    }

    {
      const $using1 = ns;
      try {
      } finally {
        $using1?.dispose();
      }
    }
  }

  static g<T extends IDisposable>(t: T): void {
    {
      const $using0 = t;
      try {
      } finally {
        $using0?.dispose();
      }
    }
  }

  static k(): void {
    {
      const a = new Res();
      try {
        const b = new Res();
        try {
          a.count = (a.count + 1) | 0;
        } finally {
          b.dispose();
        }
      } finally {
        a.dispose();
      }
    }
  }
}

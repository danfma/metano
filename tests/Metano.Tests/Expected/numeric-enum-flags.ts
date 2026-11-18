export const Perms = {
  None: 0,
  Read: 1,
  Write: 2,
  Execute: 4,
} as const;

export type Perms = number & { readonly __enum?: "Perms" };

export class Access {
  constructor() { }

  static grant(): Perms {
    let p = Perms.Read | Perms.Write;
    p |= Perms.Execute;
    p &= ~Perms.Read;

    return p ^ Perms.Write;
  }

  static canWrite(p: Perms): boolean {
    return (p & Perms.Write) !== 0;
  }

  static zero(): Perms {
    return Perms.None;
  }

  static cast(n: number): Perms {
    return n;
  }

  static three(): Perms {
    return 3;
  }

  static all(): Perms[] {
    return [Perms.Read, Perms.Write, Perms.Execute];
  }

  static describe(p: Perms): string {
    return p === Perms.None ? "none" : p === Perms.Read ? "read" : "mixed";
  }
}

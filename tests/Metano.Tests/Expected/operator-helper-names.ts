import { checkedInt32, isInt32 } from "metano-runtime";

export class Meters {
  v: number = 0;

  constructor(v: number) {
    this.v = v;
  }

  static __negate(a: Meters): Meters {
    return new Meters(-a.v);
  }

  $negate(): Meters {
    return Meters.__negate(this);
  }

  static __plus(a: Meters): Meters {
    return a;
  }

  $plus(): Meters {
    return Meters.__plus(this);
  }

  static __subtract(a: Meters, b: Meters): Meters {
    return new Meters(a.v - b.v);
  }

  $subtract(b: Meters): Meters {
    return Meters.__subtract(this, b);
  }

  static __add(a: number, m: Meters): Meters {
    return new Meters(a + m.v);
  }

  static __increment(a: Meters): Meters {
    return new Meters(a.v + 1);
  }

  $increment(): Meters {
    return Meters.__increment(this);
  }

  static __decrement(a: Meters): Meters {
    return new Meters(a.v - 1);
  }

  $decrement(): Meters {
    return Meters.__decrement(this);
  }

  static __checkedSubtract(a: Meters, b: Meters): Meters {
    return new Meters(checkedInt32(a.v - b.v));
  }

  $checkedSubtract(b: Meters): Meters {
    return Meters.__checkedSubtract(this, b);
  }

  static __shiftRightUnsigned(a: Meters, n: number): Meters {
    return new Meters((a.v >>> n) | 0);
  }

  $shiftRightUnsigned(n: number): Meters {
    return Meters.__shiftRightUnsigned(this, n);
  }

  static __multiplyMetersInt32(a: Meters, k: number): Meters {
    return new Meters(a.v * k);
  }

  static __multiplyInt32Meters(k: number, a: Meters): Meters {
    return new Meters(a.v * k * 10);
  }

  static __multiply(a: Meters, k: number): Meters;
  static __multiply(k: number, a: Meters): Meters;
  static __multiply(...args: unknown[]): Meters {
    if (args.length === 2 && args[0] instanceof Meters && isInt32(args[1])) {
      return Meters.__multiplyMetersInt32(args[0] as Meters, args[1] as number);
    }

    if (args.length === 2 && isInt32(args[0]) && args[1] instanceof Meters) {
      return Meters.__multiplyInt32Meters(args[0] as number, args[1] as Meters);
    }

    throw new Error("No matching overload for multiply");
  }

  $multiply(k: number): Meters;
  $multiply(...args: unknown[]): Meters {
    if (args.length === 1 && isInt32(args[0])) {
      return Meters.__multiplyMetersInt32(this, args[0] as number);
    }

    throw new Error("No matching overload for multiply");
  }
}

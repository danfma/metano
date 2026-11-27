import { Int32Type } from "metano-runtime";
import { Tagged } from "./tagged.js";
import { Typed } from "./typed.js";

export class Uses {
  declare private _field: Typed;

  constructor(t: Typed) {
    {
      const handler$temp$0 = new Typed(5, 1);
      handler$temp$0.appendLiteral("field");
      handler$temp$0.appendFormattedValue<number>(1, Int32Type);
      this._field = handler$temp$0;
    }
  }

  ret(n: number): Typed {
    const handler$temp$0 = new Typed(1, 1);
    handler$temp$0.appendLiteral("r");
    handler$temp$0.appendFormattedValue<number>(n, Int32Type);

    return handler$temp$0;
  }

  lam(): (arg: number) => Typed {
    return (n: number) => {
      const handler$temp$0 = new Typed(1, 1);
      handler$temp$0.appendLiteral("l");
      handler$temp$0.appendFormattedValue<number>(n, Int32Type);

      return handler$temp$0;
    };
  }

  cond(b: boolean, n: number): Typed {
    let handler$temp$0, handler$temp$1;

    return b ? (handler$temp$0 = new Typed(1, 1), handler$temp$0.appendLiteral("a"), handler$temp$0.appendFormattedValue<number>(n, Int32Type), handler$temp$0) : (handler$temp$1 = new Typed(1, 0), handler$temp$1.appendLiteral("b"), handler$temp$1);
  }

  ctor(n: number): Uses {
    let handler$temp$0;

    return new Uses((handler$temp$0 = new Typed(1, 1), handler$temp$0.appendLiteral("c"), handler$temp$0.appendFormattedValue<number>(n, Int32Type), handler$temp$0));
  }

  generic(n: number): Tagged<number> {
    const handler$temp$0 = new Tagged(1, 1) as Tagged<number>;
    handler$temp$0.appendLiteral("g");
    handler$temp$0.appendFormatted(n);

    return handler$temp$0;
  }

  nest(n: number): void {
    let handler$temp$1, handler$temp$3;
    const handler$temp$0 = new Typed(1, 1);
    handler$temp$0.appendLiteral("o");
    handler$temp$0.appendFormattedInner((handler$temp$1 = new Typed(1, 1), handler$temp$1.appendLiteral("i"), handler$temp$1.appendFormattedValue<number>(n, Int32Type), handler$temp$1));
    const nested = handler$temp$0;
    const handler$temp$2 = new Typed(2, 2);
    handler$temp$2.appendLiteral("a");
    handler$temp$2.appendFormattedValue<number>(n, Int32Type);
    handler$temp$2.appendLiteral("b");
    handler$temp$2.appendFormattedInner((handler$temp$3 = new Typed(2, 1), handler$temp$3.appendLiteral("in"), handler$temp$3.appendFormattedValue<number>(n, Int32Type), handler$temp$3));
    const inner = handler$temp$2;
  }
}

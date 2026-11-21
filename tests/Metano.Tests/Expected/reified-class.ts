import { UnknownType, isType, type RuntimeType } from "metano-runtime";

export class Box<T> {
  declare readonly $typeArguments$Box: readonly [RuntimeType<T>];
  private readonly _initType: RuntimeType;
  readonly ctorType: RuntimeType;

  constructor($typeArgs?: readonly [RuntimeType<T>]) {
    Object.defineProperty(this, "$typeArguments$Box", { value: $typeArgs, enumerable: false });
    this._initType = ($typeArgs?.[0] ?? UnknownType);
    this.ctorType = ($typeArgs?.[0] ?? UnknownType);
  }

  get initType(): RuntimeType {
    return this._initType;
  }

  matches(o: Object): boolean {
    return isType(o, this.$typeArguments$Box[0]);
  }
}

// ---
import { Int32Type } from "metano-runtime";
import { Box } from "./box";

export function check(o: Object): boolean {
  const box = new Box<number>([Int32Type]);

  return box.matches(o) && box.ctorType === box.initType;
}

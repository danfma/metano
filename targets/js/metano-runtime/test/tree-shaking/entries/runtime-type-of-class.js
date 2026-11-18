// A class with the literal `$runtimeType` getter generated code emits, reached through
// `isType` and `getRuntimeType`.
import { getRuntimeType } from "./system/reflection/get-runtime-type.js";
import { isType } from "./system/reflection/is-type.js";

let C$rt;

class C {
  static get $runtimeType() {
    return (C$rt ??= {
      kind: "class",
      name: "C",
      fullName: "App.C",
      namespace: "App",
      ctor: C,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new C(),
      test: (v) => v instanceof C,
    });
  }
}

export function describe(value) {
  return [isType(value, C.$runtimeType), getRuntimeType(new C())];
}

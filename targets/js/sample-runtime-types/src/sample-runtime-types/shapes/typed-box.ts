/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { isType, type ClassType, type RuntimeType } from "metano-runtime";

let TypedBox$rt: ClassType<TypedBox<unknown>> | undefined;

export class TypedBox<T> {
  declare readonly $typeArguments$TypedBox: readonly [RuntimeType<T>];

  constructor($typeArgs?: readonly [RuntimeType<T>]) {
    Object.defineProperty(this, "$typeArguments$TypedBox", { value: $typeArgs, enumerable: false });
  }

  static get $runtimeType(): ClassType<TypedBox<unknown>> {
    return (TypedBox$rt ??= {
      kind: "class",
      name: "TypedBox`1",
      fullName: "SampleRuntimeTypes.Shapes.TypedBox`1",
      namespace: "SampleRuntimeTypes.Shapes",
      ctor: TypedBox,
      interfaces: [],
      typeParameters: ["T"],
      slot: "$typeArguments$TypedBox",
      isAbstract: false,
      isValueType: false,
      create: (typeArguments) =>
        new TypedBox(typeArguments as readonly [RuntimeType<unknown>] | undefined),
      test: (v: unknown): v is TypedBox<unknown> => v instanceof TypedBox,
    });
  }

  matches(value: Object): boolean {
    return isType(value, this.$typeArguments$TypedBox[0]);
  }
}

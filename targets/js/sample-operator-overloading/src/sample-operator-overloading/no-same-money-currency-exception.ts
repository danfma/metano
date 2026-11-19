/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { enumText, type ClassType } from "metano-runtime";
import { Exception } from "metano-runtime/system/exceptions/exception";
import { Currency$runtimeType, type Currency } from "./currency";

let NoSameMoneyCurrencyException$rt: ClassType<NoSameMoneyCurrencyException> | undefined;

export class NoSameMoneyCurrencyException extends Exception {
  override readonly name: string = "NoSameMoneyCurrencyException";

  constructor(expected: Currency, provided: Currency) {
    super(
      `Not same currency. Money has ${enumText(provided, Currency$runtimeType)}, but expected ${enumText(expected, Currency$runtimeType)}`,
    );
  }

  static override get $runtimeType(): ClassType<NoSameMoneyCurrencyException> {
    return (NoSameMoneyCurrencyException$rt ??= {
      kind: "class",
      name: "NoSameMoneyCurrencyException",
      fullName: "SampleOperatorOverloading.NoSameMoneyCurrencyException",
      namespace: "SampleOperatorOverloading",
      ctor: NoSameMoneyCurrencyException,
      base: () => Exception.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is NoSameMoneyCurrencyException =>
        v instanceof NoSameMoneyCurrencyException,
    });
  }

  protected override get $typeName(): string {
    return "NoSameMoneyCurrencyException";
  }

  protected override get $typeNamespace(): string {
    return "SampleOperatorOverloading";
  }
}

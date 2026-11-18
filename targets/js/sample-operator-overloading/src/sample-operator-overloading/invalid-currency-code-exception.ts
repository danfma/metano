/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { ClassType } from "metano-runtime";
import { ArgumentException } from "metano-runtime/system/exceptions/argument-exception";

let InvalidCurrencyCodeException$rt: ClassType<InvalidCurrencyCodeException> | undefined;

export class InvalidCurrencyCodeException extends ArgumentException {
  override readonly name: string = "InvalidCurrencyCodeException";

  constructor(code: string) {
    super(`Unknown currency code '${code}'`, "code");
  }

  static override get $runtimeType(): ClassType<InvalidCurrencyCodeException> {
    return (InvalidCurrencyCodeException$rt ??= {
      kind: "class",
      name: "InvalidCurrencyCodeException",
      fullName: "SampleOperatorOverloading.InvalidCurrencyCodeException",
      namespace: "SampleOperatorOverloading",
      ctor: InvalidCurrencyCodeException,
      base: () => ArgumentException.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is InvalidCurrencyCodeException =>
        v instanceof InvalidCurrencyCodeException,
    });
  }

  protected override get $typeName(): string {
    return "InvalidCurrencyCodeException";
  }

  protected override get $typeNamespace(): string {
    return "SampleOperatorOverloading";
  }
}

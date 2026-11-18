import type { ClassType } from "metano-runtime";
import { Exception } from "metano-runtime/system/exceptions/exception";

let InvalidAmountError$rt: ClassType<InvalidAmountError> | undefined;

export class InvalidAmountError extends Exception {
  override readonly name: string = "InvalidAmountError";

  constructor(message: string) {
    super(message ?? "Exception of type 'InvalidAmountError' was thrown.");
  }

  static override get $runtimeType(): ClassType<InvalidAmountError> {
    return (InvalidAmountError$rt ??= {
      kind: "class",
      name: "InvalidAmountError",
      fullName: "InvalidAmountError",
      namespace: "",
      ctor: InvalidAmountError,
      base: () => Exception.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is InvalidAmountError => v instanceof InvalidAmountError,
    });
  }

  protected override get $typeName(): string {
    return "InvalidAmountError";
  }

  protected override get $typeNamespace(): string {
    return "";
  }
}

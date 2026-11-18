import type { ClassType } from "metano-runtime";
import { Exception } from "metano-runtime/system/exceptions/exception";

let DuplicateEntryException$rt: ClassType<DuplicateEntryException> | undefined;

export class DuplicateEntryException extends Exception {
  override readonly name: string = "DuplicateEntryException";

  constructor(name: string) {
    super(`Duplicate entry: ${name ?? ""}`);
  }

  static override get $runtimeType(): ClassType<DuplicateEntryException> {
    return (DuplicateEntryException$rt ??= {
      kind: "class",
      name: "DuplicateEntryException",
      fullName: "DuplicateEntryException",
      namespace: "",
      ctor: DuplicateEntryException,
      base: () => Exception.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is DuplicateEntryException => v instanceof DuplicateEntryException,
    });
  }

  protected override get $typeName(): string {
    return "DuplicateEntryException";
  }

  protected override get $typeNamespace(): string {
    return "";
  }
}

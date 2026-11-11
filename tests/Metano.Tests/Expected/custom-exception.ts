import { Exception } from "metano-runtime/system/exceptions/exception";

export class InvalidAmountError extends Exception {
  override readonly name: string = "InvalidAmountError";

  constructor(message: string) {
    super(message);
  }

  protected override get $typeName(): string {
    return "InvalidAmountError";
  }

  protected override get $typeNamespace(): string {
    return "";
  }
}

import { Exception } from "metano-runtime/system/exceptions/exception";

export class DuplicateEntryException extends Exception {
  override readonly name: string = "DuplicateEntryException";

  constructor(name: string) {
    super(`Duplicate entry: ${name}`);
  }

  protected override get typeNamespace(): string {
    return "";
  }
}

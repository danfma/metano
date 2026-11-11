import { Exception } from "metano-runtime/system/exceptions/exception";

export class DuplicateEntryException extends Exception {
  constructor(name: string) {
    super(`Duplicate entry: ${name}`);
  }
}
